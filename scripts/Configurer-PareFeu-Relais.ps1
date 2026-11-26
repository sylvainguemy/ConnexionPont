[CmdletBinding()]
param(
    [ValidateRange(1,65535)][int]$Port = 8443,
    [string]$Programme = (Join-Path $env:LOCALAPPDATA 'ConnexionPont\Applications\Relais\ConnexionPont.Relais.exe')
)
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Ouvrir PowerShell en administrateur pour ajouter la règle.' }
$exePath = (Resolve-Path -LiteralPath $Programme).ProviderPath
if ([IO.Path]::GetFileName($exePath) -ne 'ConnexionPont.Relais.exe') { throw 'Choisir le véritable EXE relais.' }
$existing = Get-NetFirewallRule -Name 'ConnexionPont-Relais-TLS' -ErrorAction SilentlyContinue
if ($existing) {
    $existing | Set-NetFirewallRule -Enabled True -Action Allow -Direction Inbound -Profile Any
    $existing | Get-NetFirewallPortFilter | Set-NetFirewallPortFilter -Protocol TCP -LocalPort $Port
    $existing | Get-NetFirewallApplicationFilter | Set-NetFirewallApplicationFilter -Program $exePath
} else {
    New-NetFirewallRule -Name 'ConnexionPont-Relais-TLS' -DisplayName 'ConnexionPont - tunnel TLS entrant' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Program $exePath -Profile Any | Out-Null
}
Write-Output "Règle active : TCP $Port vers $exePath. Configurer séparément la redirection Freebox."
