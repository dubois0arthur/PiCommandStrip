[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [System.Net.IPAddress] $ListenAddress
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src\PiCommandStrip.App\PiCommandStrip.App.csproj"

# The dashboard is intentionally exposed only on this explicit LAN address.
$env:DOTNET_ENVIRONMENT = "Lan"
$env:ASPNETCORE_ENVIRONMENT = "Lan"
$env:PiCommandStrip__Network__LanEnabled = "true"
$env:PiCommandStrip__Network__ListenAddress = $ListenAddress.ToString()

# LAN mode reads its token from this user's .NET User Secrets store, then supplies
# it only to the child process. The token is never written to this script or output.
$secretEntries = & dotnet user-secrets list --project $projectPath
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read PiCommandStrip user secrets."
}

$tokenEntry = $secretEntries |
    Where-Object { $_ -match '^PiCommandStrip:Authentication:Token = (?<token>.+)$' } |
    Select-Object -First 1
if ($null -eq $tokenEntry) {
    throw "The PiCommandStrip authentication token is missing from .NET User Secrets."
}

$env:PiCommandStrip__Authentication__Token =
    [regex]::Match($tokenEntry, '^PiCommandStrip:Authentication:Token = (?<token>.+)$').Groups['token'].Value

& dotnet run --project $projectPath --configuration Release --no-launch-profile
exit $LASTEXITCODE
