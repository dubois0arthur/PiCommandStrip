# Architecture

## Scope

This document describes the architecture through the context-aware touchscreen workspace and local media-artwork support. The ASP.NET Core host remains on Windows; the static dashboard can run locally or in a Pi browser. Protocol version 6 adds a bounded local artwork reference on top of the version 5 media controls. The dashboard hierarchy is a frontend-only refinement and retains authentication, foreground monitoring, context selection, reconnection, command security, and LAN boundaries.

## System shape

Use a single ASP.NET Core host running on the Windows PC. It serves the static dashboard, accepts one native WebSocket endpoint, monitors the foreground window, dispatches allowlisted commands, and sends results back to the requesting dashboard.

```text
Windows PC
+------------------------------------------------------------------+
| ASP.NET Core host                                                |
|                                                                  |
|  Win32 adapter --> foreground monitor --> foreground store ------|
|                         |                                        |
|                         +--> context resolver --> context store --+--> WebSocket <--> Dashboard
|                                                                  |
|  Windows media manager --> media service --> media store --------|
|                                  |                               |
|                                  +--> artwork cache --> HTTP -----|
|                                                                  |
|  fixed command allowlist <--> command dispatcher ----------------|
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

### Windows media-session service

- `IMediaSessionService` exposes the latest platform-neutral `MediaState`; consumers do not depend on WinRT types.
- `WindowsMediaSessionService` is the only component that imports `Windows.Media.Control`. It requests `GlobalSystemMediaTransportControlsSessionManager`, enumerates available sessions, and follows the session returned by `GetCurrentSession()`.
- Manager `SessionsChanged` and `CurrentSessionChanged` events handle applications appearing, disappearing, and Windows changing its preferred session. The selected session's media-properties, playback-info, and timeline events request a refreshed snapshot.
- `MediaStateNormalizer` converts incomplete platform data into nullable strings/timelines, fixed playback-state identifiers, clamped positions, and explicit capability flags.
- `MediaStateStore` retains the latest value and suppresses changes that differ only by observation timestamp. The WebSocket connection also performs per-client meaning-based deduplication.
- Media properties expose optional artwork as a WinRT `IRandomAccessStreamReference`. The service opens it only for an initial/current-session read or after `MediaPropertiesChanged`; the five-second position refresh reuses the current artwork ID.
- `MediaArtworkCache` accepts only supported raster MIME types, rejects empty or larger-than-5-MiB values, creates a SHA-256 content ID, and retains one defensive copy in memory. Replacing the current artwork or losing the session immediately evicts the previous bytes.
- `/media/artwork/{id}` serves only the cache entry matching a valid generated ID with `ETag`, private immutable caching, and `X-Content-Type-Options: nosniff`. It never reads a path, touches disk, or proxies an external URL.
- While a session is playing, a five-second timer refreshes the extrapolated timeline position. It does not poll the media API while paused or inactive; identity, metadata, playback, and capability changes remain event-driven.
- The same service implements fixed play, pause, play/pause, previous, next, and seek methods. Each operation asks the manager for Windows' current session again, reads live capability data, and catches session-disappearance races.
- Seeking accepts a normalized position relative to the displayed timeline. The Windows adapter verifies duration and advertised seek bounds, adds the session's timeline start, and passes the resulting ticks to `TryChangePlaybackPositionAsync`.

The Windows API may expose Spotify, packaged media players, and browser tabs such as YouTube when the application publishes System Media Transport Controls. It does not guarantee that every media application or browser configuration participates or supplies artwork. Missing metadata/artwork and session-level failures become partial or inactive state and are logged without terminating the host.

Media is a parallel global signal, not an input to the current foreground-process resolver. Every authenticated client receives it regardless of active context. Playback alone cannot force Media context: Spotify remains Media only when Spotify is foreground through the existing mapping, and a foreground browser remains Browser / Research while browser media is playing.

### State and connection coordinator

- Retains the latest foreground, context, and media states so a newly authenticated dashboard receives all three immediate snapshots.
- Tracks active WebSocket connections needed for broadcasting.
- Serializes outbound sends per socket; WebSocket implementations should not be asked to perform overlapping sends on the same connection.
- Removes closed or failed connections and does not let one slow client block monitoring or shutdown indefinitely.

The implementation separates the latest-state stores from a `ConcurrentDictionary`-backed WebSocket registry. Each connection serializes its own sends and suppresses duplicate foreground, context, and media snapshots. Broadcast sends have an independent two-second timeout, so a failed client is removed and aborted without preventing delivery to other clients.

### WebSocket endpoint and protocol

- Accepts WebSocket upgrades at `/ws`.
- Sends foreground-state events from server to dashboard.
- Receives command requests from dashboard to server.
- Returns a result correlated to the request.
- Rejects malformed, oversized, unknown, or directionally invalid messages.
- Observes cancellation and handles normal browser disconnects without reporting them as host failures.

The implemented version 6 protocol is specified in [protocol.md](protocol.md). A compatible and authenticated `client_hello` is required before state, selection, ping, or commands are accepted. Authentication sends retained `pc_state`, `context_state`, and `media_state` snapshots. Later foreground changes can update the first two, manual selection updates `context_state` independently, and Windows media events update `media_state` independently.

A **WebSocket** begins as an HTTP request and upgrades to a persistent, full-duplex connection. Either side can then send framed messages without repeated HTTP polling. Native WebSockets keep this milestone's transport visible and dependency-free.

### Command dispatcher and allowlist

- Accepts a parsed command identifier, not executable text.
- Looks up the identifier in a server-owned mapping of fixed handlers.
- Rejects identifiers that are not in the mapping.
- Invokes a narrow handler with a cancellation token.
- Produces a structured success or failure result suitable for display and logging.
- Applies the configured command cooldown (750 ms by default) independently to each WebSocket connection.

The allowlist is the central safety boundary. Browser-provided values must never become a shell command, executable path, script, or arbitrary argument list. Registered identifiers are `open_notepad`, `media.play`, `media.pause`, `media.playPause`, `media.previous`, `media.next`, and `media.seek`. Notepad still uses its narrow launcher. Media handlers operate only through `IMediaSessionService`; five carry no parameters, while seek accepts only one validated integer millisecond position. Adding any other command still requires a server code change and review.

`IPcCommandDispatcher` and `IPcCommandHandler` provide test seams. Handler failures are caught at the dispatcher boundary and converted to fixed browser-safe text; exception types may be logged, but exception messages and stack traces are not sent to the dashboard.

### Browser dashboard

- Uses repository-local plain HTML, CSS, and JavaScript.
- Uses three stable shell regions at the 1024x600 target: a 64-pixel status header, a flexible dynamic workspace, and a 68-pixel navigation row. The document itself does not scroll at the target viewport.
- Keeps active context, foreground process, connection/authentication state, measured round-trip latency, and local time in the compact header. Foreground process is supporting evidence rather than workspace content; its window title remains available as a tooltip and PID/context age move to diagnostics.
- Chooses the workspace presentation from existing context and media state. Media is expanded for Media context and for Default when no more valuable capability exists. Browser-owned media is expanded in Browser / Research; other contexts retain their own workspace with compact media below it.
- Treats browser media ownership conservatively: a recognizable source application is preferred, with foreground-window/media-title matching as a fallback for browsers that publish an opaque Windows AppUserModelId.
- Provides a reusable compact touch-action grid for context-owned commands. The current production grid contains only a System Details entry; prototype Notepad and manual latency actions no longer consume primary workspace area. Their server command/diagnostic behavior remains available without weakening the allowlist.
- Uses a compact bottom navigation seam for Home/Automatic, Media, and More/System. Unfinished Audio and other destinations are not exposed as fake pages. The context header and More button open the same diagnostics sheet, which contains manual context selection and technical details.
- Connects to the WebSocket endpoint and displays connecting, connected, disconnected, and retrying states.
- Renders one reusable Now Playing template/controller in two hosts: expanded as the primary workspace when media has the highest value, and compact beneath another context's higher-value workspace. Both variants receive the same normalized state, command callback, capability logic, artwork fallback, seek logic, and progress baseline. Keeping paused sessions visible preserves the user's path to resume playback.
- Shows prominent square-cropped artwork in Media context and a small compact thumbnail only when available. A deliberate local fallback occupies the expanded artwork region; text remains on an opaque surface so extreme artwork brightness cannot reduce readability.
- Displays generic media title, artist when available, source application, capability-aware transport controls, current/total time, and a touch-friendly seek range without assuming every item is music.
- Advances the displayed playing position locally with `requestAnimationFrame` from the latest server baseline. Five-second server refreshes correct drift; pausing or a new state resets the baseline.
- Populates the diagnostics context selector from the server catalog. Home sends the existing automatic-selection request, Media pins the existing Media context, and the diagnostics selector can pin any catalog context.
- Sends only a fixed command identifier selected by a known UI control, plus a generated request ID for correlation.
- Displays pressed, disabled, and processing control states without relying on hover. Ordinary successful results are short-lived accessible toasts; failures remain longer, and status text is also announced through a live region so meaning never depends on color alone.
- Reconnects with a bounded delay after an unexpected disconnect.
- Prompts for the pre-shared token and retains it only in browser `sessionStorage`; it is not part of the frontend source.
- Uses a 1024x600 landscape layout with large touch targets and no hover dependency.
- Uses a restrained cyberdeck telemetry theme built from local CSS, system fonts, low-contrast gradients, and a subtle grid. Optional media-session artwork is fetched only from the same PiCommandStrip host; there is no external image, font, CDN, or animation dependency.
- Provides a developer layout overlay through `?layoutDebug=1` or `Ctrl+Shift+D`; the overlay reports the CSS viewport and device-pixel ratio without changing protocol behavior. On loopback only, `layoutFixture=no-media` and `layoutFixture=long-media` can be combined with `layoutDebug=1` to inspect otherwise transient media layouts without altering server state or protocol messages.

The dynamic workspace is the extension seam for the upcoming Audio page. Context definitions select workspace copy/actions without creating separate page shells, the action grid accepts two or three compact controls as capabilities arrive, and compact media already composes beneath a higher-value surface. A future mixer can therefore supply master/device/application controls to the existing workspace and expose a smaller gaming subset without duplicating the header, navigation, feedback, or media systems.

The browser code uses native JavaScript modules without a build step. `dashboard.js` coordinates startup and events, `protocol.js` owns WebSocket lifecycle and message correlation, `ui.js` owns the overall shell, and `now-playing.js` owns reusable media rendering, local progress, and touch interaction. A manual pin lives in the Windows host, so reconnecting clients receive the authoritative current mode without storing profile state in the browser.

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
8. Dashboard JavaScript renders the active profile and compact foreground evidence in the header, then selects the most valuable workspace presentation from context and media state. PID and context age are available only in diagnostics.
9. A newly authenticated dashboard receives both retained values immediately rather than waiting for the next window switch.

### Media state

1. At host startup, `WindowsMediaSessionService` requests the system media-session manager and enumerates the sessions currently known to Windows.
2. The service asks Windows for its current session, attaches event handlers to that session, and reads source ID, metadata, playback info, timeline, supported controls, and the optional thumbnail stream.
3. Valid thumbnail bytes replace the one-entry in-memory artwork cache and produce a SHA-256 ID. No thumbnail, a failed read, an unsupported MIME type, an oversized image, or a session switch clears the cache.
4. Each platform value is mapped to `MediaSessionSnapshot`, then normalized into `MediaState`. Missing fields remain `null`; one broken property read does not discard other available data.
5. The store compares semantic fields, including artwork ID, and retains only meaningful changes. Changed state is broadcast as `media_state` with a small local artwork URL; a newly authenticated connection receives the retained state even when no media is active.
6. Manager events switch subscriptions when applications or the current session change. Media-property events permit a new thumbnail read. A five-second playing-only refresh advances position while reusing the current artwork ID.
7. Media state never calls the context coordinator. Context continues to resolve from foreground process or a manual pin.

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
2. JavaScript creates a unique request ID and sends a `command_request` containing its fixed command ID. Only `media.seek` also carries a non-negative integer position in milliseconds.
3. The endpoint checks message size, JSON shape, message type, and required fields.
4. The dispatcher looks up the command ID in its explicit allowlist. Media handlers then invoke only the corresponding `IMediaSessionService` method.
5. If absent, the dispatcher returns a rejected result and executes nothing.
6. If present, the associated narrow handler runs with cancellation support. Media commands re-check the current Windows session and live capabilities; seek additionally validates the live timeline and range.
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
- incomplete media metadata normalizes safely, positions are bounded, and reported capabilities are preserved;
- timestamp-only media refreshes are suppressed while meaningful metadata, playback, position, and active/inactive changes are retained;
- artwork cache IDs are stable for identical bytes, stale IDs are evicted, malformed path-like IDs are rejected, MIME types are constrained, and memory input is bounded;
- authenticated connections receive the current media snapshot;
- media command IDs dispatch only to their corresponding `IMediaSessionService` operations;
- invalid, missing, fractional, or unexpected seek parameters are rejected before dispatch;
- validated seek positions reach the media service as a `TimeSpan`, and session-disappearance failures remain safe command results;
- cancellation stops the monitoring loop and command handlers.

The real Win32 foreground adapter and WinRT media adapter need a small amount of focused integration or manual testing on Windows. Most other logic depends on interfaces and plain data types so it can be tested deterministically.

## Dependency plan

No explicit external application NuGet or frontend packages are required. The app and tests target `net10.0-windows10.0.19041.0`. On modern .NET, that Windows-specific target framework makes the SDK supply the Windows SDK reference/projection assets required to consume inbox WinRT APIs such as `Windows.Media.Control`; `Microsoft.Windows.SDK.Contracts`, `Microsoft.Windows.CsWinRT`, and Windows App SDK are not application dependencies. Foreground interop continues to use built-in .NET P/Invoke.

The installed stable .NET SDK is pinned with `global.json`. The test project uses `Microsoft.NET.Test.Sdk` to host test execution, `xunit` for the test API and assertions, and `xunit.runner.visualstudio` for test discovery through the .NET and Visual Studio test platform.

## LAN boundary

LAN mode is an explicit opt-in configuration. It binds only to the configured PC address, requires a random pre-shared token in `client_hello`, rate-limits failed authentication, and should be paired with a Windows Firewall rule limited to the Private profile and local subnet. See [lan-setup.md](lan-setup.md).

HTTPS remains deferred. HTTP and `ws` traffic, including the token, foreground titles, and commands, is visible to a network observer and can be modified by an active attacker. LAN mode is therefore suitable only for a trusted Private network and its port must never be forwarded or otherwise exposed to the internet.
