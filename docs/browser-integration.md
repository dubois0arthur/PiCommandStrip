# Firefox browser integration

## Scope

The Firefox WebExtension reports concise active-tab/selection state to the Windows PiCommandStrip host and executes a fixed set of host-authorized browser actions. It enriches the existing Browser / Research context; it does not replace foreground-window context selection and it never accepts arbitrary URLs, JavaScript, keyboard input, or shell commands from the Pi. Research Inbox Open is also ID-based from the Pi: the Windows host resolves the stored page and sends its validated web URI over this paired loopback bridge.

```text
Firefox extension
    |
    | ws://127.0.0.1:5078/browser-integration/ws
    | separate pairing token; loopback only
    v
Windows IBrowserIntegrationService
    |
    | browser_state + fixed browser.* requests on authenticated /ws
    v
Raspberry Pi dashboard
```

The Pi never connects to the extension. The extension listener is separate from the fixed dashboard port and is bound only on Windows loopback. Requests on the LAN dashboard listener receive `404` for the bridge route.

## Mozilla API choices and limitations

The extension uses the current Manifest V3 format and Firefox `background.scripts`. Firefox does not currently support `background.service_worker`; Manifest V3 background scripts are non-persistent event pages. Normal-page content scripts keep a runtime message port open while present, and every event-page restart reconstructs configuration and active-tab state from Firefox APIs. See Mozilla's [background manifest documentation](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/manifest.json/background).

Requested permissions and declarations are:

- `tabs`: reads privileged active-tab `url` and `title` without waiting for a toolbar click. Mozilla documents those privileged properties in the [permissions reference](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/manifest.json/permissions).
- `storage`: stores only the pairing token, configured loopback port, and a random browser-instance ID in the Windows Firefox profile.
- `sessions`: finds and restores only the most recently closed tab for `browser.reopenClosedTab`; Firefox restores that tab's navigation history as well.
- `clipboardWrite`: lets the extension background context copy the currently revalidated active tab URL without keyboard emulation or a page script.
- HTTP/HTTPS `content_scripts.matches`: observes `window.getSelection()` in the top frame. It sends only the selection string, never the page body. Firefox restricted pages and pages where the user withholds site access cannot be observed.
- `websiteActivity` and `websiteContent` in `data_collection_permissions`: Mozilla now requires Manifest V3 signing metadata to describe data sent outside the extension. URL/title and selected text are declared explicitly even though their only destination is the same PC. See [`browser_specific_settings`](https://developer.mozilla.org/en-US/docs/Mozilla/Add-ons/WebExtensions/manifest.json/browser_specific_settings).

It does not request browsing history, bookmarks, cookies, clipboard read access, webRequest, downloads, native messaging, or full page bodies.

Firefox exposes `tabs.goBack()` and `tabs.goForward()` operations but no general WebExtension query for whether each operation is currently available. The top-frame content script therefore reports `navigation.canGoBack`/`canGoForward` where Firefox exposes the web Navigation API. The fields remain nullable for restricted/unsupported pages; unknown is disabled rather than guessed, and PiCommandStrip never probes by navigating.

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

## Search action configuration

Google, Wikipedia, and YouTube are ordinary host configuration entries in `PiCommandStrip:BrowserIntegration:SearchActions`. Each key is the stable action ID sent by the Pi; `DisplayName` is presentation text; `UrlTemplate` must be an absolute HTTPS URL with exactly one `{query}` placeholder:

```json
"SearchActions": {
  "google": {
    "DisplayName": "Google",
    "UrlTemplate": "https://www.google.com/search?q={query}"
  }
}
```

Add a future Scholar, PubMed, GitHub, or documentation provider by adding another validated entry—not by adding another command handler. The Windows host encodes the current retained selection and substitutes only the encoded value. Invalid IDs, HTTP templates, credentials in URLs, missing/duplicate placeholders, unknown providers, and empty selections are rejected during startup or command validation.

## Loopback protocol

The extension protocol is version `3`, separate from Pi dashboard protocol version `13`. Client messages are exact-shape UTF-8 JSON envelopes and are limited to 8,192 bytes.

The first message is `browser_hello`:

```json
{
  "type": "browser_hello",
  "messageId": "9bf38286-4603-4515-8ad2-84775abe3fd0",
  "timestampUtc": "2026-08-18T12:00:00.000Z",
  "payload": {
    "protocolVersion": "3",
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
    "canGoBack": true,
    "canGoForward": false
  }
}
```

After authentication the host may send `browser_command` with one of the fixed browser IDs or the internal `research.openSavedUrl` ID. Tab-specific commands include the exact `expectedActiveTabId` retained by the host. The extension queries Firefox again and returns `stale_tab` without acting if focus changed. `browser.searchSelection` may contain only a bounded absolute HTTPS URL constructed by the Windows host from a trusted template. `research.openSavedUrl` may contain only a bounded credential-free HTTP/HTTPS URI already loaded and revalidated from the host's Research Inbox; that command is never exposed as a Pi-supplied arbitrary URL contract.

```json
{
  "type": "browser_command",
  "messageId": "4f7c9e82-a9ba-4b1e-a8ad-313488fc5c11",
  "timestampUtc": "2026-08-18T12:00:02.000Z",
  "payload": {
    "commandId": "browser.reload",
    "expectedActiveTabId": 42,
    "searchUrl": null
  }
}
```

The extension answers with exact-shape `browser_command_result` containing only `requestMessageId`, `succeeded`, and a bounded fixed result code. Unknown fields/types, binary data, malformed JSON, oversized messages, stale hello timestamps, and failed pairing are rejected. Failed pairing uses a process-wide five-attempt/30-second limiter dedicated to this endpoint.

## Normalization, privacy, and lifetime

- Only absolute HTTP/HTTPS URLs are accepted. The host removes user information and fragments, derives an IDN-normalized lowercase hostname, and caps URLs at 2,048 characters.
- Titles are capped at 512 UTF-16 characters.
- Selected text is trimmed and safely capped at 1,000 UTF-16 characters without splitting a surrogate pair.
- Selected text exists only in extension/host/Pi page memory unless the user explicitly taps Research Save. Browser-state receipt never persists it, and selected content is never included in normal logs.
- A tab switch, navigation, selection disappearance, or active bridge disconnect clears selected text. A disconnect clears all active-tab metadata.
- The authenticated Pi receives the bounded selected text only while Browser context is active so it can render a strict 180-character preview. LAN transport is unencrypted and must remain on a trusted Private network.
- Search templates live in Windows host configuration. `server_hello` sends only provider ID/display-name descriptors. The Pi returns only that provider ID; the Windows host reads its retained selection, applies `Uri.EscapeDataString`, and constructs the HTTPS URL.
- Reconnect uses bounded exponential backoff with jitter. Full state is resent after authentication; server and client both suppress meaning-identical snapshots.
- If a second authenticated Firefox instance connects, it becomes authoritative. A later disconnect from the older socket cannot erase the newer state.

## Verification

1. Start PiCommandStrip and confirm a second `Now listening on: http://localhost:5078` message appears only after browser integration is enabled.
2. Pair the extension and verify its Preferences page says **Connected**.
3. Focus Firefox and authenticate the Pi dashboard. Browser / Research should show the active page title, hostname, and compact actions.
4. Select text on a normal HTTP/HTTPS page. Confirm the bounded preview and Google/Wikipedia/YouTube actions appear; deselect and confirm the panel disappears.
5. Exercise Back, Forward, Reload, New Tab, Close Tab, Reopen, and Copy URL. Back/Forward must reflect reported capability; rapidly switch tabs while tapping and confirm a stale action fails without touching the new tab.
6. Close or disable the extension. The Pi should show the bridge as offline while foreground Firefox still selects Browser / Research.
7. Put another application in the foreground. Its context should still be chosen exclusively by the existing foreground process resolver. Browser actions should no longer occupy the workspace.
