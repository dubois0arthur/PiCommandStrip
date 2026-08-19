using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace PiCommandStrip.App.ResearchInbox;

public sealed class SqliteResearchInboxService : IResearchInboxService
{
    public const int DefaultPageSize = 20;
    public const int MaximumPageSize = 50;
    public const int MaximumSelectedTextLength = 1000;
    private const int MaximumTitleLength = 500;
    private const int MaximumSourceBrowserLength = 100;
    private const string EmptySelectionKey = "page";

    private readonly string _connectionString;
    private readonly TimeProvider _timeProvider;
    private readonly IResearchInboxStateBroadcaster _broadcaster;
    private readonly ILogger<SqliteResearchInboxService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ResearchInboxState _current;
    private bool _initialized;

    public SqliteResearchInboxService(
        string databasePath,
        TimeProvider timeProvider,
        IResearchInboxStateBroadcaster broadcaster,
        ILogger<SqliteResearchInboxService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
        _timeProvider = timeProvider;
        _broadcaster = broadcaster;
        _logger = logger;
        _current = new(0, 0, 0, "initialized", null, timeProvider.GetUtcNow());
    }

    public ResearchInboxState Current => Volatile.Read(ref _current);

    public async Task<ResearchSaveResult> SaveAsync(
        ResearchCapture capture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capture);
        var prepared = Prepare(capture);
        ResearchSaveResult result;
        ResearchInboxState? changedState = null;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = OpenInitializedConnection();
            var createdAt = _timeProvider.GetUtcNow();
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT OR IGNORE INTO research_items
                    (title, url, normalized_url, domain, selected_text, selection_key,
                     created_utc, source_browser, is_reviewed)
                VALUES
                    ($title, $url, $normalizedUrl, $domain, $selectedText, $selectionKey,
                     $createdUtc, $sourceBrowser, 0);
                """;
            insert.Parameters.AddWithValue("$title", prepared.Title);
            insert.Parameters.AddWithValue("$url", prepared.Url);
            insert.Parameters.AddWithValue("$normalizedUrl", prepared.NormalizedUrl);
            insert.Parameters.AddWithValue("$domain", prepared.Domain);
            insert.Parameters.AddWithValue("$selectedText", (object?)prepared.SelectedText ?? DBNull.Value);
            insert.Parameters.AddWithValue("$selectionKey", prepared.SelectionKey);
            insert.Parameters.AddWithValue("$createdUtc", createdAt.ToString("O", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$sourceBrowser", prepared.SourceBrowser);
            var wasCreated = insert.ExecuteNonQuery() == 1;

            using var select = connection.CreateCommand();
            select.CommandText = """
                SELECT id, title, url, normalized_url, domain, selected_text,
                       created_utc, source_browser, is_reviewed
                FROM research_items
                WHERE normalized_url = $normalizedUrl AND selection_key = $selectionKey;
                """;
            select.Parameters.AddWithValue("$normalizedUrl", prepared.NormalizedUrl);
            select.Parameters.AddWithValue("$selectionKey", prepared.SelectionKey);
            using var reader = select.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException("The saved research item could not be read back.");
            }

            result = new(ReadItem(reader), wasCreated);
            if (wasCreated)
            {
                changedState = RefreshState(connection, "saved", result.Item.Id);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changedState is not null)
        {
            await BroadcastSafelyAsync(changedState, cancellationToken);
        }
        return result;
    }

    public async Task<ResearchInboxPage> GetPageAsync(
        long? beforeId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (beforeId is <= 0)
        {
            throw new ResearchInboxValidationException("The page cursor must be a positive item ID.");
        }
        if (limit is < 1 or > MaximumPageSize)
        {
            throw new ResearchInboxValidationException($"The page size must be from 1 through {MaximumPageSize}.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, title, url, normalized_url, domain, selected_text,
                       created_utc, source_browser, is_reviewed
                FROM research_items
                WHERE $beforeId IS NULL OR id < $beforeId
                ORDER BY id DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$beforeId", (object?)beforeId ?? DBNull.Value);
            command.Parameters.AddWithValue("$limit", limit + 1);
            using var reader = command.ExecuteReader();
            var items = new List<ResearchItem>(limit + 1);
            while (reader.Read())
            {
                items.Add(ReadItem(reader));
            }

            var hasMore = items.Count > limit;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }
            return new(items, hasMore ? items[^1].Id : null, hasMore);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ResearchItem?> GetAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return null;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = OpenInitializedConnection();
            return ReadById(connection, id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> SetReviewedAsync(
        long id,
        bool isReviewed,
        CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return false;
        }

        ResearchInboxState? changedState = null;
        bool exists;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE research_items
                SET is_reviewed = $isReviewed
                WHERE id = $id AND is_reviewed <> $isReviewed;
                """;
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$isReviewed", isReviewed ? 1 : 0);
            var changed = command.ExecuteNonQuery() == 1;
            exists = changed || ReadById(connection, id) is not null;
            if (changed)
            {
                changedState = RefreshState(connection, "reviewed", id);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changedState is not null)
        {
            await BroadcastSafelyAsync(changedState, cancellationToken);
        }
        return exists;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return false;
        }

        ResearchInboxState? changedState = null;
        bool deleted;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var connection = OpenInitializedConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM research_items WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            deleted = command.ExecuteNonQuery() == 1;
            if (deleted)
            {
                changedState = RefreshState(connection, "deleted", id);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changedState is not null)
        {
            await BroadcastSafelyAsync(changedState, cancellationToken);
        }
        return deleted;
    }

    private SqliteConnection OpenInitializedConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragmas = connection.CreateCommand();
        pragmas.CommandText = "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON;";
        pragmas.ExecuteNonQuery();

        if (!_initialized)
        {
            using var initialize = connection.CreateCommand();
            initialize.CommandText = """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS research_items (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    title TEXT NOT NULL,
                    url TEXT NOT NULL,
                    normalized_url TEXT NOT NULL,
                    domain TEXT NOT NULL,
                    selected_text TEXT NULL,
                    selection_key TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    source_browser TEXT NOT NULL,
                    is_reviewed INTEGER NOT NULL DEFAULT 0 CHECK (is_reviewed IN (0, 1)),
                    tags_json TEXT NULL,
                    notes TEXT NULL,
                    content_type TEXT NULL,
                    export_state TEXT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ux_research_items_dedup
                    ON research_items(normalized_url, selection_key);
                CREATE INDEX IF NOT EXISTS ix_research_items_recent
                    ON research_items(id DESC);
                """;
            initialize.ExecuteNonQuery();
            EnsureOptionalColumn(connection, "tags_json", "TEXT NULL");
            _initialized = true;
            _current = ReadState(connection, _current.Revision, "initialized", null);
        }
        return connection;
    }

    private static void EnsureOptionalColumn(
        SqliteConnection connection,
        string columnName,
        string declaration)
    {
        using var inspect = connection.CreateCommand();
        inspect.CommandText = "PRAGMA table_info(research_items);";
        using var reader = inspect.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
            {
                return;
            }
        }
        reader.Close();

        using var migrate = connection.CreateCommand();
        migrate.CommandText = $"ALTER TABLE research_items ADD COLUMN {columnName} {declaration};";
        migrate.ExecuteNonQuery();
    }

    private ResearchInboxState RefreshState(SqliteConnection connection, string changeType, long itemId)
    {
        var state = ReadState(connection, _current.Revision + 1, changeType, itemId);
        Volatile.Write(ref _current, state);
        return state;
    }

    private ResearchInboxState ReadState(
        SqliteConnection connection,
        long revision,
        string changeType,
        long? changedItemId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*), COALESCE(SUM(CASE WHEN is_reviewed = 0 THEN 1 ELSE 0 END), 0)
            FROM research_items;
            """;
        using var reader = command.ExecuteReader();
        reader.Read();
        return new(
            revision,
            reader.GetInt32(0),
            reader.GetInt32(1),
            changeType,
            changedItemId,
            _timeProvider.GetUtcNow());
    }

    private static ResearchItem? ReadById(SqliteConnection connection, long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, title, url, normalized_url, domain, selected_text,
                   created_utc, source_browser, is_reviewed
            FROM research_items WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadItem(reader) : null;
    }

    private static ResearchItem ReadItem(SqliteDataReader reader) => new(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        reader.GetString(7),
        reader.GetInt64(8) == 1);

    private static PreparedCapture Prepare(ResearchCapture capture)
    {
        if (!ResearchUrlNormalizer.TryNormalize(capture.Url, out var uri, out var normalizedUrl))
        {
            throw new ResearchInboxValidationException("Only valid HTTP or HTTPS pages can be saved.");
        }

        var title = NormalizeSingleLine(capture.Title, MaximumTitleLength);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = uri!.Host;
        }
        var selectedText = NormalizeSelection(capture.SelectedText);
        var selectionKey = selectedText is null
            ? EmptySelectionKey
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selectedText)));
        var sourceBrowser = NormalizeSingleLine(capture.SourceBrowser, MaximumSourceBrowserLength);

        return new(
            title,
            uri!.AbsoluteUri,
            normalizedUrl,
            uri.Host,
            selectedText,
            selectionKey,
            string.IsNullOrWhiteSpace(sourceBrowser) ? "browser" : sourceBrowser);
    }

    private static string? NormalizeSelection(string? value)
    {
        var text = value?.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        return text.Length <= MaximumSelectedTextLength
            ? text
            : text[..MaximumSelectedTextLength];
    }

    private static string NormalizeSingleLine(string? value, int maximumLength)
    {
        var text = value?.Replace('\r', ' ').Replace('\n', ' ').Trim() ?? string.Empty;
        return text.Length <= maximumLength ? text : text[..maximumLength];
    }

    private async Task BroadcastSafelyAsync(
        ResearchInboxState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await _broadcaster.BroadcastAsync(state, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Research Inbox change notification failed with error type {ErrorType}",
                exception.GetType().Name);
        }
    }

    private sealed record PreparedCapture(
        string Title,
        string Url,
        string NormalizedUrl,
        string Domain,
        string? SelectedText,
        string SelectionKey,
        string SourceBrowser);
}
