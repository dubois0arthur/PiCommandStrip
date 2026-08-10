# Raspberry Pi LAN setup

## Security scope

LAN mode exposes the PiCommandStrip HTTP and WebSocket server to one explicitly selected PC network address. Authentication uses a random 32-byte pre-shared token, and commands remain restricted to the server allowlist.

This version does **not** use HTTPS. The token is sent over the LAN during `client_hello`, and foreground-window titles, command requests, and results are also unencrypted. A device able to observe or alter traffic on the local network can steal the token or modify the session. Use only a trusted Private network. Do not configure router port forwarding, expose TCP port 5077 to the internet, or use this on public Wi-Fi. HTTPS is required before treating the transport as secure against network observers.

## 1. Generate the token on Windows

Open PowerShell in the repository root:

```powershell
[byte[]]$tokenBytes = New-Object byte[] 32
$randomNumberGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $randomNumberGenerator.GetBytes($tokenBytes)
    $token = [Convert]::ToBase64String($tokenBytes)
} finally {
    $randomNumberGenerator.Dispose()
}

$token
```

Save the displayed value in a password manager. The server requires exactly 32 random bytes encoded as Base64. Do not put it in a tracked configuration file or frontend source.

For local Development runs, store it with .NET User Secrets:

```powershell
dotnet user-secrets set "PiCommandStrip:Authentication:Token" "$token" --project src/PiCommandStrip.App/PiCommandStrip.App.csproj
```

## 2. Identify the PC's LAN address

Run:

```powershell
ipconfig
```

Find the active Ethernet or Wi-Fi adapter and note its **IPv4 Address**, for example `192.168.1.42`. Ignore loopback (`127.0.0.1`), disconnected adapters, VPN adapters unless intentionally used, and automatic `169.254.x.x` addresses.

The address should be reserved for the PC in the router's DHCP settings so it does not unexpectedly change. PiCommandStrip deliberately rejects wildcard addresses such as `0.0.0.0`; LAN binding must name the intended PC address.

## 3. Start explicit LAN mode

In the same PowerShell session, substitute the PC address you found:

```powershell
$env:DOTNET_ENVIRONMENT = "Lan"
$env:ASPNETCORE_ENVIRONMENT = "Lan"
$env:PiCommandStrip__Network__LanEnabled = "true"
$env:PiCommandStrip__Network__ListenAddress = "192.168.1.42"
$env:PiCommandStrip__Authentication__Token = $token
Set-Location "C:\Users\arthu\Documents\GitHub\PiCommandStrip\src\PiCommandStrip.App"
dotnet run --configuration Release --no-build --no-launch-profile
```

LAN configuration uses fixed port `5077`. To deliberately select a different fixed port, also set `PiCommandStrip__Network__Port`, and use the same port in the firewall rule and URL.

At startup, the application prints the usable dashboard URL, for example:

```text
Pi Command Strip dashboard available at http://192.168.1.42:5077 (LAN mode)
```

`PiCommandStrip__Network__LanEnabled = "true"` is deliberately included even though the `Lan` environment also loads `appsettings.Lan.json`. It makes the LAN opt-in explicit and prevents an old Development launch profile or shortcut from leaving the app in loopback-only mode. Do not use a local Development shortcut to start this command; run it directly in this same PowerShell window.

Environment variables apply only to the current PowerShell process and child application. For a future Windows service deployment, configure equivalent service-level secret and network settings rather than committing them.

### Publish before creating a shortcut

The `bin\Release\net10.0` folder is a development build output. It does not include the deployable `wwwroot` dashboard files, so a shortcut to its `.exe` can return HTTP 404 at `/`.

After stopping the running server with `Ctrl+C`, publish a deployment folder:

```powershell
Set-Location "C:\Users\arthu\Documents\GitHub\PiCommandStrip"
dotnet publish src\PiCommandStrip.App\PiCommandStrip.App.csproj --configuration Release --output "C:\PiCommandStrip"
```

The published `C:\PiCommandStrip` folder includes `wwwroot`. The host detects that published web root even when a Windows shortcut has a different working directory. A shortcut or launcher should start `C:\PiCommandStrip\PiCommandStrip.App.exe` while supplying the same explicit LAN environment variables.

Do not reuse this terminal for a loopback-only Development run without first removing the LAN address variables. Development mode intentionally rejects a non-loopback `ListenAddress`.

## 4. Create a Windows Firewall rule

First confirm that Windows classifies the active network as **Private**. Then open PowerShell **as Administrator** and run the following, substituting the PC address and port:

```powershell
New-NetFirewallRule -DisplayName "PiCommandStrip LAN" -Direction Inbound -Action Allow -Protocol TCP -LocalAddress "192.168.1.42" -LocalPort 5077 -RemoteAddress LocalSubnet -Profile Private
```

This permits TCP traffic only to that local address and port, only from the local subnet, and only while the network has the Private profile. Do not create an `Any`-profile rule.

To inspect the rule later:

```powershell
Get-NetFirewallRule -DisplayName "PiCommandStrip LAN"
```

## 5. Connect from the Raspberry Pi

1. Connect the Pi to the same trusted LAN as the Windows PC.
2. Confirm the Pi's date and time are correct; authentication timestamps allow at most one minute of clock difference.
3. Open Chromium on the Pi.
4. Browse to the startup URL, such as `http://192.168.1.42:5077`.
5. Enter the pre-shared token and press **Connect**.
6. Confirm the header says **Authenticated**, then test **Ping**.
7. Test **Open Notepad** and confirm Notepad opens on the Windows PC.

The token is kept in browser `sessionStorage`, not in the frontend files. It survives a page reload in that tab but is normally removed when the tab or browser session closes. Clearing site data also removes it.

## 6. Stop LAN mode

Press `Ctrl+C` in the server terminal. To clear the sensitive values from that PowerShell session:

```powershell
Remove-Item Env:PiCommandStrip__Authentication__Token
Remove-Item Env:PiCommandStrip__Network__ListenAddress
Remove-Item Env:PiCommandStrip__Network__LanEnabled
Remove-Item Env:DOTNET_ENVIRONMENT
Remove-Item Env:ASPNETCORE_ENVIRONMENT
```

The firewall rule remains until explicitly removed. Keeping it is reasonable only while you intend to use PiCommandStrip on that Private network.
