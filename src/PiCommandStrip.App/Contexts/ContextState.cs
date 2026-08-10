namespace PiCommandStrip.App.Contexts;

public sealed record ContextState(
    string ContextId,
    string DisplayName,
    string SelectionMode,
    string Source,
    string Trigger,
    string? ForegroundProcess,
    string? ForegroundWindowTitle,
    DateTimeOffset ActiveSinceUtc)
{
    public bool HasSameMeaningAs(ContextState other) =>
        string.Equals(ContextId, other.ContextId, StringComparison.Ordinal) &&
        string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal) &&
        string.Equals(SelectionMode, other.SelectionMode, StringComparison.Ordinal) &&
        string.Equals(Source, other.Source, StringComparison.Ordinal) &&
        string.Equals(Trigger, other.Trigger, StringComparison.Ordinal) &&
        string.Equals(ForegroundProcess, other.ForegroundProcess, StringComparison.Ordinal) &&
        string.Equals(ForegroundWindowTitle, other.ForegroundWindowTitle, StringComparison.Ordinal);
}

public sealed record ContextSelectionUpdate(
    bool Succeeded,
    bool Changed,
    ContextState State,
    string Message);
