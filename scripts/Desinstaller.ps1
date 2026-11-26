[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Acces','Pont','Relais','Agent')][string]$Role,
    [switch]$EffacerConfiguration
)
$ErrorActionPreference = 'Stop'
$Role = switch ($Role) { 'Acces' { 'Relais' } 'Pont' { 'Agent' } default { $Role } }
$displayRole = if ($Role -eq 'Relais') { 'Acces chez vous' } else { 'Pont chez le client' }
$base = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'ConnexionPont'))
$application = [IO.Path]::GetFullPath((Join-Path $base "Applications\$Role"))
if (-not $application.StartsWith($base + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Chemin inattendu.' }
$exeName = "ConnexionPont.$Role.exe"
$running = Get-Process -Name "ConnexionPont.$Role" -ErrorAction SilentlyContinue
if ($running) { throw "Fermer ConnexionPont $Role avant de désinstaller." }
foreach ($name in @($exeName,"$exeName.config",'README.md','INSTALLATION.md','LICENSE','CHANGELOG.md','Desinstaller.ps1','Configurer-PareFeu-Relais.ps1','Verifier-Agent.ps1')) {
    $file = Join-Path $application $name
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
}
if ((Test-Path -LiteralPath $application) -and @(Get-ChildItem -LiteralPath $application -Force).Count -eq 0) { Remove-Item -LiteralPath $application }
foreach ($folder in @([Environment]::GetFolderPath('Desktop'),(Join-Path ([Environment]::GetFolderPath('Programs')) 'ConnexionPont'),[Environment]::GetFolderPath('Startup'))) {
    foreach ($label in @($Role,$displayRole)) {
        $shortcut = Join-Path $folder "ConnexionPont - $label.lnk"
        if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    }
}
if ($EffacerConfiguration) {
    foreach ($name in @('settings.bin','settings.bin.new')) {
        $file = Join-Path $base "$Role\$name"
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
}
Write-Output "Application $Role désinstallée. Configuration conservée sauf option -EffacerConfiguration."
Write-Output 'Retirer séparément la redirection Freebox et la règle Windows si elles ne sont plus utilisées.'
