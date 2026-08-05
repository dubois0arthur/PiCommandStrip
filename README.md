# PiCommandStrip

PiCommandStrip is a context-aware touchscreen command strip for a Windows PC. The eventual dashboard will run on a Raspberry Pi, but the current milestone runs the dashboard and ASP.NET Core server together on Windows.

The initial scaffold provides:

- a local HTML, CSS, and JavaScript dashboard;
- an ASP.NET Core health endpoint at `/health`; and
- a native WebSocket endpoint at `/ws` with a versioned JSON protocol, reconnecting browser client, and ping/pong measurement; and
- an xUnit project for automated tests.

Foreground-window detection and executable PC commands are intentionally not implemented yet. See [the product vision](docs/vision.md), [the initial architecture](docs/architecture.md), and [the WebSocket protocol](docs/protocol.md) for details.

## Prerequisites

- .NET SDK `10.0.302`, pinned by `global.json`
- A Windows development environment

No Node.js, npm, frontend packages, or application NuGet packages are required.

The test project uses these development-only NuGet packages:

- `Microsoft.NET.Test.Sdk` hosts test discovery and execution for `dotnet test`.
- `xunit` provides the test framework and assertions.
- `xunit.runner.visualstudio` connects xUnit to the .NET and Visual Studio test platform.

## Development commands

Run these commands from the repository root.

Restore dependencies:

```powershell
dotnet restore PiCommandStrip.sln
```

Build the complete solution:

```powershell
dotnet build PiCommandStrip.sln --no-restore
```

Run every automated test:

```powershell
dotnet test PiCommandStrip.sln --no-build
```

Start the application on a predictable local address:

```powershell
dotnet run --project src/PiCommandStrip.App/PiCommandStrip.App.csproj --no-build -- --urls http://localhost:5077
```

Open `http://localhost:5077` in a browser. The dashboard calls `http://localhost:5077/health`, connects to `ws://localhost:5077/ws`, and enables its Ping button automatically. Press `Ctrl+C` in the terminal to stop Kestrel gracefully.

## Project layout

```text
PiCommandStrip.sln
src/
  PiCommandStrip.App/       ASP.NET Core host and local dashboard
tests/
  PiCommandStrip.Tests/     Unit tests for application logic
docs/                       Product and architecture documentation
AGENTS.md                   Persistent contributor instructions
global.json                 Pinned .NET SDK selection
```
