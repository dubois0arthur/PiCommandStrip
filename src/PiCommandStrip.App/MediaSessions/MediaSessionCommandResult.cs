namespace PiCommandStrip.App.MediaSessions;

public sealed record MediaSessionCommandResult(bool Succeeded, string Message)
{
    public static MediaSessionCommandResult Success(string message) => new(true, message);

    public static MediaSessionCommandResult Failure(string message) => new(false, message);
}
