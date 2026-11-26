[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ServeurSsh)
$ErrorActionPreference = 'Stop'
Write-Output 'RDP local (PC client) :'
Test-NetConnection -ComputerName 127.0.0.1 -Port 3389 -InformationLevel Detailed
Write-Output 'SSH du serveur interne :'
Test-NetConnection -ComputerName $ServeurSsh -Port 22 -InformationLevel Detailed
