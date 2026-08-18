namespace PiCommandStrip.App.Protocol;

public static class MessageTypes
{
    public const string ServerHello = "server_hello";
    public const string PcState = "pc_state";
    public const string ContextState = "context_state";
    public const string MediaState = "media_state";
    public const string SpotifyState = "spotify_state";
    public const string AudioState = "audio_state";
    public const string BrowserState = "browser_state";
    public const string ContextSelectionResult = "context_selection_result";
    public const string CommandResult = "command_result";
    public const string Pong = "pong";
    public const string Error = "error";

    public const string ClientHello = "client_hello";
    public const string ContextSelectionRequest = "context_selection_request";
    public const string CommandRequest = "command_request";
    public const string Ping = "ping";
}
