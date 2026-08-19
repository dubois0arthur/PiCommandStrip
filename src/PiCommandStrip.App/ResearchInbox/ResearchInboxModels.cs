namespace PiCommandStrip.App.ResearchInbox;

public sealed record ResearchCapture(
    string? Title,
    string Url,
    string? SelectedText,
    string? SourceBrowser);

public sealed record ResearchItem(
    long Id,
    string Title,
    string Url,
    string NormalizedUrl,
    string Domain,
    string? SelectedText,
    DateTimeOffset CreatedAtUtc,
    string SourceBrowser,
    bool IsReviewed);

public sealed record ResearchInboxPage(
    IReadOnlyList<ResearchItem> Items,
    long? NextBeforeId,
    bool HasMore);

public sealed record ResearchSaveResult(ResearchItem Item, bool WasCreated);

public sealed record ResearchInboxState(
    long Revision,
    int TotalCount,
    int UnreviewedCount,
    string ChangeType,
    long? ChangedItemId,
    DateTimeOffset LastUpdatedUtc);

public sealed class ResearchInboxValidationException(string message) : Exception(message);

