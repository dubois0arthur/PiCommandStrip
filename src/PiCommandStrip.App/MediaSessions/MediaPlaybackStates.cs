namespace PiCommandStrip.App.MediaSessions;

public static class MediaPlaybackStates
{
    public const string None = "none";
    public const string Closed = "closed";
    public const string Opened = "opened";
    public const string Changing = "changing";
    public const string Stopped = "stopped";
    public const string Playing = "playing";
    public const string Paused = "paused";
    public const string Unknown = "unknown";
}

public enum MediaPlaybackStatus
{
    Unknown,
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused
}
