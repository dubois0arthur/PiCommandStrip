# Firefox browser integration

## Scope

The first browser integration is a Firefox WebExtension that reports the active tab to the Windows PiCommandStrip host. It enriches the existing Browser / Research context; it does not replace foreground-window context selection and it does not add browser commands.

```text
Firefox extension
    |
    | ws://127.0.0.1:5078/browser-integration/ws
    | separate pairing token; loopback only
    v
Windows IBrowserIntegrationService
    |
    | browser_state on the existing authenticated /ws connection
    v
Raspberry Pi dashboard
```

The Pi never connects to the extension. The extension listener is separate from the fixed dashboard port and is bound only on Windows loopback. Requests on the LAN dashboard listener receive `404` for the bridge route.

## Mozilla API choices and limitations

The extension uses the current Manifest V3 format and Firefox `background.scripts`. Firefox does not currently support `background.service_worker`; Manifest V3 background scripts are non-persistent event pages. Normal-page content scripts keep a runtime message port open while present, and every event-page restart reconstructs configuration and active-tab state from Firefox APIs. See Mozilla's [background manifest documentation](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/manifest.json/background).

Requested permissions and declarations are:

- `tabs`: reads privileged active-tab `url` and `title` without waiting for a toolbar click. Mozilla documents those privileged properties in the [permissions reference](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/manifest.json/permissions).
- `storage`: stores only the pairing token, configured loopback port, and a random browser-instance ID in the Windows Firefox profile.
- HTTP/HTTPS `content_scripts.matches`: observes `window.getSelection()` in the top frame. It sends only the selection string, never the page body. Firefox restricted pages and pages where the user withholds site access cannot be observed.
- `websiteActivity` and `websiteContent` in `data_collection_permissions`: Mozilla now requires Manifest V3 signing metadata to describe data sent outside the extension. URL/title and selected text are declared explicitly even though their only destination is the same PC. See [`browser_specific_settings`](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/manifest.json/browser_specific_settings).

It does not request history, bookmarks, cookies, clipboard, webRequest, downloads, native messaging, or all page contents.

Firefox exposes `tabs.goBack()` and `tabs.goForward()` operations but no reliable, non-mutating query for whether each operation is currently available. PiCommandStrip therefore carries nullable `canGoBack`/`canGoForward` fields and reports `null` in this version. It does not probe by navigating the page.

Firefox Manifest V3's default Content Security Policy includes `upgrade-insecure-requests`. The local extension overrides that policy only for `ws://127.0.0.1:*`; no remote connection is allowed. Mozilla documents local insecure-source support for temporarily loaded Manifest V3 extensions from Firefox 147, so the manifest sets that minimum version. A future signed distribution may need WSS or additional Mozilla policy work even though the traffic never leaves loopback. See Mozilla's [extension CSP documentation](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/Content_Security_Policy.).

## Pairing and manual installation

From the repository root, generate a new 32-byte token. Do not reuse the Pi dashboard token:

```powershell
[byte[]]$browserTokenBytes = New-Object byte[] 32
$browserRandom = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $browserRandom.GetBytes($browserTokenBytes)
    $browserPairingToken = [Convert]::ToBase64String($browserTokenBytes)
} finally {
    $browserRandom.Dispose()
}

$browserPairingToken
dotnet user-secrets set "PiCommandStrip:BrowserIntegration:Enabled" "true" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
dotnet user-secrets set "PiCommandStrip:BrowserIntegration:Port" "5078" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
dotnet user-secrets set "PiCommandStrip:BrowserIntegration:Token" "$browserPairingToken" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
```

Restart the host after changing configuration. Then follow Mozilla's [temporary installation instructions](https://extensionworkshop.com/documentation/develop/temporary-installation-in-firefox/):

1. Open `about:debugging` in Firefox.
2. Select **This Firefox** and **Load Temporary Add-on**.
3. Select `browser-extension/firefox/manifest.json`.
4. Open `about:addons`, find **PiCommandStrip Firefox Bridge**, and open Preferences.
5. Paste the browser pairing token, keep port `5078`, and select **Save and connect**.

A temporary add-on stays installed only until it is removed or Firefox restarts. Reload it from `about:debugging` during development. The fixed Gecko extension ID keeps its storage identity stable for supported install/reload workflows.

## Loopback protocol

The extension protocol is version `1`, separate from the Pi dashboard protocol. Messages are exact-shape UTF-8 JSON envelopes and are limited to 8,192 bytes.

The first message is `browser_hello`:

```json
{
  "type": "browser_hello",
  "messageId": "9bf38286-4603-4515-8ad2-84775abe3fd0",
  "timestampUtc": "2026-08-18T12:00:00.000Z",
  "payload": {
    "protocolVersion": "1",
    "authenticationToken": "<separate 32-byte Base64 token>",
    "browserType": "firefox",
    "sourceIdentifier": "firefox-bridge@picommandstrip.local",
    "instanceIdentifier": "<random Firefox-profile instance ID>"
  }
}
```

After `browser_bridge_ready`, the extension sends full `browser_state_update` snapshots only when their meaning changes:

```json
{
  "type": "browser_state_update",
  "messageId": "dc0b68f1-77b2-45fb-8170-9f030a2fb664",
  "timestampUtc": "2026-08-18T12:00:01.000Z",
  "payload": {
    "activeTabId": 42,
    "url": "https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions",
    "title": "Browser extensions - Mozilla | MDN",
    "selectedText": null,
    "canGoBack": null,
    "canGoForward": null
  }
}
```

The listener accepts no command type. Unknown fields/types, binary data, malformed JSON, oversized messages, stale hello timestamps, and failed pairing are rejected. Failed pairing uses a process-wide five-attempt/30-second limiter dedicated to this endpoint.

## Normalization, privacy, and lifetime

- Only absolute HTTP/HTTPS URLs are accepted. The host removes user information and fragments, derives an IDN-normalized lowercase hostname, and caps URLs at 2,048 characters.
- Titles are capped at 512 UTF-16 characters.
- Selected text is trimmed and safely capped at 1,000 UTF-16 characters without splitting a surrogate pair.
- Selected text exists only in extension/host memory. It is never persisted or included in normal logs.
- A tab switch, navigation, selection disappearance, or active bridge disconnect clears selected text. A disconnect clears all active-tab metadata.
- The Pi's `browser_state` includes URL/title metadata but only `hasSelectedText`, never the selected text itself.
- Reconnect uses bounded exponential backoff with jitter. Full state is resent after authentication; server and client both suppress meaning-identical snapshots.
- If a second authenticated Firefox instance connects, it becomes authoritative. A later disconnect from the older socket cannot erase the newer state.

## Verification

1. Start PiCommandStrip and confirm a second `Now listening on: http://localhost:5078` message appears only after browser integration is enabled.
2. Pair the extension and verify its Preferences page says **Connected**.
3. Focus Firefox and authenticate the Pi dashboard. Browser / Research should show the active page title, hostname, and connected state.
4. Select and deselect text on a normal HTTP/HTTPS page; the Pi should change between **Text selected** and **No text selected**, without showing the selected content.
5. Switch tabs and navigate. Title/hostname should update and the selection indicator should clear.
6. Close or disable the extension. The Pi should show the bridge as offline while foreground Firefox still selects Browser / Research.
7. Put another application in the foreground. Its context should still be chosen exclusively by the existing foreground process resolver.
