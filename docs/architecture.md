# Architecture

## Scope

This document describes the architecture through the optional Spotify enrichment layer, touchscreen Windows audio mixer and output selector, context-aware Research workspace, local media-artwork support, and Firefox browser bridge. The ASP.NET Core host remains on Windows; the static dashboard can run locally or in a Pi browser. Protocol version 12 adds constrained browser research actions and selected-text preview while retaining generic Windows media authority, authentication, reconnection, command security, and LAN boundaries.

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
|  optional Spotify Web API --> Spotify service/store -------------|
|             ^                                                    |
|             +-- encrypted host refresh credential                |
|                                                                  |
|  Windows Core Audio --> audio mixer service --> audio store ------|
|                                                                  |
|  Firefox extension -- loopback-only bridge --> browser store -----|
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

### Optional Spotify enrichment service

- `ISpotifyService` is an optional application seam for Spotify-only data and mutations. `IMediaSessionService` remains the source of title, artist, artwork, progress, transport capabilities, and play/pause/previous/next.
- `SpotifyService` does no work when disabled and calls the Web API only while the current Windows media source explicitly identifies Spotify. It reads playback at a ten-second active cadence and refreshes saved state/queue only when the item changes or after thirty seconds. Spotify rate limits and failures use slower retry timing.
- A Spotify response applies to the visible media only when the generic source is Spotify and normalized titles match exactly. The browser never performs this join and never receives the Spotify item URI. Ambiguous or non-Spotify media gets no Spotify controls.
- `SpotifyStateStore` and per-connection comparison suppress timestamp-only duplicates. A new authenticated client receives the retained `spotify_state`; other clients receive meaningful changes only.
- Server-side Authorization Code flow is exposed through loopback-only `/spotify/auth/start` and `/spotify/auth/callback`. A random, one-use, ten-minute OAuth state protects the callback. The redirect uses an explicit loopback IP because Spotify no longer accepts `localhost` aliases.
- Client ID, client secret, and redirect configuration remain in Windows host configuration. The access token exists only in host memory. The refresh token is encrypted with ASP.NET Core Data Protection and stored under the Windows user's Local Application Data `PiCommandStrip` folder, outside the repository. No Spotify credential is sent to the Pi or logged.
- The client secret is required because PiCommandStrip is a confidential Windows-host application. No third-party OAuth package is needed: ASP.NET Core Data Protection, `HttpClient`, and `System.Text.Json` cover the narrow flow and REST contracts.
- The allowlisted `spotify.setSaved`, `spotify.setShuffle`, and `spotify.setRepeat` handlers call only `ISpotifyService`. The service re-checks the confident current-item match and device restriction before every mutation. Failures return fixed browser-safe text and do not touch generic media state.

The minimum scopes are `user-read-playback-state` (current item, device, queue, shuffle/repeat), `user-modify-playback-state` (shuffle/repeat mutations), `user-library-read` (saved status), and `user-library-modify` (save/remove). Generic Windows media sessions remain fully functional when Spotify is disabled, unauthenticated, unavailable, expired, or rate-limited.

### Windows audio-mixer service

- `IAudioMixerService` exposes the latest platform-neutral `AudioState`; protocol and future UI code do not depend on NAudio or COM types.
- `WindowsAudioMixerService` is the only PiCommandStrip component that imports `NAudio.CoreAudioApi`. It owns active render-endpoint enumeration, the default multimedia endpoint, endpoint-volume object, audio-session manager, and per-session event registrations.
- `NAudio.Wasapi` 2.3.0 is the single application package used for classic Windows Core Audio COM projection. PiCommandStrip uses only device/session discovery, identity/state, `IAudioEndpointVolume`, `ISimpleAudioVolume`, and notifications—never capture, playback, codecs, DSP, or routing.
- Endpoint volume notifications and per-session volume, mute, display-name, state, and disconnect notifications request immediate refreshes. Session-created notifications request reconciliation. Callback threads only signal the service; state reads and resource changes remain serialized in the background loop.
- A two-second fallback check refreshes active playback devices, detects default-output and hot-plug changes, and recovers missed state events. A fifteen-second full reconciliation catches missed session creation/removal notifications without high-frequency enumeration.
- Every session wrapper and event registration is explicitly detached/disposed when its session expires, the default device changes, or the host stops. Individual COM/session failures are caught and omitted from that observation rather than terminating PiCommandStrip.
- `AudioStateNormalizer` clamps and rounds scalar volumes, removes only explicit Windows system-sounds and expired sessions, groups safe application peers, and produces deterministic ordering/identifiers. Missing process or display metadata remains a visible `Unknown audio session` entry.
- `AudioStateStore` assigns a monotonically increasing in-process revision only to meaningful changes. Timestamp-only observations and sub-0.001 volume noise do not broadcast.
- Volume, mute, and output-selection operations enter a single-reader command queue owned by `WindowsAudioMixerService`. WebSocket threads never access mutable COM wrappers or the session dictionary directly. The service resolves application/device IDs against a fresh normalized observation and observes once more before broadcasting authoritative state.
- `IDefaultAudioOutputDeviceSwitcher` isolates the only unsupported Windows detail. Windows publicly documents endpoint enumeration/default observation but does not publish a desktop API for setting the system default. `WindowsDefaultAudioOutputDeviceSwitcher` therefore uses the private PolicyConfig COM interface used by established desktop switchers, sets Console and Multimedia roles, and leaves Communications unchanged. The adapter is best-effort on the project's Windows 10 2004+ target and may require maintenance if Microsoft changes that private interface.
- After a device-selection attempt, the worker pauses later queued mixer mutations, disposes the old endpoint/session wrappers, resolves the new default, rebuilds master/session state, and only then continues. A device that vanishes between validation and PolicyConfig becomes a fixed failed result; the browser never receives the COM error.
- Stale application IDs, lost output devices, and sessions disappearing during a grouped update return fixed failed results. COM details are logged locally and never copied into browser result text.

Media and audio remain separate even when both describe Spotify or a browser. A media session answers “what is playing?” and follows the one session Windows considers current; it contains title, artist, artwork, timeline, and transport capabilities. An audio session answers “which render streams exist on this output?” and contains endpoint, volume, mute, process, and activity state. There may be many audio sessions for one media application, and many audio sessions with no media metadata at all.

### Optional Firefox browser integration

- `IBrowserIntegrationService` exposes normalized browser state without making context, protocol, or frontend code depend on WebExtension details.
- A separate Kestrel listener is added only when browser integration is enabled. It uses `ListenLocalhost` on the configured bridge port (5078 by default). `/browser-integration/ws` additionally verifies loopback local/remote addresses, the exact listener port, and a WebExtension origin (`moz-extension://`, with `chrome-extension://` reserved for a future producer); the route returns `404` through the dashboard/LAN listener.
- The bridge has its own 32-byte Base64 pairing token and failed-authentication limiter. It does not accept the Pi token, and the extension never receives the Pi token. Pairing timestamps are fresh-only and tokens are compared in constant time.
- The bridge protocol is independently versioned at `2`. Extension-to-host messages are limited to exact `browser_hello`, `browser_state_update`, and `browser_command_result` shapes with an 8 KiB receive limit. The host can send only a typed `browser_command`; one channel-level send lock prevents command/error sends from overlapping on a socket.
- `BrowserStateNormalizer` accepts only HTTP/HTTPS URLs, removes URI user information and fragments, derives an IDN-normalized hostname, and bounds title/URL/selection fields. Selected-text truncation avoids leaving an unmatched UTF-16 high surrogate.
- Selected text is never logged or persisted. Protocol version 12 sends it only to authenticated Pi clients while Browser context is active, where `research-workspace.js` normalizes whitespace and limits its rendered preview to 180 characters.
- `BrowserIntegrationService` serializes connection/update/disconnect transitions. The newest authenticated Firefox instance becomes authoritative, and an old socket cannot clear newer state when it eventually disconnects.
- `BrowserStateStore` suppresses timestamp-only/duplicate changes. Connect, meaningful tab/title/URL/selection changes, and disconnect are broadcast to authenticated Pi clients; disconnect clears all tab and selection data.
- `IBrowserCommandService` is the only PC-command handler dependency for `browser.*`. It checks live connection/tab/capability/selection state, constructs searches through `BrowserSearchCatalog`, and delegates a fixed extension command through `IBrowserIntegrationService`.
- Search configuration is data, not executable behavior: every action has a safe provider ID, bounded display name, and exactly one absolute HTTPS template placeholder. The dashboard receives only provider descriptors and sends only the chosen ID. The Windows host performs URL encoding and the extension independently rejects non-HTTPS or credential-bearing search URLs.
- Tab-specific commands include the retained tab ID. Firefox re-queries the active tab immediately before executing and returns `stale_tab` on a mismatch. New-tab/reopen actions are intentionally tab-independent. Bridge timeouts, disconnects, stale tabs, unavailable navigation, clipboard failure, and extension exceptions become fixed browser-safe command results.

Foreground-window detection and context resolution remain unchanged. Foreground Firefox still selects Browser / Research through process configuration; browser state only enriches that workspace. If the extension is disabled, unpaired, asleep, or disconnected, Browser context and every existing media/audio feature continue to work.

The Firefox extension uses Manifest V3 `background.scripts`, `tabs`, `storage`, `sessions`, `clipboardWrite`, and top-frame HTTP/HTTPS content scripts. It observes active-tab, Navigation API, and selection events rather than polling. Navigation capabilities remain nullable on restricted/unsupported pages and unknown is disabled. The bridge remains a small Firefox adapter, not a plugin framework.

Application grouping uses the following priority:

1. A normalized process name groups ordinary sessions, including multiple process IDs for applications such as browsers.
2. Without a process name, a non-empty Windows grouping GUID is used.
3. Without that, the Windows session identifier is used.
4. The session-instance identifier is the final per-session fallback. Display name alone is never considered safe evidence for merging.

One `ApplicationAudioState` therefore carries a stable hashed `ApplicationId`, zero or more process IDs, user-facing metadata, aggregate state, session count, mixed-volume/mute flags, and the underlying session-instance IDs retained only on the server. The displayed volume is the maximum member volume so an audible member is not hidden; displayed mute is true only when every member is muted. Audio commands resolve the application ID back to every current underlying session and set them together. Raw session identifiers—which can contain implementation details—are not serialized to the browser.

Peak level is deliberately omitted from the current protocol. Core Audio exposes inexpensive meters, but a useful meter requires frequent sampling and would conflict with change-only WebSocket state. It can be added later as a separately paced signal if the physical mixer UI demonstrates that need.

### State and connection coordinator

- Retains the latest foreground, context, media, audio, optional Spotify, and optional browser states so a newly authenticated dashboard receives immediate snapshots.
- Tracks active WebSocket connections needed for broadcasting.
- Serializes outbound sends per socket; WebSocket implementations should not be asked to perform overlapping sends on the same connection.
- Removes closed or failed connections and does not let one slow client block monitoring or shutdown indefinitely.

The implementation separates the latest-state stores from a `ConcurrentDictionary`-backed WebSocket registry. Each connection serializes its own sends and suppresses duplicate foreground, context, media, audio, and Spotify snapshots. Broadcast sends have an independent two-second timeout, so a failed client is removed and aborted without preventing delivery to other clients.

### WebSocket endpoint and protocol

- Accepts WebSocket upgrades at `/ws`.
- Sends foreground-state events from server to dashboard.
- Receives command requests from dashboard to server.
- Returns a result correlated to the request.
- Rejects malformed, oversized, unknown, or directionally invalid messages.
- Observes cancellation and handles normal browser disconnects without reporting them as host failures.

The implemented version 12 protocol is specified in [protocol.md](protocol.md). A compatible and authenticated `client_hello` is required before state, selection, ping, or commands are accepted. Authentication sends retained `pc_state`, `context_state`, `media_state`, `audio_state`, `spotify_state`, and `browser_state` snapshots. Spotify and browser state are enrichment only; Windows foreground/media/audio events continue to update independently.

A **WebSocket** begins as an HTTP request and upgrades to a persistent, full-duplex connection. Either side can then send framed messages without repeated HTTP polling. Native WebSockets keep this milestone's transport visible and dependency-free.

### Command dispatcher and allowlist

- Accepts a parsed command identifier, not executable text.
- Looks up the identifier in a server-owned mapping of fixed handlers.
- Rejects identifiers that are not in the mapping.
- Invokes a narrow handler with a cancellation token.
- Produces a structured success or failure result suitable for display and logging.
- Applies the configured command cooldown (750 ms by default) independently to ordinary commands on each WebSocket connection. Continuously coalesced audio volume commands use a separate 40 ms safety gate.

The allowlist is the central safety boundary. Browser-provided values must never become a shell command, executable path, script, arbitrary URL, or arbitrary argument list. Registered identifiers are `open_notepad`, the six `media.*` controls, five `audio.*` controls, three Spotify controls, and eight fixed `browser.*` actions. Notepad still uses its narrow launcher. Media handlers operate only through `IMediaSessionService`; audio handlers only through `IAudioMixerService`; Spotify handlers only through `ISpotifyService`; browser handlers only through `IBrowserCommandService`. The only browser payload value is a safe configured search-provider ID for `browser.searchSelection`. Adding any other command still requires a server code change and review.

`IPcCommandDispatcher` and `IPcCommandHandler` provide test seams. Handler failures are caught at the dispatcher boundary and converted to fixed browser-safe text; exception types may be logged, but exception messages and stack traces are not sent to the dashboard.

### Browser dashboard

- Uses repository-local plain HTML, CSS, and JavaScript.
- Uses three stable shell regions at the 1024x600 target: a 64-pixel status header, a flexible dynamic workspace, and a 68-pixel navigation row. The document itself does not scroll at the target viewport.
- Keeps active context, foreground process, connection/authentication state, measured round-trip latency, and local time in the compact header. Foreground process is supporting evidence rather than workspace content; its window title remains available as a tooltip and PID/context age move to diagnostics.
- Builds a small presentation policy from the existing context, foreground, media, and audio snapshots. Media is expanded for Media context and for Default when no more valuable capability exists. Browser / Research keeps page actions primary and uses a promoted compact Now Playing strip for browser-owned media; foreign media stays in the ordinary compact strip. Gaming composes a priority mixer with compact media.
- Matches capabilities conservatively in `capability-matching.js`: exact normalized process/source identity comes first, then a short explicit family map for Spotify, Firefox, Chrome, Edge, and Discord. Browser foreground-window/media-title equality is the only fallback for an opaque Windows media source. Display names are never fuzzy-matched, and multiple possible audio entries produce no inline control.
- Provides a reusable compact touch-action grid for context-owned commands. The current production grid contains only a System Details entry; prototype Notepad and manual latency actions no longer consume primary workspace area. Their server command/diagnostic behavior remains available without weakening the allowlist.
- Uses compact bottom navigation for Home/Automatic, Media, Audio, and More/System. Audio pins the existing Audio context and includes the current master percentage as a small global entry point. The context header and More button open the same diagnostics sheet, which contains manual context selection and technical details.
- Connects to the WebSocket endpoint and displays connecting, connected, disconnected, and retrying states.
- Renders one reusable Now Playing template/controller in two hosts: expanded as the primary workspace when media has the highest value, and compact beneath another context's higher-value workspace. Both variants receive the same normalized state, command callback, capability logic, artwork fallback, seek logic, and progress baseline. Keeping paused sessions visible preserves the user's path to resume playback.
- Shows prominent square-cropped artwork in Media context and a small compact thumbnail only when available. A deliberate local fallback occupies the expanded artwork region; text remains on an opaque surface so extreme artwork brightness cannot reduce readability.
- Displays generic media title, artist when available, source application, capability-aware transport controls, current/total time, and a touch-friendly seek range without assuming every item is music.
- Mounts the same compact Spotify accessory in both Now Playing variants only when server state confidently applies. Like, shuffle, and repeat are primary; an expanded queue overlay and current device label are secondary. These commands are independent from generic media buttons, so API latency cannot delay Windows transport controls.
- Renders a dedicated Audio workspace with one compact master-output section and an internally scrolling application list. The current output name is a touch button that opens an overlaid, internally bounded endpoint selector, so the list consumes no permanent mixer height. Active sessions sort first; inactive sessions remain available without receiving oversized cards. The document header and navigation never scroll.
- Renders Browser / Research as a dedicated compact hierarchy: one-line page title/domain, seven capability-aware tab actions, a dynamic bounded selected-text/search panel, and the same reusable Firefox audio row. When the bridge is absent, only the restrained degraded label replaces browser actions; generic media, audio, header, and navigation remain intact.
- Uses one `AudioMixerController` command path plus exported reusable volume/mute controllers for the full Audio page and context-composed rows. Slider thumbs update immediately, samples are coalesced to at most one send every 160 ms, and release schedules one final value after the short server safety gap.
- Keeps authoritative Windows values separate from the local value under the user's finger. Incoming audio state updates metadata immediately but cannot move an actively dragged thumb. A matching post-command state clears the optimistic value; failures or a short reconciliation timeout restore the latest authoritative value.
- Keeps output selection authoritative as well: tapping an endpoint immediately shows `Switching…`, but the check mark and current name change only when a later `audio_state` marks that endpoint as default. Failure restores the previous label and uses the existing accessible error feedback. The compact Audio navigation item remains the uncluttered global entry point rather than duplicating an endpoint list outside the Audio page.
- Advances the displayed playing position locally with `requestAnimationFrame` from the latest server baseline. Five-second server refreshes correct drift; pausing or a new state resets the baseline.
- Populates the diagnostics context selector from the server catalog. Home sends the existing automatic-selection request, Media pins the existing Media context, and the diagnostics selector can pin any catalog context.
- Sends only a fixed command identifier selected by a known UI control, plus a generated request ID for correlation.
- Displays pressed, disabled, and processing control states without relying on hover. Ordinary successful results are short-lived accessible toasts; failures remain longer, and status text is also announced through a live region so meaning never depends on color alone.
- Reconnects with a bounded delay after an unexpected disconnect.
- Prompts for the pre-shared token and retains it only in browser `sessionStorage`; it is not part of the frontend source.
- Uses a 1024x600 landscape layout with large touch targets and no hover dependency.
- Uses a restrained cyberdeck telemetry theme built from local CSS, system fonts, low-contrast gradients, and a subtle grid. Optional media-session artwork is fetched only from the same PiCommandStrip host; there is no external image, font, CDN, or animation dependency.
- Provides a developer layout overlay through `?layoutDebug=1` or `Ctrl+Shift+D`; the overlay reports the CSS viewport and device-pixel ratio without changing protocol behavior. Loopback-only fixtures cover no/long media, the full Audio page, Media, Default with/without media, Browser-owned/foreign media, and Gaming composition without altering server state or protocol messages.

The dynamic workspace remains the extension seam for future capabilities. `context-composition.js` is a presentation policy and view coordinator, not a second state store: it derives which existing capabilities to show, mounts contextual volume rows into either the workspace or expanded Now Playing accessory slot, and delegates every mutation to the same `AudioMixerController`. The full Audio destination remains the complete mixer. Removing an audio entry disposes its interaction controller immediately, while server-side application-ID revalidation still protects a command already in flight.

The browser code uses native JavaScript modules without a build step. `dashboard.js` coordinates startup and events, `protocol.js` owns WebSocket lifecycle and message correlation, `ui.js` owns the overall shell, `research-workspace.js` owns bounded Research view derivation and browser-action controls, `capability-matching.js` owns strict cross-signal identity rules, `context-composition.js` owns context presentation policy, `now-playing.js` owns reusable media rendering, `spotify-controls.js` owns optional Spotify controls/queue presentation, and `audio-mixer.js` owns the shared optimistic/coalesced volume interaction. A manual pin lives in the Windows host, so reconnecting clients receive the authoritative current mode without storing profile state in the browser.

Repository-local module URLs share one manually advanced frontend version token. This is intentionally simpler than adding a build pipeline and prevents a long-lived Pi browser cache from combining incompatible HTML or JavaScript modules after an update.

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

### Audio state

1. The hosted audio service uses `MMDeviceEnumerator` to enumerate active render endpoints and identify the Multimedia default. Endpoint IDs remain opaque stable identifiers; the default endpoint also supplies master scalar volume and mute.
2. Its audio-session manager enumerates current render sessions and registers session-created notifications. Each usable control registers an `IAudioSessionEventsHandler` for external volume/mute, metadata, state, and disconnect changes.
3. Each refresh reads independent raw `AudioSessionSnapshot` values. A process lookup is best-effort; failures retain the session with its other Windows metadata.
4. The normalizer filters only explicit system-sounds and expired controls, groups sessions using process/group/session identity, and converts them into deterministic application entries. Raw identity remains server-side for grouped multi-session control.
5. The store compares endpoint inventory/default flags, master state, aggregate application values, membership, and ordering. A meaningful change increments `revision`, retains the new timestamp, and broadcasts `audio_state`; an identical observation does nothing.
6. Authentication sends the retained audio state after foreground, context, and media state. Audio remains available to every context and does not influence context resolution.
7. Default-device polling and slow full reconciliation complement Windows events. They are recovery mechanisms, not level-meter polling.
8. An audio command is queued back onto this same service loop. Application IDs are resolved against the fresh normalized observation before the operation is applied to available member controls.
9. Output selection likewise requires an exact active device ID. The isolated PolicyConfig adapter asks Windows to set Console and Multimedia defaults, then the service reopens the new default and rebuilds master/session state before processing later queued mixer changes. The resulting `audio_state`, not the command result alone, is authoritative.

### Context selection

1. The dashboard builds its selector from `availableContexts` in `server_hello`.
2. Selecting a profile sends an authenticated `context_selection_request` in `manual` mode with one fixed catalog ID. Selecting Automatic sends the same request type with `automatic` mode and no context ID.
3. The parser enforces the exact direction-specific payload shape and field bounds.
4. The context store rejects unknown profile IDs without changing state.
5. A valid manual request pins that context globally for this host process. Foreground observations still refresh the evidence fields but do not change the profile.
6. A valid automatic request clears the pin and immediately resolves the latest retained foreground observation.
7. A changed state is broadcast to every authenticated client, and the requester receives a correlated `context_selection_result` even when the requested mode was already active.

Pins are intentionally in-memory and server-wide for this single-user, shared-token appliance. They reset to Automatic when the host restarts. Per-user persistence would require identity and storage requirements that are outside this milestone.

### Browser state

1. Firefox observes active-tab activation, URL/title/status changes, focused-window changes, tab removal, top-frame selection changes, and Navigation API capability changes using events.
2. The event page coalesces nearby events, builds one full state snapshot, and compares it with the last sent meaning. It performs no high-frequency polling and sends no page body/history.
3. The extension authenticates to `ws://127.0.0.1:5078/browser-integration/ws` with its separate pairing token. Reconnect uses exponential backoff with jitter and resends the current full state after authentication.
4. The loopback handler validates envelope/payload shape and bounds before passing typed data to `IBrowserIntegrationService`.
5. The normalizer sanitizes the URL and bounds transient text. The store retains only meaningful changes and clears state on active connection loss.
6. The existing connection manager broadcasts a `browser_state` projection over the already-authenticated PC-to-Pi WebSocket. It includes bounded selection only while Browser context is active; no state is persisted.
7. The frontend renders concise Research metadata/actions and composes the same media/audio controllers already used elsewhere. Context resolution continues to consume the independent foreground process signal.
8. For a browser action, the Pi sends only a fixed command ID and, for search, a configured provider ID. The host validates live state, builds any search URL itself, and sends a fixed command with an expected tab ID over loopback. Firefox revalidates that tab, executes through Tabs/Sessions/Clipboard APIs, and returns a correlated fixed result code.

### Command round trip

1. The user presses a known dashboard button.
2. JavaScript creates a unique request ID and sends a `command_request` containing its fixed command ID and only that command's typed fields. Media seek carries a non-negative integer position; mixer commands carry a normalized volume or Boolean mute state plus a server-generated application ID where required; output selection carries only an endpoint ID received from `audio_state`; selected-text search carries only a host-advertised provider ID.
3. The endpoint checks message size, JSON shape, message type, and required fields.
4. The dispatcher looks up the command ID in its explicit allowlist. Media handlers invoke only `IMediaSessionService`; audio handlers invoke only `IAudioMixerService`; browser handlers invoke only `IBrowserCommandService`.
5. If absent, the dispatcher returns a rejected result and executes nothing.
6. If present, the associated narrow handler runs with cancellation support. Media commands re-check the current Windows session and live capabilities. Audio commands execute on the audio service loop and re-resolve the current output/application sessions before touching Core Audio.
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
- frontend matching accepts exact process/source identity and explicit application families, rejects non-unique mixer candidates, and promotes Browser media only with confident foreground ownership;
- incomplete media metadata normalizes safely, positions are bounded, and reported capabilities are preserved;
- timestamp-only media refreshes are suppressed while meaningful metadata, playback, position, and active/inactive changes are retained;
- audio sessions group deterministically by safe identity while duplicate process sessions map to one application entry;
- missing audio metadata remains visible, explicit system/expired sessions are omitted, and session removal changes membership;
- audio volume, mute, output-device, and availability changes increment revision while duplicate observations are suppressed;
- artwork cache IDs are stable for identical bytes, stale IDs are evicted, malformed path-like IDs are rejected, MIME types are constrained, and memory input is bounded;
- authenticated connections receive the current media snapshot;
- media command IDs dispatch only to their corresponding `IMediaSessionService` operations;
- invalid, missing, fractional, or unexpected seek parameters are rejected before dispatch;
- validated seek positions reach the media service as a `TimeSpan`, and session-disappearance failures remain safe command results;
- cancellation stops the monitoring loop and command handlers.
- valid/malformed bridge envelopes, pairing failures, URL/domain normalization, duplicate suppression, tab changes, selection clearing/truncation, stale connection ownership, and disconnect clearing behave deterministically without Firefox;
- search templates require one absolute HTTPS placeholder, selections are percent-encoded, empty/unknown actions fail, navigation capabilities gate commands, and stale tab/bridge results remain browser-safe.

The real Win32 foreground adapter, WinRT media adapter, and Core Audio/NAudio adapter need a small amount of focused integration or manual testing on Windows. Most other logic depends on interfaces and plain data types so it can be tested deterministically.

## Dependency plan

The app and tests target `net10.0-windows10.0.19041.0`. On modern .NET, that Windows-specific target framework makes the SDK supply the Windows SDK reference/projection assets required to consume inbox WinRT APIs such as `Windows.Media.Control`; `Microsoft.Windows.SDK.Contracts`, `Microsoft.Windows.CsWinRT`, and Windows App SDK are not application dependencies. Foreground interop continues to use built-in .NET P/Invoke.

The Firefox bridge adds no .NET or JavaScript dependency. It uses Kestrel/native WebSockets, `System.Text.Json`, built-in cryptography, and standard Mozilla WebExtensions APIs. Firefox 147 is the minimum declared extension version because its current Manifest V3 policy permits the explicit loopback `ws:` CSP source for temporarily loaded extensions. Temporary installation remains a development path; signed distribution may require WSS or further Mozilla policy review.

Core Audio application sessions are classic COM APIs rather than WinRT and are not supplied as convenient .NET SDK projections. The application therefore references only stable `NAudio.Wasapi` 2.3.0 (which brings its small `NAudio.Core` dependency) for endpoint/session enumeration, callbacks, volume, and release logic. The broad `NAudio` metapackage and NAudio 3 preview are not used. NAudio intentionally does not expose a system-default setter because Windows has no public one; the small PolicyConfig vtable needed solely for that operation is maintained in one isolated adapter and documented as an unsupported compatibility risk.

The installed stable .NET SDK is pinned with `global.json`. The test project uses `Microsoft.NET.Test.Sdk` to host test execution, `xunit` for the test API and assertions, and `xunit.runner.visualstudio` for test discovery through the .NET and Visual Studio test platform.

## LAN boundary

LAN mode is an explicit opt-in configuration. It binds only to the configured PC address, requires a random pre-shared token in `client_hello`, rate-limits failed authentication, and should be paired with a Windows Firewall rule limited to the Private profile and local subnet. See [lan-setup.md](lan-setup.md).

HTTPS remains deferred. HTTP and `ws` traffic, including the token, foreground titles, and commands, is visible to a network observer and can be modified by an active attacker. LAN mode is therefore suitable only for a trusted Private network and its port must never be forwarded or otherwise exposed to the internet.
