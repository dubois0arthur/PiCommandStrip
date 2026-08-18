# PiCommandStrip

PiCommandStrip is a context-aware touchscreen command strip. The ASP.NET Core host runs on a Windows PC, observes the foreground application, serves the dashboard, and executes a deliberately small allowlist of commands. The dashboard can run locally or, when LAN mode is explicitly enabled, in a Raspberry Pi browser.

Implemented features include:

- repository-local HTML, CSS, and JavaScript dashboard;
- `/health` HTTP endpoint and authenticated `/ws` native WebSocket endpoint;
- Windows foreground-application detection with change-only broadcasts;
- Windows system media-session discovery and normalized change-only state broadcasts;
- Windows Core Audio output/application-session discovery with normalized change-only mixer state;
- capability-aware Windows media controls and a touch-oriented Now Playing interface;
- generic Default, Media, Browser / Research, Gaming, and Audio context profiles;
- automatic process-based context switching plus authenticated manual pinning;
- fixed server-allowlisted Notepad and media commands with a per-connection cooldown;
- loopback-only development configuration and explicit LAN configuration; and
- focused xUnit tests for protocol, authentication, commands, foreground state, context selection, normalized media state, and audio grouping/state changes.

Browser input cannot select a path, shell, script, arguments, or executable. LAN mode is intended only for a trusted Private network and currently uses unencrypted HTTP/WebSocket traffic.

## Prerequisites

- .NET SDK `10.0.302`, pinned by `global.json`
- Windows 10 version 2004 or later for foreground detection, system media sessions, Core Audio mixer state, and Notepad execution

No Node.js, npm, or frontend packages are required. The application references only stable `NAudio.Wasapi` 2.3.0 for classic Windows Core Audio device/session COM wrappers; it does not use NAudio playback, capture, codec, MIDI, DSP, or UI packages. Test-only packages are `Microsoft.NET.Test.Sdk`, `xunit`, and `xunit.runner.visualstudio`.

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

The dashboard is sized for the Raspberry Pi display's 1024x600 landscape CSS viewport. Add `?layoutDebug=1` to the URL, or press `Ctrl+Shift+D`, to outline the major interface regions and show the current viewport dimensions and device-pixel ratio. The shortcut can also close the overlay. For repeatable local layout checks, loopback URLs may also add `&layoutFixture=no-media` or `&layoutFixture=long-media`; these affect only rendered media state and cannot activate over a LAN hostname. This presentation tooling does not alter WebSocket messages or command behavior.

## Run for a Raspberry Pi client

Follow [the LAN setup guide](docs/lan-setup.md). LAN mode is not selected by `launchSettings.json`; it requires an explicit LAN-enabled setting, a concrete PC address, and the token supplied outside Git.

Do not create a shortcut directly to `bin\Release\net10.0-windows10.0.19041.0\PiCommandStrip.App.exe`. That folder is a build output used by development and does not contain the deployable static dashboard files. Use `dotnet run` from the project directory while developing, or create a published deployment folder before making a shortcut.

## Available commands

The server allowlist contains `open_notepad`, `media.play`, `media.pause`, `media.playPause`, `media.previous`, `media.next`, and `media.seek`. Notepad uses its dedicated fixed launcher. Media commands call only `IMediaSessionService`; seek is the sole parameterized command and accepts one validated millisecond position. Unknown identifiers execute nothing.

Each dashboard connection has a 750 ms command cooldown to prevent accidental double taps. Adjust it with `PiCommandStrip:Commands:CooldownMilliseconds` in external configuration (or `PiCommandStrip__Commands__CooldownMilliseconds` as an environment variable). Values from 100 through 10,000 ms are accepted.

## Context mappings

Automatic context selection reads the foreground process already observed by the Windows adapter. Add or change process names only in `PiCommandStrip:Contexts:ProcessMappings` in `src/PiCommandStrip.App/appsettings.json` (or an external configuration override). Matching is case-insensitive; `.exe` is optional.

The initial mappings are Spotify → Media and Firefox/Chrome/Edge → Browser / Research. Add game process names to the `gaming` array. Default is the implicit fallback, so it is not configured as a process mapping. Audio is available for manual selection but has no automatic integration yet.

The compact header opens System Details, where the context selector can pin any catalog context or return to Automatic. Home returns directly to Automatic and Media pins the Media context. A pin applies to all authenticated dashboards, survives WebSocket reconnects, and resets when the host process restarts.

## Windows media sessions

The host follows the current session selected by Windows System Media Transport Controls and publishes normalized metadata, playback state, timeline, supported-control flags, and an optional local artwork URL over WebSocket. Spotify and browser media such as YouTube can appear when those applications expose a Windows media session. Playback does not affect context selection: the existing foreground-process mappings remain authoritative.

Media is the primary workspace in Media context and in Default when no more useful context capability exists. Browser-owned media is also promoted while Browser / Research is active; when another context has higher-value content, the same state renders as a compact persistent strip. Both presentations share the same component logic, provide a deliberate no-art fallback, support capability-aware playback buttons and touch seeking, and advance progress locally between server corrections. Artwork is read only from the Windows media session, kept in a bounded one-entry memory cache, and served by the local host; Spotify Web API and external image requests are not used.

The normal interface is a compact status header, one dynamic workspace, and a small Home/Media/Audio/More navigation row. Foreground process is supporting metadata rather than the main panel. PID, context age, manual RTT, and manual context selection live in System Details. Prototype Notepad and latency cards are no longer primary controls; ordinary command outcomes appear as accessible transient feedback instead of permanent result panels.

## Windows audio mixer state

The host separately monitors the default multimedia output device and its Windows Core Audio render sessions. It publishes master output volume/mute plus grouped application entries containing process metadata, volume, mute, and active/inactive state. Media and audio remain separate: media describes content and playback controls, while audio describes render streams and volume.

Multiple sessions with the same recognizable process name are grouped into one application entry; metadata-poor sessions use Windows grouping/session identity and are never merged by display text alone. Raw Windows session identifiers remain server-side so the Audio page can target every member of a grouped application. Explicit system-sounds and expired sessions are omitted, but incomplete ordinary sessions remain visible.

The Audio destination provides touch-friendly master and application volume/mute controls. Slider changes are coalesced during a drag and always send a final release value; incoming `audio_state` remains authoritative. Application IDs are resolved against the current mixer state before the Windows-specific service changes any underlying sessions. Microphones, output-device switching, peak meters, routing, and equalization remain out of scope.

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
