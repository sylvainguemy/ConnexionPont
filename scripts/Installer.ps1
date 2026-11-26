[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Relais','Agent')][string]$Role,
    [switch]$DemarrerALOuvertureSession
)
$ErrorActionPreference = 'Stop'
$frameworkRelease = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release -ErrorAction Stop).Release
if ($frameworkRelease -lt 528040) { throw 'Installer .NET Framework 4.8 avant de continuer.' }
$exeName = "ConnexionPont.$Role.exe"
$source = Join-Path $PSScriptRoot $exeName
if (-not (Test-Path -LiteralPath $source)) { throw "Extraire le ZIP complet : $exeName est manquant." }
$destination = Join-Path $env:LOCALAPPDATA "ConnexionPont\Applications\$Role"
$destinationFull = [IO.Path]::GetFullPath($destination)
$expectedBase = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'ConnexionPont\Applications')) + [IO.Path]::DirectorySeparatorChar
if (-not $destinationFull.StartsWith($expectedBase,[StringComparison]::OrdinalIgnoreCase)) { throw 'Destination inattendue.' }
New-Item -ItemType Directory -Path $destinationFull -Force | Out-Null
foreach ($name in @($exeName,"$exeName.config",'README.md','INSTALLATION.md','LICENSE','CHANGELOG.md','Desinstaller.ps1','Configurer-PareFeu-Relais.ps1','Verifier-Agent.ps1')) {
    $file = Join-Path $PSScriptRoot $name
    if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination (Join-Path $destinationFull $name) -Force }
}
$shell = New-Object -ComObject WScript.Shell
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'ConnexionPont'
New-Item -ItemType Directory -Path $menu -Force | Out-Null
$shortcutFolders = @([Environment]::GetFolderPath('Desktop'),$menu)
if ($DemarrerALOuvertureSession) { $shortcutFolders += [Environment]::GetFolderPath('Startup') }
foreach ($folder in $shortcutFolders) {
    $shortcut = $shell.CreateShortcut((Join-Path $folder "ConnexionPont - $Role.lnk"))
    $shortcut.TargetPath = Join-Path $destinationFull $exeName
    $shortcut.WorkingDirectory = $destinationFull
    $shortcut.Save()
}
Write-Output "Installé pour votre compte : $destinationFull"
Write-Output 'Lancer le raccourci, puis importer ou créer le profil. Les paramètres Windows et Freebox sont décrits dans INSTALLATION.md.'
