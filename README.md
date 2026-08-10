# PiCommandStrip

PiCommandStrip is a context-aware touchscreen command strip. The ASP.NET Core host runs on a Windows PC, observes the foreground application, serves the dashboard, and executes a deliberately small allowlist of commands. The dashboard can run locally or, when LAN mode is explicitly enabled, in a Raspberry Pi browser.

Implemented features include:

- repository-local HTML, CSS, and JavaScript dashboard;
- `/health` HTTP endpoint and authenticated `/ws` native WebSocket endpoint;
- Windows foreground-application detection with change-only broadcasts;
- server-allowlisted `open_notepad` command with a per-connection cooldown;
- loopback-only development configuration and explicit LAN configuration; and
- focused xUnit tests for protocol, authentication, commands, and state changes.

Browser input cannot select a path, shell, script, arguments, or executable. LAN mode is intended only for a trusted Private network and currently uses unencrypted HTTP/WebSocket traffic.

## Prerequisites

- .NET SDK `10.0.302`, pinned by `global.json`
- Windows for foreground detection and Notepad execution

No Node.js, npm, frontend packages, or application NuGet packages are required. The test-only packages are `Microsoft.NET.Test.Sdk`, `xunit`, and `xunit.runner.visualstudio`.

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

The dashboard is sized for the Raspberry Pi display's 1024x600 landscape CSS viewport. Add `?layoutDebug=1` to the URL, or press `Ctrl+Shift+D`, to outline the major interface regions and show the current viewport dimensions and device-pixel ratio. The shortcut can also close the overlay. This mode is local presentation tooling and does not alter WebSocket messages or command behavior.

## Run for a Raspberry Pi client

Follow [the LAN setup guide](docs/lan-setup.md). LAN mode is not selected by `launchSettings.json`; it requires an explicit LAN-enabled setting, a concrete PC address, and the token supplied outside Git.

Do not create a shortcut directly to `bin\Release\net10.0\PiCommandStrip.App.exe`. That folder is a build output used by development and does not contain the deployable static dashboard files. Use `dotnet run` from the project directory while developing, or create a published deployment folder before making a shortcut.

## Available command

The dashboard sends only the fixed identifier `open_notepad`. The server maps it to a dedicated handler that starts Notepad from the Windows system directory with shell execution disabled and no arguments. Unknown identifiers execute nothing.

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
