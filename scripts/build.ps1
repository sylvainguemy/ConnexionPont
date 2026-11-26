[CmdletBinding()]
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'test.ps1') }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 / compilateur C# Windows requis.' }
$output = Join-Path $projectRoot 'dist\ConnexionPont'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
foreach ($kind in @('Relais','Agent')) {
    $define = if ($kind -eq 'Relais') { 'RELAY' } else { 'AGENT' }
    $exe = Join-Path $output "ConnexionPont.$kind.exe"
    & $compiler /nologo /warn:4 /nowarn:4014 /optimize+ /target:winexe /platform:anycpu "/define:$define" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll /r:System.Security.dll "/win32manifest:$projectRoot\src\app.manifest" "/out:$exe" @sources
    if ($LASTEXITCODE -ne 0) { throw "Compilation $kind en échec." }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination "$exe.config"
}
foreach ($name in @('README.md','INSTALLATION.md','LICENSE','CHANGELOG.md')) {
    if (Test-Path -LiteralPath (Join-Path $projectRoot $name)) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $output }
}
$installScripts = @('Configurer-PareFeu-Relais.ps1','Verifier-Agent.ps1','Installer.ps1','Desinstaller.ps1')
foreach ($name in $installScripts) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $name)) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $output }
}
$archive = Join-Path $projectRoot 'dist\ConnexionPont-Windows-v0.1.0.zip'
$deliveryNames = @('ConnexionPont.Relais.exe','ConnexionPont.Relais.exe.config','ConnexionPont.Agent.exe','ConnexionPont.Agent.exe.config','README.md','INSTALLATION.md','LICENSE','CHANGELOG.md') + $installScripts
$deliveryPaths = $deliveryNames | ForEach-Object { Join-Path $output $_ } | Where-Object { Test-Path -LiteralPath $_ }
Compress-Archive -LiteralPath $deliveryPaths -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
Write-Output $archive
