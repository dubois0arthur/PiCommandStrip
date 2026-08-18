namespace PiCommandStrip.App.AudioMixer;

public static class AudioMixerTargetResolver
{
    public const int ApplicationIdLength = 64;

    public static bool IsValidApplicationId(string? applicationId) =>
        applicationId is { Length: ApplicationIdLength } &&
        applicationId.All(Uri.IsHexDigit);

    public static ApplicationAudioState? ResolveApplication(
        AudioState state,
        string applicationId)
    {
        if (!IsValidApplicationId(applicationId))
        {
            return null;
        }

        return state.Applications.FirstOrDefault(application =>
            string.Equals(
                application.ApplicationId,
                applicationId,
                StringComparison.Ordinal));
    }
}
