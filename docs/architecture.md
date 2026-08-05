# Initial architecture

## Scope

This document describes the planned architecture for the Windows-only first milestone. The ASP.NET Core host, static dashboard, health endpoint, and test project are scaffolded; the WebSocket and Windows-integration sections remain design guidance for later phases.

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

### Foreground-window adapter

- Hides Windows-specific calls behind an interface such as `IForegroundWindowReader`.
- Uses built-in platform interop to identify the foreground window, its owning process, and its title.
- Returns a small application model rather than exposing Win32 handles to the rest of the system.
- Handles windows that disappear, inaccessible processes, empty titles, and transient interop failures without crashing the monitor.

The likely Windows APIs are `GetForegroundWindow`, `GetWindowThreadProcessId`, and `GetWindowText`. C# calls native operating-system functions through **P/Invoke** (Platform Invocation Services). Keeping P/Invoke in one adapter limits platform-specific code and lets tests substitute a fake reader.

### Foreground monitor

- Runs as an ASP.NET Core hosted background service.
- Polls the adapter at a modest interval because Windows does not provide this application with a simple managed foreground-change stream.
- Publishes only meaningful changes to avoid sending identical state continuously.
- Accepts the host cancellation token and exits promptly during shutdown.

A **hosted service** is a component whose lifetime ASP.NET Core manages alongside the web server. A **cancellation token** is a cooperative stop signal: loops and asynchronous operations observe it and finish cleanly rather than being forcibly terminated.

### State and connection coordinator

- Retains the latest foreground state so a newly connected dashboard receives an immediate snapshot.
- Tracks active WebSocket connections needed for broadcasting.
- Serializes outbound sends per socket; WebSocket implementations should not be asked to perform overlapping sends on the same connection.
- Removes closed or failed connections and does not let one slow client block monitoring or shutdown indefinitely.

This coordination may begin as one small service. It should be split only if implementation or tests reveal distinct responsibilities.

### WebSocket endpoint and protocol

- Accepts WebSocket upgrades at a dedicated endpoint.
- Sends foreground-state events from server to dashboard.
- Receives command requests from dashboard to server.
- Returns a result correlated to the request.
- Rejects malformed, oversized, unknown, or directionally invalid messages.
- Observes cancellation and handles normal browser disconnects without reporting them as host failures.

A **WebSocket** begins as an HTTP request and upgrades to a persistent, full-duplex connection. Either side can then send framed messages without repeated HTTP polling. Native WebSockets keep this milestone's transport visible and dependency-free.

### Command dispatcher and allowlist

- Accepts a parsed command identifier, not executable text.
- Looks up the identifier in a server-owned mapping of fixed handlers.
- Rejects identifiers that are not in the mapping.
- Invokes a narrow handler with a cancellation token.
- Produces a structured success or failure result suitable for display and logging.

The allowlist is the central safety boundary. Browser-provided values must never become a shell command, executable path, script, or arbitrary argument list. Adding a command requires a code change and review on the PC side.

An interface such as `ICommandDispatcher` gives protocol tests a fake dispatcher, while individual handlers can remain simple classes or delegates until complexity justifies more structure.

### Browser dashboard

- Uses repository-local plain HTML, CSS, and JavaScript.
- Connects to the WebSocket endpoint and displays connecting, connected, disconnected, and retrying states.
- Renders the current process name and window title.
- Sends only a fixed command identifier selected by a known UI control, plus a generated request ID for correlation.
- Displays pending, success, rejected, and failed command outcomes.
- Reconnects with a bounded delay after an unexpected disconnect.
- Uses a 1024x600 landscape layout with large touch targets and no hover dependency.

Client-side fixed buttons improve usability, but they are not a security boundary. The server must validate every message and enforce its own allowlist because browser traffic can be modified.

## Data contracts

Use small UTF-8 JSON messages with a `type` discriminator. A discriminator is a field that tells the receiver which message shape follows. Keep server-to-client and client-to-server types distinct.

Illustrative foreground-state event:

```json
{
  "type": "foregroundChanged",
  "processName": "notepad",
  "windowTitle": "Notes.txt - Notepad"
}
```

Illustrative command request:

```json
{
  "type": "commandRequest",
  "requestId": "6c797f72-7a86-4d20-98fc-a5be66872e86",
  "commandId": "demo.safe-command"
}
```

Illustrative command result:

```json
{
  "type": "commandResult",
  "requestId": "6c797f72-7a86-4d20-98fc-a5be66872e86",
  "commandId": "demo.safe-command",
  "succeeded": true,
  "message": "Command completed."
}
```

The examples are a starting point. During implementation, contracts should be represented by explicit C# types, validated before dispatch, and documented when finalized. Result messages should be safe for display and should not expose stack traces or sensitive machine details.

## End-to-end data flow

### Foreground state

1. The hosted monitor asks `IForegroundWindowReader` for the current foreground state.
2. The Windows adapter calls the required Win32 APIs and maps the result to a small C# value.
3. The monitor compares the value with the last published state.
4. When it changes, the coordinator stores it and broadcasts a `foregroundChanged` JSON message.
5. Dashboard JavaScript parses the message and updates the process and title elements.
6. A newly connected dashboard receives the stored current value immediately rather than waiting for the next window switch.

### Command round trip

1. The user presses a known dashboard button.
2. JavaScript creates a unique request ID and sends a `commandRequest` containing only that ID and the button's fixed command ID.
3. The endpoint checks message size, JSON shape, message type, and required fields.
4. The dispatcher looks up the command ID in its explicit allowlist.
5. If absent, the dispatcher returns a rejected result and executes nothing.
6. If present, the associated narrow handler runs with cancellation support.
7. The endpoint sends a correlated `commandResult` to the requesting dashboard.
8. JavaScript matches the result by request ID and displays the outcome.

## Error handling and observability

- Log host lifecycle, WebSocket connect/disconnect events, foreground-monitor failures, command IDs, validation rejections, and command outcomes at appropriate levels.
- Do not log full arbitrary inbound payloads, stack traces to the browser, secrets, or more window-title content than is operationally necessary.
- Treat malformed client input as a request failure, not a host failure.
- Treat transient Windows inspection failures as recoverable and continue monitoring after a logged warning.
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

## Deferred network boundary

During this milestone, both browser and server run on the Windows PC. Moving the browser to the Raspberry Pi changes the trust boundary: the server must listen beyond loopback and traffic crosses the LAN. Authentication, authorization, TLS, origin policy, firewall configuration, and device provisioning must be designed before that deployment. The Windows-only prototype must not be presented as secure for untrusted networks.
