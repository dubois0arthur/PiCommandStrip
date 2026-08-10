# WebSocket protocol

## Purpose and status

PiCommandStrip protocol version 3 is a small JSON message protocol carried over the native WebSocket endpoint at `/ws`. It provides a stable boundary between the dashboard and the Windows host without allowing browser data to become executable behavior.

This version implements connection greeting, validation, ping/pong, foreground-window and resolved-context publication, automatic/manual context selection, structured errors, and one allowlisted PC command: `open_notepad`. Version 3 retains the version 2 authentication and command safety model but is intentionally versioned because it adds client and server message types.

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
    "protocolVersion": "3",
    "authenticationToken": "<32-byte Base64 token>"
  }
}
```

- `clientName` is required, non-blank, and at most 100 characters.
- `protocolVersion` is required. The server returns `unsupported_protocol_version` when it is not `3`.
- `authenticationToken` must match the 32-byte Base64 pre-shared token configured outside Git on the Windows host. A missing, incorrect, expired, or rate-limited attempt receives a structured error and does not enable state or commands.

The token is never logged or embedded in frontend source. The browser obtains it from the user and keeps it in `sessionStorage`. Because version 3 currently uses unencrypted HTTP/WebSocket transport, a LAN observer can read the token; use only a trusted Private network and do not expose the port to the internet.

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

Carries only a fixed command identifier. It never contains a shell command, executable path, script, arguments, or executable object. The payload must contain exactly one property named `commandId`; additional properties are rejected.

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

`commandId` is required, non-blank, and at most 100 characters. `open_notepad` is the only allowlisted value. It maps internally to a dedicated handler with no command parameters. Unknown identifiers execute nothing and receive a failed `command_result`. A connection may attempt one command every two seconds; faster attempts receive a rate-limit result.

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
    "protocolVersion": "3",
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

A `command_request` is only a `CommandRequestMessage` containing a bounded identifier. `PcCommandDispatcher` resolves that identifier through its server-created dictionary of `IPcCommandHandler` instances. The only registered handler is `OpenNotepadCommandHandler`; it accepts no payload values and calls a Notepad-specific launcher. The browser never supplies the executable path.

The Notepad launcher combines the server's trusted Windows system directory with the fixed filename `notepad.exe` and calls `Process.Start` with `UseShellExecute` disabled. Unknown identifiers, extra command payload fields, and attempts during the per-connection cooldown are rejected before process launch.

## Connection lifecycle

1. The browser requests an HTTP upgrade at `/ws`.
2. ASP.NET Core rejects ordinary HTTP requests to that path with status 400.
3. After a successful upgrade, the server sends `server_hello`.
4. The dashboard sends `client_hello` with protocol version 3, its fresh UTC timestamp, and the configured token.
5. The server checks rate limits, protocol compatibility, timestamp freshness, and the token using constant-time comparison.
6. Only after successful authentication does the server mark the connection ready and send retained `pc_state` followed by `context_state`.
7. The authenticated dashboard may send `ping`, `context_selection_request`, or `command_request` messages, while meaningful foreground and context changes are broadcast independently.
8. The server reads complete messages, validates them, and dispatches only recognized typed records.
9. Either peer may initiate the normal WebSocket close handshake.
10. Application shutdown cancels active receive and polling operations and attempts a bounded close before releasing each socket.
11. The browser reconnects after transport failures unless its page is unloading or authentication requires user action.
