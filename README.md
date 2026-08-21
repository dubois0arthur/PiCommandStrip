# PiCommandStrip

PiCommandStrip is a context-aware touchscreen command strip. The ASP.NET Core host runs on a Windows PC, observes the foreground application, serves the dashboard, and executes a deliberately small allowlist of commands. The dashboard can run locally or, when LAN mode is explicitly enabled, in a Raspberry Pi browser.

Implemented features include:

- repository-local HTML, CSS, and JavaScript dashboard;
- `/health` HTTP endpoint and authenticated `/ws` native WebSocket endpoint;
- Windows foreground-application detection with change-only broadcasts;
- Windows system media-session discovery and normalized change-only state broadcasts;
- Windows Core Audio output-device/application-session discovery with normalized change-only mixer state and touch output selection;
- one-second normalized CPU/GPU/RAM telemetry with explicit partial/unavailable sensor state;
- capability-aware Windows media controls and a touch-oriented Now Playing interface;
- optional Spotify saved-state, shuffle, repeat, queue, and playback-device enrichment;
- optional Firefox active-tab and selected-text-presence enrichment through a loopback-only bridge;
- generic Default, Media, Browser / Research, Gaming, and Audio context profiles;
- automatic process-based context switching plus authenticated manual pinning;
- fixed server-allowlisted Notepad and media commands with a per-connection cooldown;
- loopback-only development configuration and explicit LAN configuration; and
- focused xUnit tests for protocol, authentication, commands, foreground state, context selection, normalized media state, and audio grouping/state changes.

Browser input cannot select a path, shell, script, arguments, or executable. LAN mode is intended only for a trusted Private network and currently uses unencrypted HTTP/WebSocket traffic.

## Prerequisites

- .NET SDK `10.0.302`, pinned by `global.json`
- Windows 10 version 2004 or later for foreground detection, system media sessions, Core Audio mixer state, and Notepad execution

No Node.js, npm, or frontend packages are required. The application uses stable `NAudio.Wasapi` 2.3.0 for classic Windows Core Audio device/session COM wrappers, `LibreHardwareMonitorLib` 0.9.6 for read-only CPU/GPU sensors that Windows does not expose through a reliable public temperature API, and `Microsoft.Data.Sqlite` for the local Research Inbox. PiCommandStrip enables only LibreHardwareMonitor CPU/GPU categories and does not use NAudio playback, capture, codec, MIDI, DSP, or UI packages. Test-only packages are `Microsoft.NET.Test.Sdk`, `xunit`, and `xunit.runner.visualstudio`.

## Generate the pre-shared token

Run these commands from the repository root in PowerShell:

```powershell
[byte[]]$tokenBytes = New-Object byte[] 32
$randomNumberGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomNumberGenerator.GetBytes($tokenBytes)
    $token = [Convert]::ToBase64String($tokenBytes)
} finally {
    $randomNumberGenerator.Dispose()
}

$token
dotnet user-secrets set "PiCommandStrip:Authentication:Token" "$token" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
```

Copy the displayed token to a password manager for entry on the Raspberry Pi. User Secrets are stored outside this repository and are loaded by the Development environment. Never add the token to `appsettings*.json`, JavaScript, documentation, or Git.

## Build and test

```powershell
dotnet restore PiCommandStrip.sln
dotnet build PiCommandStrip.sln --no-restore
dotnet test PiCommandStrip.sln --no-build
```

## Run locally on the Windows PC

Development mode explicitly binds only to `127.0.0.1:5077`:

```powershell
Remove-Item Env:PiCommandStrip__Network__ListenAddress -ErrorAction SilentlyContinue
Remove-Item Env:PiCommandStrip__Network__Port -ErrorAction SilentlyContinue
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src/PiCommandStrip.App/PiCommandStrip.App.csproj --no-build --launch-profile http
```

Open `http://localhost:5077`, enter the token, and press **Connect**. The page connects to `/ws` on the same origin. Press `Ctrl+C` to stop Kestrel gracefully.

The dashboard is sized for the Raspberry Pi display's 1024x600 landscape CSS viewport. Add `?layoutDebug=1` to the URL, or press `Ctrl+Shift+D`, to outline the major interface regions and show the current viewport dimensions and device-pixel ratio. The shortcut can also close the overlay. Repeatable loopback-only fixtures include `no-media`, `long-media`, `audio`, `media`, `default-media`, `default-no-media`, `browser-owned`, `browser-foreign`, `browser-selected`, `browser-disconnected`, `browser-long`, and `gaming`, selected with `&layoutFixture=...`. They alter only rendered state and cannot activate over a LAN hostname; protocol and command behavior remain unchanged.

## Run for a Raspberry Pi client

Follow [the LAN setup guide](docs/lan-setup.md). LAN mode is not selected by `launchSettings.json`; it requires an explicit LAN-enabled setting, a concrete PC address, and the token supplied outside Git.

Do not create a shortcut directly to `bin\Release\net10.0-windows10.0.19041.0\PiCommandStrip.App.exe`. That folder is a build output used by development and does not contain the deployable static dashboard files. Use `dotnet run` from the project directory while developing, or create a published deployment folder before making a shortcut.

## Available commands

The server allowlist contains the fixed `open_notepad`, `media.*`, `audio.*`, and optional `spotify.*` identifiers documented in [the protocol](docs/protocol.md). Notepad uses its dedicated launcher; generic media commands call only `IMediaSessionService`; audio commands call only `IAudioMixerService`; Spotify enrichment commands call only `ISpotifyService`. Unknown identifiers execute nothing.

Each dashboard connection has a 750 ms command cooldown to prevent accidental double taps. Adjust it with `PiCommandStrip:Commands:CooldownMilliseconds` in external configuration (or `PiCommandStrip__Commands__CooldownMilliseconds` as an environment variable). Values from 100 through 10,000 ms are accepted.

## Context mappings

Automatic context selection reads the foreground process already observed by the Windows adapter. Add or change process names only in `PiCommandStrip:Contexts:ProcessMappings` in `src/PiCommandStrip.App/appsettings.json` (or an external configuration override). Matching is case-insensitive; `.exe` is optional.

The initial mappings are Spotify → Media and Firefox/Chrome/Edge → Browser / Research. Add game process names to the `gaming` array. Default is the implicit fallback, so it is not configured as a process mapping. Audio is available for manual selection but has no automatic integration yet.

The compact header opens System Details, where the context selector can pin any catalog context or return to Automatic. Home returns directly to Automatic and Media pins the Media context. A pin applies to all authenticated dashboards, survives WebSocket reconnects, and resets when the host process restarts.

## Windows media sessions

The host follows the current session selected by Windows System Media Transport Controls and publishes normalized metadata, playback state, timeline, supported-control flags, and an optional local artwork URL over WebSocket. Spotify and browser media such as YouTube can appear when those applications expose a Windows media session. Playback does not affect context selection: the existing foreground-process mappings remain authoritative.

Media is the primary workspace in Media context and in Default when no more useful context capability exists. Browser-owned media is also promoted while Browser / Research is active; when another context has higher-value content, the same state renders as a compact persistent strip. Both presentations share the same component logic, provide a deliberate no-art fallback, support capability-aware playback buttons and touch seeking, and advance progress locally between server corrections. Artwork is still read only from the Windows media session, kept in a bounded one-entry memory cache, and served by the local host; Spotify enrichment never replaces this generic path or requests external artwork.

The normal interface is a compact status header, one dynamic workspace, and a small Home/Media/Audio/More navigation row. Foreground process is supporting metadata rather than the main panel. PID, context age, manual RTT, and manual context selection live in System Details. Prototype Notepad and latency cards are no longer primary controls; ordinary command outcomes appear as accessible transient feedback instead of permanent result panels.

Contexts compose the same media and audio capabilities rather than owning separate implementations. Media and media-owning Browser views add a matched application-volume row beneath expanded Now Playing; Gaming orders a compact mixer as foreground game, Discord, matched media, then other active sessions; Default promotes media when present and otherwise offers master output. Matching prefers exact normalized process/source names and the small known application-family map. Browser title matching is used only to establish ownership when Windows publishes an opaque media source. If a candidate is missing or non-unique, PiCommandStrip omits the inline volume control instead of risking the wrong application; the full Audio destination remains available.

## Optional Firefox browser bridge

The Firefox extension in `browser-extension/firefox` enriches Browser / Research context with the active page title, HTTP(S) URL/hostname, tab ID, navigation capability, and bounded selected text. It never sends page bodies or browsing history. Selected text is capped, kept only in memory, cleared on tab/navigation/disconnect changes, never logged, and rendered on the authenticated Pi as a strict 180-character preview only while Browser context is active.

Generate a separate browser-pairing token and store it in Windows User Secrets:

```powershell
[byte[]]$browserTokenBytes = New-Object byte[] 32
$browserRandom = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $browserRandom.GetBytes($browserTokenBytes)
    $browserPairingToken = [Convert]::ToBase64String($browserTokenBytes)
} finally {
    $browserRandom.Dispose()
}

$browserPairingToken
dotnet user-secrets set "PiCommandStrip:BrowserIntegration:Enabled" "true" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
dotnet user-secrets set "PiCommandStrip:BrowserIntegration:Port" "5078" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
dotnet user-secrets set "PiCommandStrip:BrowserIntegration:Token" "$browserPairingToken" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
```

Restart PiCommandStrip. In Firefox 147 or newer, open `about:debugging`, choose **This Firefox**, select **Load Temporary Add-on**, and choose `browser-extension/firefox/manifest.json`. Then open the extension's Preferences from `about:addons`, paste the browser-pairing token, leave port `5078`, and save. Temporary extensions are removed when Firefox restarts; a permanent distribution would require Mozilla signing and a review of its local insecure-WebSocket policy.

The extension connects only to `ws://127.0.0.1:5078/browser-integration/ws`. The server also verifies that both socket endpoints are loopback and that the WebSocket origin is an extension origin (`moz-extension://` now, with `chrome-extension://` reserved for a future Chromium producer). This endpoint is not reachable through the PiCommandStrip LAN listener, accepts only fixed typed browser commands from the Windows host, uses an 8 KiB receive cap and a separate failed-pairing limiter, and does not reuse the Pi dashboard token. Search providers are configured under `PiCommandStrip:BrowserIntegration:SearchActions`; templates remain on Windows and must be absolute HTTPS URLs containing exactly one `{query}` placeholder. See [the browser integration guide](docs/browser-integration.md) for permissions, privacy details, protocol fields, and verification steps.

Research context now includes an explicit one-touch Save flow and a bounded Research Inbox under More. Pages and explicitly selected passages are stored on the Windows host at `%LOCALAPPDATA%\PiCommandStrip\research-inbox.v1.db`; merely selecting text never writes it. Exact repeated page/passage saves deduplicate, distinct passages remain distinct, and Open resolves a stored ID through the Firefox bridge without accepting a Pi-supplied URL or invoking a shell. Inbox lists/details use same-origin bearer-authenticated HTTP while WebSocket messages carry only counts and change revisions. See [the protocol](docs/protocol.md) and [architecture](docs/architecture.md) for lifecycle and API details.

## Optional Spotify enrichment

Spotify support is disabled by default. It enriches only a Windows media session that explicitly identifies Spotify and whose title exactly matches Spotify's current Web API item. Like/unlike, shuffle, and repeat appear beside the existing Now Playing controls; queue and current Spotify device are secondary information. Generic Windows title, artist, artwork, timeline, and transport controls remain authoritative and work without Spotify configuration or during Spotify API errors/rate limits.

Create a Spotify app in the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard), add this exact redirect URI for the standard development port, and add your Spotify account to the app's Development Mode user allowlist:

```text
http://127.0.0.1:5077/spotify/auth/callback
```

Spotify requires an explicit loopback IP; `http://localhost:5077/...` is not interchangeable. Then store the host-only settings outside Git:

```powershell
dotnet user-secrets set "PiCommandStrip:Spotify:Enabled" "true" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
dotnet user-secrets set "PiCommandStrip:Spotify:ClientId" "<client-id>" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
dotnet user-secrets set "PiCommandStrip:Spotify:ClientSecret" "<client-secret>" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
dotnet user-secrets set "PiCommandStrip:Spotify:RedirectUri" "http://127.0.0.1:5077/spotify/auth/callback" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
```

Start PiCommandStrip in Development mode, then open `http://127.0.0.1:5077/spotify/auth/start` on the Windows PC and approve the four requested scopes. The OAuth endpoints deliberately return `404` to non-loopback callers, so the Raspberry Pi never handles Spotify credentials. After the success page appears, the refresh credential remains available to later host runs under the same Windows user.

PiCommandStrip explicitly loads its repository-external User Secrets for both Development and the named `Lan` environment, while Windows-host environment variables and command-line options keep higher precedence. The same settings therefore work with the normal LAN launcher under the same Windows account; do not put them in `appsettings.json`, a Pi startup script, or browser storage. The access token stays in Windows-host memory. The refresh token is encrypted with ASP.NET Core Data Protection in `%LOCALAPPDATA%\PiCommandStrip\spotify-authorization.v1`, with DPAPI-protected keys under `%LOCALAPPDATA%\PiCommandStrip\DataProtection-Keys`; the client secret remains in the Windows User Secrets store. Spotify refresh credentials currently expire after six months, so repeat the loopback authorization when required.

The requested scopes are exactly:

- `user-read-playback-state` — current Spotify item, queue, shuffle/repeat state, and current playback device;
- `user-modify-playback-state` — change shuffle and repeat;
- `user-library-read` — check whether the current item is saved; and
- `user-library-modify` — save or remove the current item.

Spotify's current Development Mode requires the app owner to have Premium, restricts a new app to one client ID and up to five allowlisted users, and applies a lower rolling rate quota. Player modification endpoints require Premium. The integration uses the server-side Authorization Code flow because the Windows host can keep a client secret; no secret or refresh credential is ever sent to browser JavaScript.

## Windows audio mixer state

The host separately monitors the default multimedia output device and its Windows Core Audio render sessions. It publishes master output volume/mute plus grouped application entries containing process metadata, volume, mute, and active/inactive state. Media and audio remain separate: media describes content and playback controls, while audio describes render streams and volume.

Multiple sessions with the same recognizable process name are grouped into one application entry; metadata-poor sessions use Windows grouping/session identity and are never merged by display text alone. Raw Windows session identifiers remain server-side so the Audio page can target every member of a grouped application. Explicit system-sounds and expired sessions are omitted, but incomplete ordinary sessions remain visible.

The Audio destination provides touch-friendly master and application volume/mute controls. Tapping the current output opens a compact list of active Windows playback endpoints; a selection is accepted only when its opaque ID still exists in the current mixer state, and the UI waits for authoritative confirmation. Slider changes are coalesced during a drag and always send a final release value; incoming `audio_state` remains authoritative. Application IDs are likewise resolved against current state before the Windows-specific service changes any underlying sessions. Microphones, per-application device routing, communications-device selection, peak meters, and equalization remain out of scope.

Windows publicly supports playback-endpoint enumeration and default-change observation, but it does not publish a desktop API for setting the system default. PiCommandStrip isolates the private PolicyConfig COM mechanism used by established Windows switchers, changes the Console and Multimedia roles, and leaves Communications unchanged. This is a best-effort Windows 10/11 integration: a future Windows update could require maintenance to that adapter. No extra package, shell command, registry path, or external switcher is used.

## System hardware telemetry

The Windows host publishes a separate read-only `system_telemetry` state about once per second. Overall CPU utilization comes from Windows system timing, physical RAM from Windows memory status, and CPU/GPU temperatures, GPU core utilization, and VRAM from the isolated LibreHardwareMonitor provider. The compact CPU/GPU/RAM readout lives in the existing navigation band, so normal workspaces lose no height. System Details shows the provider, selected sensors/GPU, and fixed unavailable reasons.

Default presentation thresholds are CPU 75 °C elevated / 90 °C warning and GPU 75 °C elevated / 85 °C warning. They are visual bands, not claims that the PC is dangerous. Adjust them outside Git with these configuration keys:

```text
PiCommandStrip:SystemTelemetry:CpuElevatedTemperatureCelsius
PiCommandStrip:SystemTelemetry:CpuWarningTemperatureCelsius
PiCommandStrip:SystemTelemetry:GpuElevatedTemperatureCelsius
PiCommandStrip:SystemTelemetry:GpuWarningTemperatureCelsius
```

Collection defaults to 1,000 ms and accepts 500–10,000 ms through `PiCommandStrip:SystemTelemetry:PollIntervalMilliseconds`. Multi-GPU selection normally prefers dedicated-memory evidence, then discrete AMD/NVIDIA hardware. To override it, set `PiCommandStrip:SystemTelemetry:PreferredGpu` to the exact identifier or exact name shown in System Details (for example `/gpu-amd/5`). Missing sensors remain unavailable; the UI never substitutes zero. Some LibreHardwareMonitor sensor access requires administrator rights or compatible firmware/drivers. PiCommandStrip continues with native CPU/RAM and any accessible GPU facts when a sensor is unavailable.

The current telemetry milestone deliberately excludes FPS, power, clocks, fans, per-core temperatures, processes, disk/network throughput, history, alerts, and control actions. A future Gaming milestone can promote the same compact component. FPS should be a separate game/performance signal (for example a carefully bounded PresentMon/ETW provider), not inferred from the one-second hardware-sensor loop.

## Project layout

```text
PiCommandStrip.sln
src/
  PiCommandStrip.App/       ASP.NET Core Windows host and dashboard
tests/
  PiCommandStrip.Tests/     Focused automated tests
docs/                       Vision, architecture, protocol, and LAN setup
AGENTS.md                   Persistent contributor instructions
global.json                 Pinned .NET SDK selection
```
