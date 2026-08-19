# WebSocket protocol

## Purpose and status

PiCommandStrip protocol version 13 is a small JSON message protocol carried over the native WebSocket endpoint at `/ws`. It provides a stable boundary between the dashboard and the Windows host without allowing browser data to become executable behavior.

This version implements connection greeting, validation, ping/pong, foreground-window, resolved-context, Windows system media state, normalized Windows audio-mixer state, optional Spotify enrichment state, optional Firefox research state/actions, automatic/manual context selection, structured errors, and fixed allowlisted commands. Version 13 adds the local Research Inbox's fixed Save/Open commands and lightweight change state; bounded inbox content is retrieved through the authenticated HTTP API rather than embedded in WebSocket messages.

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
    "protocolVersion": "13",
    "authenticationToken": "<32-byte Base64 token>"
  }
}
```

- `clientName` is required, non-blank, and at most 100 characters.
- `protocolVersion` is required. The server returns `unsupported_protocol_version` when it is not `13`.
- `authenticationToken` must match the 32-byte Base64 pre-shared token configured outside Git on the Windows host. A missing, incorrect, expired, or rate-limited attempt receives a structured error and does not enable state or commands.

The token is never logged or embedded in frontend source. The browser obtains it from the user and keeps it in `sessionStorage`. Because version 13 currently uses unencrypted HTTP/WebSocket transport, a LAN observer can read the token, transient selected-text previews, and Research Inbox HTTP responses; use only a trusted Private network and do not expose the port to the internet.

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

Carries a fixed allowlisted command identifier. It never contains a shell command, executable path, script, argument list, or executable object. Each command has an exact payload shape; additional properties are rejected.

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
- `audio.setMasterVolume`
- `audio.setMasterMute`
- `audio.setApplicationVolume`
- `audio.setApplicationMute`
- `audio.setOutputDevice`
- `spotify.setSaved`
- `spotify.setShuffle`
- `spotify.setRepeat`
- `browser.back`
- `browser.forward`
- `browser.reload`
- `browser.newTab`
- `browser.closeTab`
- `browser.reopenClosedTab`
- `browser.copyCurrentUrl`
- `browser.searchSelection`

`media.seek` has exactly this shape:

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

`positionMilliseconds` must be a non-negative JSON integer. The server validates it again against the live media session's seek capability, duration, and seek range before converting the relative position to Windows timeline ticks. Unknown identifiers execute nothing and receive a failed `command_result`.

Audio volume commands carry a finite normalized `volume` from `0.0` through `1.0`. Mute commands carry a Boolean `isMuted`. Application commands additionally carry the opaque 64-character hexadecimal `applicationId` from the latest `audio_state`:

```json
{
  "type": "command_request",
  "messageId": "c227745c-5d58-4859-92fb-b5586c685b13",
  "timestampUtc": "2026-08-18T12:00:02.000Z",
  "payload": {
    "commandId": "audio.setApplicationVolume",
    "applicationId": "9b88c63f4dd15f40d263d0ff745d5fc906422256178790824662f4a45e0cc880",
    "volume": 0.42
  }
}
```

The server resolves application IDs only against the current normalized mixer state, then applies the change to every still-live underlying Windows session in that group. A stale or disappeared ID returns a failed `command_result`. Master commands target only the current default multimedia render endpoint. Ordinary commands retain the configured 750-millisecond per-connection cooldown; coalesced audio volume updates use a separate 40-millisecond server gate so a final touch release is not blocked by the previous drag sample.

Output selection carries exactly the opaque `deviceId` from the latest `audio_state`:

```json
{
  "type": "command_request",
  "messageId": "d227745c-5d58-4859-92fb-b5586c685b13",
  "timestampUtc": "2026-08-18T12:00:03.000Z",
  "payload": {
    "commandId": "audio.setOutputDevice",
    "deviceId": "{0.0.0.00000000}.{example-device-id}"
  }
}
```

The parser bounds the ID to 512 characters, and the audio service then requires an exact ordinal match to an active endpoint in its current normalized state. A display name, stale ID, executable path, registry path, or extra property cannot select a device. PiCommandStrip changes the Windows Console and Multimedia default render roles; it deliberately leaves the Communications default unchanged.

Spotify enhancement commands are accepted only for the server's confidently matched current Spotify item. Their exact additional fields are a Boolean `isSaved`, a Boolean `shuffleEnabled`, or a `repeatState` of `off`, `context`, or `track`, respectively. No access token, client secret, Spotify URI, device ID, arbitrary endpoint, or extra property is accepted from the browser. These commands call only `ISpotifyService` and return ordinary `command_result` messages. The generic `media.*` controls remain separate and continue to work if Spotify is disabled or unavailable.

Browser navigation/tab commands contain only their fixed `commandId`. Selected-text search contains exactly a configured provider identifier:

```json
{
  "type": "command_request",
  "messageId": "e227745c-5d58-4859-92fb-b5586c685b13",
  "timestampUtc": "2026-08-18T12:00:04.000Z",
  "payload": {
    "commandId": "browser.searchSelection",
    "searchActionId": "wikipedia"
  }
}
```

The Pi cannot send selected text, an arbitrary URL, JavaScript, keyboard input, or a raw extension command. `IBrowserCommandService` resolves the provider against the host-owned search catalog, reads the current bounded selection from host memory, URL-encodes it, and builds an absolute HTTPS URL from the configured fixed template. Tab-specific actions carry the retained active-tab ID only on the separate Windows-loopback bridge; the extension re-queries the active tab and rejects a stale mismatch before acting.

Research Inbox commands are also exact-shape allowlisted messages. `research.saveCurrent` has no fields beyond `commandId`; the host captures the current normalized browser page and optional selection only when that command executes. `research.openItem` accepts only a positive server-generated integer item ID:

```json
{
  "type": "command_request",
  "messageId": "f227745c-5d58-4859-92fb-b5586c685b13",
  "timestampUtc": "2026-08-19T12:00:04.000Z",
  "payload": {
    "commandId": "research.openItem",
    "researchItemId": 42
  }
}
```

The host resolves that ID from its own store, revalidates the stored HTTP/HTTPS URI, and asks the browser bridge to open it in a new tab. The Pi cannot supply the destination URI, and no URL is passed to a shell.

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
    "protocolVersion": "13",
    "maximumMessageSizeBytes": 16384,
    "availableContexts": [
      { "contextId": "default", "displayName": "Default" },
      { "contextId": "media", "displayName": "Media" },
      { "contextId": "browser", "displayName": "Browser / Research" },
      { "contextId": "gaming", "displayName": "Gaming" },
      { "contextId": "audio", "displayName": "Audio" }
    ],
    "availableBrowserSearchActions": [
      { "actionId": "google", "displayName": "Google" },
      { "actionId": "wikipedia", "displayName": "Wikipedia" },
      { "actionId": "youtube", "displayName": "YouTube" }
    ]
  }
}
```

`availableContexts` is presentation metadata for constructing the manual selector. It does not allow the browser to define profiles; the server still validates every selected ID against its own catalog.

`availableBrowserSearchActions` contains presentation-only provider IDs and names from host configuration. Templates are never sent to the Pi, and every incoming provider ID is revalidated against the same host catalog before use.

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

### `audio_state`

Reports the normalized state of the default multimedia render endpoint and its user-facing application groups. It is sent after `media_state` when a client authenticates and then only after a meaningful device, membership, volume, mute, metadata, or activity change.

```json
{
  "type": "audio_state",
  "messageId": "f3da68ce-54ec-46d6-94d2-cb597fca00d6",
  "timestampUtc": "2026-08-10T12:00:03.0750000+00:00",
  "payload": {
    "isAvailable": true,
    "outputDevice": {
      "deviceId": "{0.0.0.00000000}.{example-device-id}",
      "friendlyName": "Speakers (USB Audio Device)",
      "volume": 0.72,
      "isMuted": false
    },
    "outputDevices": [
      {
        "deviceId": "{0.0.0.00000000}.{example-device-id}",
        "friendlyName": "Speakers (USB Audio Device)",
        "state": "active",
        "isDefault": true
      },
      {
        "deviceId": "{0.0.0.00000000}.{example-headphones-id}",
        "friendlyName": "Headphones",
        "state": "active",
        "isDefault": false
      }
    ],
    "applications": [
      {
        "applicationId": "9b88c63f4dd15f40d263d0ff745d5fc906422256178790824662f4a45e0cc880",
        "processIds": [5312, 7420],
        "processName": "firefox",
        "displayName": "Firefox",
        "volume": 0.5,
        "isMuted": false,
        "state": "active",
        "sessionCount": 3,
        "hasMixedVolume": true,
        "hasMixedMute": false
      }
    ],
    "revision": 14,
    "lastUpdatedUtc": "2026-08-10T12:00:03.0740000+00:00"
  }
}
```

`outputDevices` contains the currently usable Windows render endpoints. `deviceId` is the stable opaque Core Audio endpoint ID, `friendlyName` is display text, `state` is currently `active` for a usable entry, and exactly the observed Multimedia default is marked with `isDefault`. Device IDs are not filesystem paths and the selector never treats them as executable input. Application `volume` values are finite normalized scalars from `0.0` through `1.0`, rounded to three decimal places. Application `state` is `active`, `inactive`, or `unknown`. `processIds` can be empty or contain several processes; `processName` is nullable. `displayName` always has a deliberate fallback. Peak level is not part of version 13.

`applicationId` is a server-generated SHA-256 identifier for the grouping key. It is stable while the grouping evidence is stable and never exposes a process path or raw Windows session identifier. `sessionCount` shows how many underlying Windows controls contribute to the entry. `hasMixedVolume` and `hasMixedMute` indicate that those controls disagree. The reported grouped volume is the maximum member scalar, and grouped `isMuted` is true only when every member is muted.

When no default output is available, `isAvailable` is `false`, `outputDevice` is `null`, and `applications` is empty. `outputDevices` can still contain active endpoints that may establish a new default. `revision` starts at zero and increases monotonically for this host process whenever the normalized meaning changes, including endpoint arrival/removal and default-role changes. `lastUpdatedUtc` records that meaningful observation; timestamp-only samples do not produce a message.

Audio state is global and independent of both `media_state` and `context_state`. It can describe many applications while media state describes only the session Windows currently prefers. Version 13 commands reference this state but do not make it client-owned: later `audio_state` messages remain authoritative. The device selector shows processing feedback but does not mark a choice current until an authoritative state identifies it as the default.

### `spotify_state`

Reports optional Spotify-only enrichment. It is sent to a newly authenticated client after the generic media/audio snapshots and then only when its meaning changes.

```json
{
  "type": "spotify_state",
  "messageId": "a3da68ce-54ec-46d6-94d2-cb597fca00d7",
  "timestampUtc": "2026-08-18T12:00:03.0900000+00:00",
  "payload": {
    "status": "available",
    "isConfigured": true,
    "isAuthenticated": true,
    "appliesToCurrentMedia": true,
    "itemType": "track",
    "isSaved": true,
    "shuffleEnabled": false,
    "repeatState": "off",
    "device": {
      "name": "Office PC",
      "type": "computer",
      "isRestricted": false
    },
    "queue": [
      { "title": "Next item", "subtitle": "Example artist", "itemType": "track" }
    ],
    "lastUpdatedUtc": "2026-08-18T12:00:03.0890000+00:00",
    "retryAfterUtc": null
  }
}
```

`status` is `unconfigured`, `unauthenticated`, `idle`, `available`, `rate_limited`, or `error`. Controls render only when `appliesToCurrentMedia` is true, which requires an explicit Spotify Windows media source and an exact normalized title match against Spotify's current Web API item. This deliberately conservative join prevents a browser or another player from receiving Spotify controls. Queue entries are capped at five. Server-only Spotify item URIs and match evidence are never serialized.

An enrichment error may retain the last matched values for readable degraded state while setting `status` to `error` or `rate_limited`; controls are disabled until recovery. `retryAfterUtc` is present only when Spotify supplies a rate-limit delay. `unconfigured`, `unauthenticated`, and non-Spotify `idle` states do not affect generic `media_state` or any `media.*` command.

### `browser_state`

Reports optional browser enrichment retained by `IBrowserIntegrationService`. A newly authenticated Pi dashboard receives the current state after the Spotify snapshot. Meaningful extension connect, active-tab, URL/title, bounded selection, navigation-capability, and disconnect changes are then broadcast independently.

```json
{
  "type": "browser_state",
  "messageId": "b3da68ce-54ec-46d6-94d2-cb597fca00d8",
  "timestampUtc": "2026-08-18T12:00:03.1000000+00:00",
  "payload": {
    "connectionState": "connected",
    "browserType": "firefox",
    "sourceIdentifier": "firefox-bridge@picommandstrip.local",
    "instanceIdentifier": "cc1467f2-622a-4a7d-9eae-531044ac9d20",
    "activeTabId": 42,
    "url": "https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions",
    "hostName": "developer.mozilla.org",
    "pageTitle": "Browser extensions - Mozilla | MDN",
    "hasSelectedText": true,
    "selectedText": "A bounded passage selected for research",
    "canGoBack": true,
    "canGoForward": false,
    "lastUpdatedUtc": "2026-08-18T12:00:03.0990000+00:00"
  }
}
```

`connectionState` is `connected` or `disconnected`. Browser/source/instance identify the producer but are not authentication credentials. Firefox tab IDs are valid only for the current browser session and may be reused. `url` is either a sanitized absolute HTTP/HTTPS URL or `null`: URI user information and fragments are removed before publication. `hostName` is derived server-side and IDN-normalized. Restricted/internal pages can therefore report only a title or no page metadata.

`selectedText` is transient, trimmed, and capped at 1,000 UTF-16 characters by the Windows host; the frontend additionally limits the visible preview to 180 characters. It is present only while Browser context is active and a current selection exists. Merely receiving or rendering this state never persists it, and it is never logged. It becomes persisted research content only when the user explicitly invokes `research.saveCurrent`. `hasSelectedText` remains an explicit convenience flag. `canGoBack` and `canGoForward` are nullable: the content script reports exact values where Firefox exposes the Navigation API, and unknown is rendered disabled rather than guessed.

A disconnected state has `connectionState: "disconnected"`, `hasSelectedText: false`, and `null` for every other browser field including `selectedText`. Browser state does not select Browser / Research context; the existing foreground-process resolver remains authoritative. See [browser-integration.md](browser-integration.md) for the separate localhost protocol, pairing, permissions, commands, and privacy boundary.

### `research_inbox_state`

Reports only lightweight invalidation/count state. It is sent after `browser_state` to a newly authenticated dashboard and after a created save, review-state change, or deletion. Duplicate saves that do not mutate storage do not increment the revision.

```json
{
  "type": "research_inbox_state",
  "messageId": "c3da68ce-54ec-46d6-94d2-cb597fca00d8",
  "timestampUtc": "2026-08-19T12:00:03.1000000+00:00",
  "payload": {
    "revision": 12,
    "totalCount": 47,
    "unreviewedCount": 19,
    "changeType": "saved",
    "changedItemId": 48,
    "lastUpdatedUtc": "2026-08-19T12:00:03.0990000+00:00"
  }
}
```

No title, URL, or selected text appears in this message. `changeType` is `initialized`, `saved`, `reviewed`, or `deleted`; it is presentation/invalidation metadata, not an instruction.

## Authenticated Research Inbox HTTP API

Inbox content uses same-origin HTTP so it can be requested only while the view is open and can be paged independently of real-time PC state. Every request must contain `Authorization: Bearer <dashboard-token>` using the same pre-shared token as `/ws`; tokens are never accepted in a URL or logged. Responses use `Cache-Control: no-store`.

- `GET /api/research-inbox?limit=20&beforeId=48` returns at most 50 newest-first summaries. `beforeId` is an optional exclusive integer cursor. Summaries omit URL and selected-text content and include only `hasSelectedText`.
- `GET /api/research-inbox/{id}` returns one detail including its validated URL and optional selected text.
- `PATCH /api/research-inbox/{id}/reviewed` accepts only `{ "isReviewed": true|false }`.
- `DELETE /api/research-inbox/{id}` deletes one exact stored ID.

Missing/stale IDs return `404`; invalid cursors/page sizes return `400`; missing or incorrect authentication returns `401`. Save and Open remain allowlisted WebSocket commands so they use the existing correlated `command_result` feedback and browser-command security boundary.

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
4. The dashboard sends `client_hello` with protocol version 13, its fresh UTC timestamp, and the configured token.
5. The server checks rate limits, protocol compatibility, timestamp freshness, and the token using constant-time comparison.
6. Only after successful authentication does the server mark the connection ready and send retained `pc_state`, `context_state`, `media_state`, `audio_state`, `spotify_state`, `browser_state`, and `research_inbox_state` snapshots in that order.
7. The authenticated dashboard may send `ping`, `context_selection_request`, or `command_request` messages, while meaningful foreground, context, media, audio, Spotify, browser, and lightweight inbox changes are broadcast independently.
8. The server reads complete messages, validates them, and dispatches only recognized typed records.
9. Either peer may initiate the normal WebSocket close handshake.
10. Application shutdown cancels active receive and polling operations and attempts a bounded close before releasing each socket.
11. The browser reconnects after transport failures unless its page is unloading or authentication requires user action.
