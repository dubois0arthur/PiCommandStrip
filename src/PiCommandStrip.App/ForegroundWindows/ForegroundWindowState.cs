namespace PiCommandStrip.App.ForegroundWindows;

public sealed record ForegroundWindowState(
    bool IsAvailable,
    string? ProcessName,
    int? ProcessId,
    string? WindowTitle,
    DateTimeOffset ObservedAtUtc)
{
    public static ForegroundWindowState Unavailable(DateTimeOffset observedAtUtc) =>
        new(false, null, null, null, observedAtUtc);

    public bool HasSameMeaningAs(ForegroundWindowState other) =>
        IsAvailable == other.IsAvailable &&
        ProcessId == other.ProcessId &&
        string.Equals(ProcessName, other.ProcessName, StringComparison.Ordinal) &&
        string.Equals(WindowTitle, other.WindowTitle, StringComparison.Ordinal);
}
