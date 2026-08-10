namespace PiCommandStrip.App.Contexts;

public static class ContextIds
{
    public const string Default = "default";
    public const string Media = "media";
    public const string Browser = "browser";
    public const string Gaming = "gaming";
    public const string Audio = "audio";
}

public static class ContextSelectionModes
{
    public const string Automatic = "automatic";
    public const string Manual = "manual";
}

public static class ContextSources
{
    public const string ForegroundProcess = "foreground_process";
    public const string Fallback = "fallback";
    public const string ManualOverride = "manual_override";
}
