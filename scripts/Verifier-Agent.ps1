[CmdletBinding()]
param([string]$ServeurSsh,[string]$ServeurWeb)
$ErrorActionPreference = 'Stop'
Write-Output 'RDP local (PC client) :'
Test-NetConnection -ComputerName 127.0.0.1 -Port 3389 -InformationLevel Detailed
if ($ServeurSsh) {
    Write-Output 'SSH du serveur interne :'
    Test-NetConnection -ComputerName $ServeurSsh -Port 22 -InformationLevel Detailed
}
if ($ServeurWeb) {
    Write-Output 'Serveur web interne, port 8080 :'
    Test-NetConnection -ComputerName $ServeurWeb -Port 8080 -InformationLevel Detailed
    Write-Output 'Serveur web interne, port 8090 :'
    Test-NetConnection -ComputerName $ServeurWeb -Port 8090 -InformationLevel Detailed
}
