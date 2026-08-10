# Architecture

## Scope

This document describes the architecture through the generic context/profile milestone. The ASP.NET Core host remains on Windows; the static dashboard can run locally or in a Pi browser. Protocol version 3 adds automatic context resolution and authenticated manual context selection while retaining the version 2 authentication, command, foreground-monitoring, reconnection, and LAN boundaries.

## System shape

Use a single ASP.NET Core host running on the Windows PC. It serves the static dashboard, accepts one native WebSocket endpoint, monitors the foreground window, dispatches allowlisted commands, and sends results back to the requesting dashboard.

```text
Windows PC
+------------------------------------------------------------------+
| ASP.NET Core host                                                |
|                                                                  |
|  Foreground monitor --> foreground store --> WebSocket endpoint  |
|       |                       |                    ^             |
|       v                       v                    |             |
|  Win32 adapter       context resolver --> context state <--------|
|                                                  <--> Dashboard  |
|                                  |                               |
|                                  v                               |
|                         command dispatcher                       |
|                                  |                               |
|                                  v                               |
|                       fixed command allowlist                    |
+------------------------------------------------------------------+
```

One process is enough for this milestone. Splitting the parts by responsibility makes them testable without turning them into separate services or deployable applications.

## Responsibilities

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
- Passes each meaningful foreground change to context coordination after the original raw `pc_state` publication.
- Accepts the host cancellation token and exits promptly during shutdown.

A **hosted service** is a component whose lifetime ASP.NET Core manages alongside the web server. A **cancellation token** is a cooperative stop signal: loops and asynchronous operations observe it and finish cleanly rather than being forcibly terminated.

### Context catalog, resolver, and state

- `ContextCatalog` owns the small fixed set of context IDs and display names: `default`, `media`, `browser`, `gaming`, and `audio`.
- `IContextResolver` accepts a `ContextSignals` value. Its first implementation, `ForegroundProcessContextResolver`, uses only the already-collected foreground process and returns Default when no process mapping matches.
- `PiCommandStrip:Contexts:ProcessMappings` is the single configuration map from context ID to process names. Process matching is case-insensitive and accepts configured `.exe` suffixes. Empty names, unknown context IDs, and a process assigned more than once fail startup instead of producing ambiguous behavior.
- `ContextStateStore` retains the latest foreground evidence, effective context, selection mode, source/trigger, and activation time. Its lock makes foreground polling and dashboard selection requests safe to apply concurrently.
- `ContextStateCoordinator` serializes updates with their broadcasts so automatic and manual changes reach clients in the order the server applies them.

The default mappings classify `spotify` as Media and `firefox`, `chrome`, and `msedge` as Browser / Research. Gaming is deliberately an empty configurable list. Audio is a defined profile that can be manually pinned, but it has no automatic signal or external integration yet.

`ContextSignals` is intentionally a small wrapper rather than making the resolver depend directly on Win32 detection. Future fields can represent media sessions, audio sessions, or a running game without changing the foreground adapter or WebSocket transport. Manual selection remains selection policy above automatic resolution; this is not a plugin framework.

### State and connection coordinator

- Retains the latest foreground and context states so a newly authenticated dashboard receives both immediate snapshots.
- Tracks active WebSocket connections needed for broadcasting.
- Serializes outbound sends per socket; WebSocket implementations should not be asked to perform overlapping sends on the same connection.
- Removes closed or failed connections and does not let one slow client block monitoring or shutdown indefinitely.

The implementation separates the latest-state stores from a `ConcurrentDictionary`-backed WebSocket registry. Each connection serializes its own sends and suppresses duplicate foreground and context snapshots. Broadcast sends have an independent two-second timeout, so a failed client is removed and aborted without preventing delivery to other clients.

### WebSocket endpoint and protocol

- Accepts WebSocket upgrades at `/ws`.
- Sends foreground-state events from server to dashboard.
- Receives command requests from dashboard to server.
- Returns a result correlated to the request.
- Rejects malformed, oversized, unknown, or directionally invalid messages.
- Observes cancellation and handles normal browser disconnects without reporting them as host failures.

The implemented version 3 protocol is specified in [protocol.md](protocol.md). A compatible and authenticated `client_hello` is required before state, selection, ping, or commands are accepted. Authentication sends retained `pc_state` and `context_state` snapshots. Later foreground changes can update both; manual selection updates `context_state` independently.

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
- Renders the resolved context alongside the current process name and window title.
- Populates its context selector from the server catalog and can pin a context or return to automatic selection.
- Sends only a fixed command identifier selected by a known UI control, plus a generated request ID for correlation.
- Displays pressed, disabled, processing, success, rejected, and failed command states without relying on hover.
- Reconnects with a bounded delay after an unexpected disconnect.
- Prompts for the pre-shared token and retains it only in browser `sessionStorage`; it is not part of the frontend source.
- Uses a 1024x600 landscape layout with large touch targets and no hover dependency.
- Uses a high-contrast cyberpunk telemetry theme built entirely from local CSS, system fonts, gradients, and static geometric patterns; it has no image, font, CDN, or animation dependency.
- Provides a developer layout overlay through `?layoutDebug=1` or `Ctrl+Shift+D`; the overlay reports the CSS viewport and device-pixel ratio without changing protocol behavior.

The browser code uses native JavaScript modules without a build step. `dashboard.js` coordinates startup and events, `protocol.js` owns WebSocket lifecycle and message correlation, and `ui.js` owns DOM rendering, context selection, clock/context-age updates, control availability, persistent feedback, and layout debugging. A manual pin lives in the Windows host, so reconnecting clients receive the authoritative current mode without storing profile state in the browser.

Client-side fixed buttons improve usability, but they are not a security boundary. The server must validate every message and enforce its own allowlist because browser traffic can be modified.

## Data contracts

The finalized, direction-specific UTF-8 JSON envelopes and payload examples are maintained in [protocol.md](protocol.md). The server validates the `type` discriminator and constructs only known data records; result text remains safe for display and never includes stack traces or sensitive paths.

## End-to-end data flow

### Foreground state

1. The hosted monitor asks `IForegroundWindowProvider` for the current foreground state every 250 milliseconds.
2. The Windows adapter calls the required Win32 APIs and maps the result to a small C# value.
3. The monitor compares the value with the last published raw foreground state.
4. When availability, process ID, process name, or title changes, it retains and broadcasts the original `pc_state` JSON message.
5. The monitor then gives the same typed observation to `ContextStateCoordinator`; no process-name policy exists in Win32 code or the polling service.
6. In automatic mode, the resolver selects a mapped profile or Default fallback. In manual mode, the pin stays effective while foreground process/title evidence continues to update.
7. Changed context state is retained and broadcast as `context_state`. Its activation timestamp changes only when the effective context ID changes, not for a title change or polling timestamp.
8. Dashboard JavaScript renders the active profile, selection mode, foreground details, and context age.
9. A newly authenticated dashboard receives both retained values immediately rather than waiting for the next window switch.

### Context selection

1. The dashboard builds its selector from `availableContexts` in `server_hello`.
2. Selecting a profile sends an authenticated `context_selection_request` in `manual` mode with one fixed catalog ID. Selecting Automatic sends the same request type with `automatic` mode and no context ID.
3. The parser enforces the exact direction-specific payload shape and field bounds.
4. The context store rejects unknown profile IDs without changing state.
5. A valid manual request pins that context globally for this host process. Foreground observations still refresh the evidence fields but do not change the profile.
6. A valid automatic request clears the pin and immediately resolves the latest retained foreground observation.
7. A changed state is broadcast to every authenticated client, and the requester receives a correlated `context_selection_result` even when the requested mode was already active.

Pins are intentionally in-memory and server-wide for this single-user, shared-token appliance. They reset to Automatic when the host restarts. Per-user persistence would require identity and storage requirements that are outside this milestone.

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
- configured process mappings resolve to the expected profile and unknown processes fall back to Default;
- automatic context changes, manual pinning, and returning to automatic mode preserve the expected state;
- cancellation stops the monitoring loop and command handlers.

The real Win32 adapter needs a small amount of focused integration or manual testing on Windows. Most other logic should depend on interfaces and plain data types so it can be tested deterministically.

## Dependency plan

No external application NuGet or frontend packages are currently required. ASP.NET Core hosting, dependency injection, logging, background services, JSON serialization, static files, and WebSockets are included in the .NET shared framework. The browser provides the WebSocket API and DOM APIs. Windows interop is available through .NET P/Invoke.

The installed stable .NET SDK is pinned with `global.json`. The test project uses `Microsoft.NET.Test.Sdk` to host test execution, `xunit` for the test API and assertions, and `xunit.runner.visualstudio` for test discovery through the .NET and Visual Studio test platform.

## LAN boundary

LAN mode is an explicit opt-in configuration. It binds only to the configured PC address, requires a random pre-shared token in `client_hello`, rate-limits failed authentication, and should be paired with a Windows Firewall rule limited to the Private profile and local subnet. See [lan-setup.md](lan-setup.md).

HTTPS remains deferred. HTTP and `ws` traffic, including the token, foreground titles, and commands, is visible to a network observer and can be modified by an active attacker. LAN mode is therefore suitable only for a trusted Private network and its port must never be forwarded or otherwise exposed to the internet.
