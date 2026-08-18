namespace PiCommandStrip.App.BrowserIntegration;

public static class BrowserConnectionStates
{
    public const string Connected = "connected";
    public const string Disconnected = "disconnected";
}

public sealed record BrowserState(
    string ConnectionState,
    string? BrowserType,
    string? SourceIdentifier,
    string? InstanceIdentifier,
    int? ActiveTabId,
    string? Url,
    string? HostName,
    string? PageTitle,
    string? SelectedText,
    bool? CanGoBack,
    bool? CanGoForward,
    DateTimeOffset LastUpdatedUtc)
{
    public bool IsConnected => ConnectionState == BrowserConnectionStates.Connected;

    public bool HasSameMeaningAs(BrowserState other) =>
        ConnectionState == other.ConnectionState &&
        BrowserType == other.BrowserType &&
        SourceIdentifier == other.SourceIdentifier &&
        InstanceIdentifier == other.InstanceIdentifier &&
        ActiveTabId == other.ActiveTabId &&
        Url == other.Url &&
        HostName == other.HostName &&
        PageTitle == other.PageTitle &&
        SelectedText == other.SelectedText &&
        CanGoBack == other.CanGoBack &&
        CanGoForward == other.CanGoForward;

    public static BrowserState Disconnected(DateTimeOffset timestampUtc) =>
        new(
            BrowserConnectionStates.Disconnected,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            timestampUtc);
}

public sealed record BrowserIdentity(
    string BrowserType,
    string SourceIdentifier,
    string InstanceIdentifier);

public sealed record BrowserTabObservation(
    int? ActiveTabId,
    string? Url,
    string? PageTitle,
    string? SelectedText,
    bool? CanGoBack,
    bool? CanGoForward);
