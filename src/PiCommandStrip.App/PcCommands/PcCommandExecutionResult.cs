namespace PiCommandStrip.App.PcCommands;

public sealed record PcCommandExecutionResult(bool Succeeded, string Message)
{
    public static PcCommandExecutionResult Success(string message) => new(true, message);

    public static PcCommandExecutionResult Failure(string message) => new(false, message);
}
