# Initial architecture

## Scope

This document describes the initial architecture through the first explicitly enabled Raspberry Pi LAN client. The ASP.NET Core host remains on Windows; the static dashboard can run locally or in a Pi browser. Version 2 adds pre-shared-token authentication without changing the command or foreground-monitoring architecture.

## System shape

Use a single ASP.NET Core host running on the Windows PC. It serves the static dashboard, accepts one native WebSocket endpoint, monitors the foreground window, dispatches allowlisted commands, and sends results back to the requesting dashboard.

```text
Windows PC
+------------------------------------------------------------------+
| ASP.NET Core host                                                |
|                                                                  |
|  Foreground monitor --> current-state coordinator                |
|       |                       |                                  |
|       v                       v                                  |
|  Win32 adapter          WebSocket endpoint <--> Browser dashboard|
|                                  |                               |
|                                  v                               |
|                         command dispatcher                       |
|                                  |                               |
|                                  v                               |
|                       fixed command allowlist                    |
+------------------------------------------------------------------+
```

One process is enough for this milestone. Splitting the parts by responsibility makes them testable without turning them into separate services or deployable applications.

## Planned responsibilities

### ASP.NET Core host

- Configures dependency injection, logging, static-file serving, and endpoint routing.
- Enables WebSockets and maps a dedicated endpoint such as `/ws`.
- Owns application startup and graceful shutdown.
- Serves all HTML, CSS, and JavaScript from the repository; no frontend asset is fetched from a CDN.
- Binds to `127.0.0.1:5077` by default. The separate `Lan` configuration requires an explicit non-loopback PC address and uses the configured fixed port without relying on `launchSettings.json`.
- Prints the usable dashboard URL after Kestrel starts.

### Foreground-window adapter

- Hides Windows-specific calls behind `IForegroundWindowProvider`.
- Uses built-in platform interop to identify the foreground window, its owning process, and its title.
- Returns a small application model rather than exposing Win32 handles to the rest of the system.
- Handles windows that disappear, inaccessible processes, empty titles, and transient interop failures without crashing the monitor.

The Windows implementation uses `GetForegroundWindow`, `GetWindowThreadProcessId`, `GetWindowTextLengthW`, and `GetWindowTextW`, followed by `Process.GetProcessById` for the process name. C# calls the native operating-system functions through **P/Invoke** (Platform Invocation Services). Keeping P/Invoke in one adapter limits platform-specific code and lets tests substitute a fake provider. Missing windows, process-exit races, inaccessible process metadata, and non-Windows hosts become an explicit unavailable observation.

### Foreground monitor

- Runs as an ASP.NET Core hosted background service.
- Polls the adapter every 250 milliseconds (approximately four times per second) because Windows does not provide this application with a simple managed foreground-change stream.
- Publishes only meaningful changes to avoid sending identical state continuously.
- Accepts the host cancellation token and exits promptly during shutdown.

A **hosted service** is a component whose lifetime ASP.NET Core manages alongside the web server. A **cancellation token** is a cooperative stop signal: loops and asynchronous operations observe it and finish cleanly rather than being forcibly terminated.

### State and connection coordinator

- Retains the latest foreground state so a newly connected dashboard receives an immediate snapshot.
- Tracks active WebSocket connections needed for broadcasting.
- Serializes outbound sends per socket; WebSocket implementations should not be asked to perform overlapping sends on the same connection.
- Removes closed or failed connections and does not let one slow client block monitoring or shutdown indefinitely.

The implementation separates the latest-state store from a `ConcurrentDictionary`-backed WebSocket registry. Each connection serializes its own sends. Broadcast sends have an independent two-second timeout, so a failed client is removed and aborted without preventing delivery to other clients.

### WebSocket endpoint and protocol

- Accepts WebSocket upgrades at `/ws`.
- Sends foreground-state events from server to dashboard.
- Receives command requests from dashboard to server.
- Returns a result correlated to the request.
- Rejects malformed, oversized, unknown, or directionally invalid messages.
- Observes cancellation and handles normal browser disconnects without reporting them as host failures.

The implemented version 2 protocol is specified in [protocol.md](protocol.md). A compatible and authenticated `client_hello` is required before foreground state, ping, or commands are accepted. Foreground-state publication then sends `pc_state` immediately and subsequently only when meaningful state changes.

A **WebSocket** begins as an HTTP request and upgrades to a persistent, full-duplex connection. Either side can then send framed messages without repeated HTTP polling. Native WebSockets keep this milestone's transport visible and dependency-free.

### Command dispatcher and allowlist

- Accepts a parsed command identifier, not executable text.
- Looks up the identifier in a server-owned mapping of fixed handlers.
- Rejects identifiers that are not in the mapping.
- Invokes a narrow handler with a cancellation token.
- Produces a structured success or failure result suitable for display and logging.
- Applies a two-second cooldown independently to each WebSocket connection.

The allowlist is the central safety boundary. Browser-provided values must never become a shell command, executable path, script, or arbitrary argument list. The only registered identifier is `open_notepad`. Its handler calls an `INotepadLauncher` that constructs the fixed Notepad location internally and disables shell execution. Adding a command requires a code change and review on the PC side.

`IPcCommandDispatcher` and `IPcCommandHandler` provide test seams. Handler failures are caught at the dispatcher boundary and converted to fixed browser-safe text; exception types may be logged, but exception messages and stack traces are not sent to the dashboard.

### Browser dashboard

- Uses repository-local plain HTML, CSS, and JavaScript.
- Keeps active context, connection state, measured round-trip latency, and local time in a persistent status header.
- Gives the foreground process visual priority, with the title, process ID, and elapsed context age nearby.
- Places large allowlisted action buttons in a dedicated touch-oriented action rail.
- Keeps the latest command result, highest-priority warning, and primary-view navigation override in a persistent utility rail.
- Connects to the WebSocket endpoint and displays connecting, connected, disconnected, and retrying states.
- Renders the current process name and window title.
- Sends only a fixed command identifier selected by a known UI control, plus a generated request ID for correlation.
- Displays pressed, disabled, processing, success, rejected, and failed command states without relying on hover.
- Reconnects with a bounded delay after an unexpected disconnect.
- Prompts for the pre-shared token and retains it only in browser `sessionStorage`; it is not part of the frontend source.
- Uses a 1024x600 landscape layout with large touch targets and no hover dependency.
- Uses a high-contrast cyberpunk telemetry theme built entirely from local CSS, system fonts, gradients, and static geometric patterns; it has no image, font, CDN, or animation dependency.
- Provides a developer layout overlay through `?layoutDebug=1` or `Ctrl+Shift+D`; the overlay reports the CSS viewport and device-pixel ratio without changing protocol behavior.

The browser code uses native JavaScript modules without a build step. `dashboard.js` coordinates startup and events, `protocol.js` owns WebSocket lifecycle and message correlation, and `ui.js` owns DOM rendering, clock/context-age updates, control availability, persistent feedback, and layout debugging.

Client-side fixed buttons improve usability, but they are not a security boundary. The server must validate every message and enforce its own allowlist because browser traffic can be modified.

## Data contracts

The finalized, direction-specific UTF-8 JSON envelopes and payload examples are maintained in [protocol.md](protocol.md). The server validates the `type` discriminator and constructs only known data records; result text remains safe for display and never includes stack traces or sensitive paths.

## End-to-end data flow

### Foreground state

1. The hosted monitor asks `IForegroundWindowProvider` for the current foreground state every 250 milliseconds.
2. The Windows adapter calls the required Win32 APIs and maps the result to a small C# value.
3. The monitor compares the value with the last published state.
4. When availability, process ID, process name, or title changes, the coordinator retains that observation and broadcasts a `pc_state` JSON message.
5. Dashboard JavaScript parses the message and updates availability, process name, process ID, title, and time last changed.
6. A newly connected dashboard receives the stored current value immediately rather than waiting for the next window switch.

### Command round trip

1. The user presses a known dashboard button.
2. JavaScript creates a unique request ID and sends a `command_request` containing only that ID and the button's fixed command ID.
3. The endpoint checks message size, JSON shape, message type, and required fields.
4. The dispatcher looks up the command ID in its explicit allowlist.
5. If absent, the dispatcher returns a rejected result and executes nothing.
6. If present, the associated narrow handler runs with cancellation support.
7. The endpoint sends a correlated `command_result` to the requesting dashboard.
8. JavaScript matches the result by request ID and displays the outcome.

## Error handling and observability

- Log host lifecycle, WebSocket connect/disconnect events, foreground-monitor failures, command IDs, validation rejections, and command outcomes at appropriate levels.
- Do not log full arbitrary inbound payloads, stack traces to the browser, secrets, or more window-title content than is operationally necessary.
- Treat malformed client input as a request failure, not a host failure.
- Treat expected Windows inspection races as recoverable, log them at debug level, and continue monitoring.
- Put sensible bounds on inbound message size and command duration.
- On host shutdown, stop polling, stop accepting work, cancel active operations, close sockets when practical, and let the ASP.NET Core host exit cleanly.

## Test seams

The first automated tests should focus on behavior that can run without an interactive Windows desktop:

- known command IDs dispatch to the expected handler;
- unknown command IDs are rejected and invoke nothing;
- malformed and unsupported protocol messages are rejected;
- command results preserve request correlation;
- foreground changes publish once while duplicate samples do not;
- cancellation stops the monitoring loop and command handlers.

The real Win32 adapter needs a small amount of focused integration or manual testing on Windows. Most other logic should depend on interfaces and plain data types so it can be tested deterministically.

## Dependency plan

No external application NuGet or frontend packages are currently required. ASP.NET Core hosting, dependency injection, logging, background services, JSON serialization, static files, and WebSockets are included in the .NET shared framework. The browser provides the WebSocket API and DOM APIs. Windows interop is available through .NET P/Invoke.

The installed stable .NET SDK is pinned with `global.json`. The test project uses `Microsoft.NET.Test.Sdk` to host test execution, `xunit` for the test API and assertions, and `xunit.runner.visualstudio` for test discovery through the .NET and Visual Studio test platform.

## LAN boundary

LAN mode is an explicit opt-in configuration. It binds only to the configured PC address, requires a random pre-shared token in `client_hello`, rate-limits failed authentication, and should be paired with a Windows Firewall rule limited to the Private profile and local subnet. See [lan-setup.md](lan-setup.md).

HTTPS remains deferred. HTTP and `ws` traffic, including the token, foreground titles, and commands, is visible to a network observer and can be modified by an active attacker. LAN mode is therefore suitable only for a trusted Private network and its port must never be forwarded or otherwise exposed to the internet.
