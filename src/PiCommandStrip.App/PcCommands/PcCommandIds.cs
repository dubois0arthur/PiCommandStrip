namespace PiCommandStrip.App.PcCommands;

public static class PcCommandIds
{
    public const string OpenNotepad = "open_notepad";
    public const string MediaPlay = "media.play";
    public const string MediaPause = "media.pause";
    public const string MediaPlayPause = "media.playPause";
    public const string MediaPrevious = "media.previous";
    public const string MediaNext = "media.next";
    public const string MediaSeek = "media.seek";
    public const string AudioSetMasterVolume = "audio.setMasterVolume";
    public const string AudioSetMasterMute = "audio.setMasterMute";
    public const string AudioSetApplicationVolume = "audio.setApplicationVolume";
    public const string AudioSetApplicationMute = "audio.setApplicationMute";
    public const string AudioSetOutputDevice = "audio.setOutputDevice";
    public const string SpotifySetSaved = "spotify.setSaved";
    public const string SpotifySetShuffle = "spotify.setShuffle";
    public const string SpotifySetRepeat = "spotify.setRepeat";
    public const string BrowserBack = "browser.back";
    public const string BrowserForward = "browser.forward";
    public const string BrowserReload = "browser.reload";
    public const string BrowserNewTab = "browser.newTab";
    public const string BrowserCloseTab = "browser.closeTab";
    public const string BrowserReopenClosedTab = "browser.reopenClosedTab";
    public const string BrowserCopyCurrentUrl = "browser.copyCurrentUrl";
    public const string BrowserSearchSelection = "browser.searchSelection";
    public const string ResearchSaveCurrent = "research.saveCurrent";
    public const string ResearchOpenItem = "research.openItem";

    public static IReadOnlyList<string> BrowserCommands { get; } =
    [
        BrowserBack,
        BrowserForward,
        BrowserReload,
        BrowserNewTab,
        BrowserCloseTab,
        BrowserReopenClosedTab,
        BrowserCopyCurrentUrl,
        BrowserSearchSelection
    ];
}
