# WebSocket protocol

## Purpose and status

PiCommandStrip protocol version 1 is a small JSON message protocol carried over the native WebSocket endpoint at `/ws`. It provides a stable boundary between the dashboard and the Windows host without allowing browser data to become executable behavior.

This version implements connection greeting, validation, ping/pong, structured errors, and a non-executing response to command requests. `pc_state` is defined for the next foreground-window phase but is not published yet. No PC command is executed in this version.

## Transport

- Connect to `/ws` using `ws` when the page uses HTTP or `wss` when it uses HTTPS.
- Messages are UTF-8 JSON text messages. Binary messages are rejected.
- A logical JSON message may use multiple WebSocket frames.
- The maximum accepted client message size is 16 KiB (16,384 bytes), measured after all frames are combined.
- The server and browser both support a normal WebSocket close handshake.
- The browser reconnects two seconds after a disconnect or connection failure.

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

The current parser requires exact camel-case property names. Incoming timestamps and IDs are validated, but browser timestamps are not treated as trusted clock values.

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
    "protocolVersion": "1"
  }
}
```

- `clientName` is required, non-blank, and at most 100 characters.
- `protocolVersion` is required. The server returns `unsupported_protocol_version` when it is not `1`.

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

### `command_request`

Carries only a fixed command identifier. It never contains a shell command, executable path, script, arguments, or executable object.

```json
{
  "type": "command_request",
  "messageId": "a227745c-5d58-4859-92fb-b5586c685b13",
  "timestampUtc": "2026-08-05T12:00:02.000Z",
  "payload": {
    "commandId": "demo.safe-command"
  }
}
```

`commandId` is required, non-blank, and at most 100 characters. In this protocol-only milestone every valid request receives a failed `command_result` stating that commands are unavailable. A later milestone will resolve identifiers through an explicit server-owned allowlist.

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
    "protocolVersion": "1",
    "maximumMessageSizeBytes": 16384
  }
}
```

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
    "commandId": "demo.safe-command",
    "succeeded": false,
    "message": "PC commands are not available in this protocol-only milestone."
  }
}
```

### `pc_state`

Reserved for foreground-window state. The contract is defined now but messages are not emitted until Windows state detection exists.

```json
{
  "type": "pc_state",
  "messageId": "c3da68ce-54ec-46d6-94d2-cb597fca00d4",
  "timestampUtc": "2026-08-05T12:00:03.0000000+00:00",
  "payload": {
    "processName": "notepad",
    "windowTitle": "Notes.txt - Notepad"
  }
}
```

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

Malformed, invalid, unknown, binary, and oversized messages receive an error when the connection is still writable. They do not crash the application, and recoverable errors do not automatically close the connection.

## Parsing and safety boundary

The server does not ask `System.Text.Json` to create an arbitrary polymorphic object from the browser payload. It first parses a bounded message into a `JsonDocument`, validates the common fields, explicitly switches on the allowlisted client message type, validates that payload, and constructs a specific data-only C# record.

The connection handler then switches over those known records. A `command_request` is only a `CommandRequestMessage` containing a string identifier. It is not an executable command and cannot launch a process. This separation must remain in place when the command allowlist is added.

## Connection lifecycle

1. The browser requests an HTTP upgrade at `/ws`.
2. ASP.NET Core rejects ordinary HTTP requests to that path with status 400.
3. After a successful upgrade, the server sends `server_hello`.
4. The dashboard sends `client_hello` and may then send `ping` or `command_request` messages.
5. The server reads complete messages, validates them, and dispatches only recognized typed records.
6. Either peer may initiate the normal WebSocket close handshake.
7. Application shutdown cancels active receive operations and attempts a bounded close before releasing the socket.
8. The browser reconnects after two seconds unless its page is unloading.
