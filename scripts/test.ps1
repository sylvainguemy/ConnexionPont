[CmdletBinding()]
param([switch]$CryptoOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @('Settings.cs','Transport.cs','Socks5.cs','WebBrowserLauncher.cs','Relay.cs','Agent.cs') | ForEach-Object { Join-Path $projectRoot "src\$_" }
& $compiler /nologo /warn:4 /nowarn:4014 /target:exe /platform:anycpu /r:System.Runtime.Serialization.dll /r:System.Security.dll "/out:$output\RdpTunnel.Tests.exe" @sources (Join-Path $projectRoot 'tests\Tests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation des tests en échec.' }
$testArguments = @()
if ($CryptoOnly) { $testArguments += '--crypto-only' }
& (Join-Path $output 'RdpTunnel.Tests.exe') @testArguments | Tee-Object -FilePath (Join-Path $output 'test-results.txt')
if ($LASTEXITCODE -ne 0) { throw 'Tests en échec.' }
