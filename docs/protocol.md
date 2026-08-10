# WebSocket protocol

## Purpose and status

PiCommandStrip protocol version 6 is a small JSON message protocol carried over the native WebSocket endpoint at `/ws`. It provides a stable boundary between the dashboard and the Windows host without allowing browser data to become executable behavior.

This version implements connection greeting, validation, ping/pong, foreground-window, resolved-context, and Windows system media-state publication, automatic/manual context selection, structured errors, and fixed allowlisted Notepad/media commands. Version 6 retains version 5 media controls and adds a local content-addressed artwork reference to media state; artwork bytes remain outside WebSocket messages.

## Transport

- Connect to `/ws` using `ws` when the page uses HTTP or `wss` when it uses HTTPS.
- Messages are UTF-8 JSON text messages. Binary messages are rejected.
- A logical JSON message may use multiple WebSocket frames.
- The maximum accepted client message size is 16 KiB (16,384 bytes), measured after all frames are combined.
- The server and browser both support a normal WebSocket close handshake.
- The browser reconnects two seconds after a transport failure, but stops retrying after a terminal authentication failure so it does not hammer the server.

WebSocket control-frame keepalives and the application-level `ping` message solve different problems. Kestrel's keepalive helps maintain the transport. The JSON `ping`/`pong` pair verifies the application protocol and allows the dashboard to measure round-trip time.

## Common envelope

Every client and server message has exactly the same top-level shape:

```json
{
  "type": "ping",
  "messageId": "4de31b91-9a89-42cb-a35f-3a36ac38dd5f",
  "timestampUtc": "2026-08-05T12:00:00.000Z",
  "payload": {}
}
```

| Field | Type | Meaning |
| --- | --- | --- |
| `type` | non-empty string | Selects one of the explicitly supported message contracts. |
| `messageId` | non-empty GUID string | Uniquely identifies this message. Responses refer to a request's ID from their payload. |
| `timestampUtc` | ISO 8601 timestamp with a zero UTC offset | Records when the sender created the message. It is diagnostic metadata, not trusted for elapsed-time measurement. |
| `payload` | JSON object | Contains only fields defined for the selected message type. An empty payload is still `{}`. |

The current parser requires exact camel-case property names. Incoming timestamps and IDs are validated. The `client_hello` timestamp must be within one minute of server UTC time to reject expired authentication attempts; other browser timestamps are diagnostic only.

## Client-to-server messages

### `client_hello`

Sent by the dashboard immediately after the WebSocket opens.

```json
{
  "type": "client_hello",
  "messageId": "ff5bfc17-e1a5-4f4a-ac07-e111d4053be8",
  "timestampUtc": "2026-08-05T12:00:00.000Z",
  "payload": {
    "clientName": "browser-dashboard",
    "protocolVersion": "6",
    "authenticationToken": "<32-byte Base64 token>"
  }
}
```

- `clientName` is required, non-blank, and at most 100 characters.
- `protocolVersion` is required. The server returns `unsupported_protocol_version` when it is not `6`.
- `authenticationToken` must match the 32-byte Base64 pre-shared token configured outside Git on the Windows host. A missing, incorrect, expired, or rate-limited attempt receives a structured error and does not enable state or commands.

The token is never logged or embedded in frontend source. The browser obtains it from the user and keeps it in `sessionStorage`. Because version 6 currently uses unencrypted HTTP/WebSocket transport, a LAN observer can read the token; use only a trusted Private network and do not expose the port to the internet.

### `ping`

Requests an application-level pong. Its payload is empty.

```json
{
  "type": "ping",
  "messageId": "1caf10e3-8c7e-490c-b984-62d830229845",
  "timestampUtc": "2026-08-05T12:00:01.000Z",
  "payload": {}
}
```

### `context_selection_request`

Selects automatic resolution or a manually pinned catalog context. It is accepted only after authentication and never contains process names, executable data, or user-defined code.

Automatic mode has exactly one payload property:

```json
{
  "type": "context_selection_request",
  "messageId": "4a0ec715-03a6-4137-863d-a264189d2c5f",
  "timestampUtc": "2026-08-10T12:00:01.500Z",
  "payload": {
    "mode": "automatic"
  }
}
```

Manual mode has exactly `mode` and `contextId`:

```json
{
  "type": "context_selection_request",
  "messageId": "5a0ec715-03a6-4137-863d-a264189d2c5f",
  "timestampUtc": "2026-08-10T12:00:01.600Z",
  "payload": {
    "mode": "manual",
    "contextId": "media"
  }
}
```

`mode` must be exactly `automatic` or `manual`. A manual `contextId` is required, non-blank, and at most 50 characters. The server accepts only IDs from its fixed context catalog; an unknown ID changes nothing and receives a failed `context_selection_result`.

### `command_request`

Carries a fixed allowlisted command identifier. It never contains a shell command, executable path, script, argument list, or executable object. All commands except `media.seek` have exactly one payload property named `commandId`; additional properties are rejected.

```json
{
  "type": "command_request",
  "messageId": "a227745c-5d58-4859-92fb-b5586c685b13",
  "timestampUtc": "2026-08-05T12:00:02.000Z",
  "payload": {
    "commandId": "open_notepad"
  }
}
```

The allowlisted identifiers are:

- `open_notepad`
- `media.play`
- `media.pause`
- `media.playPause`
- `media.previous`
- `media.next`
- `media.seek`

`media.seek` is the only parameterized command and has exactly this shape:

```json
{
  "type": "command_request",
  "messageId": "b227745c-5d58-4859-92fb-b5586c685b13",
  "timestampUtc": "2026-08-10T12:00:02.000Z",
  "payload": {
    "commandId": "media.seek",
    "positionMilliseconds": 42500
  }
}
```

`positionMilliseconds` must be a non-negative JSON integer. The server validates it again against the live media session's seek capability, duration, and seek range before converting the relative position to Windows timeline ticks. Unknown identifiers execute nothing and receive a failed `command_result`. A connection may attempt one command every two seconds; faster attempts receive a rate-limit result.

## Server-to-client messages

### `server_hello`

The first message sent by the server after accepting a connection.

```json
{
  "type": "server_hello",
  "messageId": "f159b3d0-c82a-4386-a875-247e37daf0cf",
  "timestampUtc": "2026-08-05T12:00:00.0000000+00:00",
  "payload": {
    "applicationName": "PiCommandStrip.App",
    "protocolVersion": "6",
    "maximumMessageSizeBytes": 16384,
    "availableContexts": [
      { "contextId": "default", "displayName": "Default" },
      { "contextId": "media", "displayName": "Media" },
      { "contextId": "browser", "displayName": "Browser / Research" },
      { "contextId": "gaming", "displayName": "Gaming" },
      { "contextId": "audio", "displayName": "Audio" }
    ]
  }
}
```

`availableContexts` is presentation metadata for constructing the manual selector. It does not allow the browser to define profiles; the server still validates every selected ID against its own catalog.

### `pong`

Sent in response to `ping`. `requestMessageId` is the `messageId` of that ping.

```json
{
  "type": "pong",
  "messageId": "ba9681b6-b986-42fb-90f6-45c731d59b73",
  "timestampUtc": "2026-08-05T12:00:01.0100000+00:00",
  "payload": {
    "requestMessageId": "1caf10e3-8c7e-490c-b984-62d830229845"
  }
}
```

### `command_result`

Reports the outcome of a `command_request`. The result has its own `messageId`; `requestMessageId` correlates it to the request.

```json
{
  "type": "command_result",
  "messageId": "7650d838-4d35-41fa-9a55-c58d25d24987",
  "timestampUtc": "2026-08-05T12:00:02.0100000+00:00",
  "payload": {
    "requestMessageId": "a227745c-5d58-4859-92fb-b5586c685b13",
    "commandId": "open_notepad",
    "succeeded": true,
    "message": "Notepad opened.",
    "completedAtUtc": "2026-08-05T12:00:02.0090000+00:00"
  }
}
```

`message` is safe display text selected by the server. Exceptions, stack traces, executable paths, and other local details are never copied into it. `completedAtUtc` is the server time at which dispatch completed or rejection was decided.

### `pc_state`

Reports the latest foreground-window observation. The server sends it immediately after accepting a compatible `client_hello`, then only when availability, process ID, process name, or window title changes. A newer polling timestamp alone does not cause a message.

```json
{
  "type": "pc_state",
  "messageId": "c3da68ce-54ec-46d6-94d2-cb597fca00d4",
  "timestampUtc": "2026-08-05T12:00:03.0000000+00:00",
  "payload": {
    "isAvailable": true,
    "processName": "notepad",
    "processId": 4280,
    "windowTitle": "Notes.txt - Notepad",
    "observedAtUtc": "2026-08-05T12:00:02.9500000+00:00"
  }
}
```

`observedAtUtc` is when the server observed the meaningful foreground change. If no usable foreground window exists, `isAvailable` is `false`; `processName`, `processId`, and `windowTitle` are `null`. Expected causes include no foreground HWND, a process exiting during inspection, inaccessible process information, or running the host on a non-Windows platform.

### `context_state`

Reports the effective profile and the foreground evidence used by context policy. The server sends it immediately after the initial authenticated `pc_state`, whenever automatic resolution changes, whenever foreground process/title evidence changes, and whenever manual selection changes.

```json
{
  "type": "context_state",
  "messageId": "d3da68ce-54ec-46d6-94d2-cb597fca00d4",
  "timestampUtc": "2026-08-10T12:00:03.0000000+00:00",
  "payload": {
    "contextId": "browser",
    "displayName": "Browser / Research",
    "selectionMode": "automatic",
    "source": "foreground_process",
    "trigger": "firefox",
    "foregroundProcess": "firefox",
    "foregroundWindowTitle": "Research notes",
    "activeSinceUtc": "2026-08-10T11:58:20.0000000+00:00"
  }
}
```

`selectionMode` is `automatic` or `manual`. Automatic `source` is `foreground_process` for a configured match or `fallback` for Default. Manual state uses `manual_override`. `trigger` identifies the normalized process, `foreground_unavailable`, or pinned context ID as appropriate. Foreground fields are nullable when Windows has no usable observation.

`activeSinceUtc` records when the effective context ID became active. It is preserved while a title changes, while two processes resolve to the same context, and while a manual pin observes new foreground windows. It changes when the effective context ID changes.

### `media_state`

Reports the normalized state of the session that Windows considers current. It is sent after the retained foreground and context snapshots when a client authenticates, then whenever session identity, metadata, playback state, timeline, or advertised control capabilities meaningfully change.

```json
{
  "type": "media_state",
  "messageId": "e3da68ce-54ec-46d6-94d2-cb597fca00d5",
  "timestampUtc": "2026-08-10T12:00:03.0500000+00:00",
  "payload": {
    "hasActiveSession": true,
    "sessionSourceIdentifier": "Spotify.exe",
    "sourceName": "Spotify",
    "title": "Example track",
    "artist": "Example artist",
    "albumTitle": "Example album",
    "artworkUrl": "/media/artwork/4f8c7b6a0c8f5b2a3d57f4057b7d8b51e458915a765eada0b70f19528e4a9db0",
    "playbackState": "playing",
    "positionMilliseconds": 84250,
    "totalDurationMilliseconds": 232000,
    "supportsPrevious": true,
    "supportsNext": true,
    "supportsPlay": false,
    "supportsPause": true,
    "supportsPlayPause": true,
    "supportsSeeking": true,
    "lastUpdatedUtc": "2026-08-10T12:00:03.0490000+00:00"
  }
}
```

`sessionSourceIdentifier` is the source application identifier supplied by Windows; `sourceName` is a short display-oriented name derived from it. Metadata fields are nullable because applications may expose only some values. Playback state is `closed`, `opened`, `changing`, `stopped`, `playing`, `paused`, or `unknown`; `none` is used when there is no current session. Positions and durations are nullable non-negative milliseconds. Reported positions are clamped to a known duration. Capability flags are authoritative hints for rendering controls; the server re-reads live capability data before every media command.

`artworkUrl` is either `null` or a same-origin `/media/artwork/{sha256-id}` path. The WebSocket never embeds image bytes or a filesystem path. The HTTP endpoint serves only the single currently cached bitmap ID, rejects malformed IDs, limits source artwork to 5 MiB, and returns `404` after the artwork is replaced or cleared. Clients may cache a successful content-addressed response using its ETag.

When no session is current, `hasActiveSession` is `false`, the identifier, source, metadata, position, and duration fields are `null`, every capability is `false`, and playback state is `none`. A timestamp-only refresh is not broadcast. While playback is active, the host performs a low-frequency timeline resync in addition to reacting to Windows events, so position changes may be published even when metadata and playback state are stable.

Media state is independent of `context_state` and is available in every context. Playing media does not select Media context. Foreground Spotify can still map to Media through process configuration, while foreground Firefox, Chrome, or Edge remains Browser / Research even when its current media session represents YouTube.

### `context_selection_result`

Correlates the outcome of a `context_selection_request`. A valid request receives a successful result even if that mode/context was already active; state is broadcast only when its meaningful value changed.

```json
{
  "type": "context_selection_result",
  "messageId": "e3da68ce-54ec-46d6-94d2-cb597fca00d4",
  "timestampUtc": "2026-08-10T12:00:03.1000000+00:00",
  "payload": {
    "requestMessageId": "5a0ec715-03a6-4137-863d-a264189d2c5f",
    "succeeded": true,
    "message": "Media context pinned.",
    "completedAtUtc": "2026-08-10T12:00:03.0990000+00:00"
  }
}
```

Manual selection is global to the running host and in-memory. Any authenticated dashboard sharing the pre-shared token can change it. Returning to automatic mode immediately resolves the latest retained foreground observation. Restarting the host clears the pin.

### `error`

Reports a recoverable protocol problem. When the server could validate the inbound `messageId`, `requestMessageId` contains it; otherwise it is `null`.

```json
{
  "type": "error",
  "messageId": "02dd0e65-602f-4213-879c-ae2b832ea027",
  "timestampUtc": "2026-08-05T12:00:04.0000000+00:00",
  "payload": {
    "requestMessageId": null,
    "code": "malformed_json",
    "message": "The message is not valid JSON."
  }
}
```

Current error codes are:

- `malformed_json`
- `invalid_envelope`
- `invalid_payload`
- `unknown_message_type`
- `unsupported_protocol_version`
- `unsupported_data`
- `message_too_large`
- `authentication_missing`
- `authentication_failed`
- `authentication_expired`
- `authentication_rate_limited`
- `authentication_required`
- `already_authenticated`

Malformed, invalid, unknown, binary, and oversized messages receive an error when the connection is still writable. They do not crash the application, and recoverable errors do not automatically close the connection.

## Parsing and safety boundary

The server does not ask `System.Text.Json` to create an arbitrary polymorphic object from the browser payload. It first parses a bounded message into a `JsonDocument`, validates the common fields, explicitly switches on the allowlisted client message type, validates that payload, and constructs a specific data-only C# record.

The connection handler then switches over those known records. Before it accepts `ping`, `context_selection_request`, or `command_request`, a version-compatible `client_hello` must pass constant-time pre-shared-token comparison and timestamp freshness validation. A process-wide limiter permits at most five authentication attempts per 30-second window and resets after success. The token is never included in logs.

Context selection is separate from command dispatch. The parser creates a data-only `ContextSelectionRequestMessage`; `ContextStateStore` resolves its fixed catalog ID or rejects it. It cannot supply process mappings, paths, scripts, commands, or profile definitions.

A `command_request` becomes a bounded `PcCommandInvocation`. `PcCommandDispatcher` resolves its fixed identifier through a server-created dictionary of `IPcCommandHandler` instances. `OpenNotepadCommandHandler` accepts no payload value and calls a Notepad-specific launcher. Fixed `MediaCommandHandler` registrations call only `IMediaSessionService`; the browser cannot supply an executable path, Windows API name, or arbitrary argument.

The parser permits `positionMilliseconds` only with `media.seek`. The media handler validates its numeric range, and the Windows implementation requires a current session, a seek-enabled live capability, a valid timeline, and a position inside both the duration and advertised seek range. A session that disappears during a request produces a safe failed `command_result`.

The Notepad launcher combines the server's trusted Windows system directory with the fixed filename `notepad.exe` and calls `Process.Start` with `UseShellExecute` disabled. Unknown identifiers, extra command payload fields, and attempts during the per-connection cooldown are rejected before process launch.

## Connection lifecycle

1. The browser requests an HTTP upgrade at `/ws`.
2. ASP.NET Core rejects ordinary HTTP requests to that path with status 400.
3. After a successful upgrade, the server sends `server_hello`.
4. The dashboard sends `client_hello` with protocol version 6, its fresh UTC timestamp, and the configured token.
5. The server checks rate limits, protocol compatibility, timestamp freshness, and the token using constant-time comparison.
6. Only after successful authentication does the server mark the connection ready and send retained `pc_state`, `context_state`, and `media_state` snapshots in that order.
7. The authenticated dashboard may send `ping`, `context_selection_request`, or `command_request` messages, while meaningful foreground, context, and media changes are broadcast independently.
8. The server reads complete messages, validates them, and dispatches only recognized typed records.
9. Either peer may initiate the normal WebSocket close handshake.
10. Application shutdown cancels active receive and polling operations and attempts a bounded close before releasing each socket.
11. The browser reconnects after transport failures unless its page is unloading or authentication requires user action.
