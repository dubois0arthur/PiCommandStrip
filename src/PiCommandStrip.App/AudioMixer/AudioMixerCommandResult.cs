namespace PiCommandStrip.App.AudioMixer;

public sealed record AudioMixerCommandResult(bool Succeeded, string Message)
{
    public static AudioMixerCommandResult Success(string message) => new(true, message);

    public static AudioMixerCommandResult Failure(string message) => new(false, message);
}
