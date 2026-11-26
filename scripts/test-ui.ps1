[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appFolder = Join-Path $projectRoot 'dist\ConnexionPont'
$testFolder = Join-Path $projectRoot 'artifacts\ui'
New-Item -ItemType Directory -Path $testFolder -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$assembly = Join-Path $appFolder 'ConnexionPont.Relais.exe'
if (-not (Test-Path -LiteralPath $assembly)) { throw 'Compiler les applications avant le test UI.' }
Copy-Item -LiteralPath $assembly -Destination $testFolder -Force
Copy-Item -LiteralPath "$assembly.config" -Destination $testFolder -Force
& $compiler /nologo /target:exe /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$assembly" "/out:$testFolder\UiSmoke.exe" (Join-Path $projectRoot 'tests\UiSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation du test UI en échec.' }
& (Join-Path $testFolder 'UiSmoke.exe')
if ($LASTEXITCODE -ne 0) { throw 'Construction des interfaces en échec.' }
