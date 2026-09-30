[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Este pré-voo deve ser executado no Windows Server de destino.' }
$operatingSystem = Get-CimInstance Win32_OperatingSystem
if ($operatingSystem.ProductType -eq 1) { throw 'O destino precisa ser Windows Server, não Windows cliente.' }
if (-not (Get-Service W3SVC -ErrorAction SilentlyContinue)) { throw 'IIS/W3SVC não está instalado.' }
Import-Module WebAdministration -ErrorAction Stop
if (-not (Get-WebGlobalModule -Name AspNetCoreModuleV2 -ErrorAction SilentlyContinue)) {
    throw 'ASP.NET Core Hosting Bundle/ANCM V2 não foi encontrado.'
}
$runtimes = & dotnet --list-runtimes
if ($LASTEXITCODE -ne 0 -or -not ($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 10\.' })) {
    throw 'Runtime ASP.NET Core 10 não foi encontrado.'
}
if (Get-Command Get-WindowsFeature -ErrorAction SilentlyContinue) {
    $webSockets = Get-WindowsFeature Web-WebSockets
    if (-not $webSockets.Installed) { throw 'Recurso IIS WebSocket Protocol não está habilitado.' }
}
Write-Host 'Windows Server, IIS, ANCM V2, ASP.NET Core 10 e WebSocket conferidos.'
Write-Host 'Este pré-voo não valida MySQL, TLS, DNS, firewall, backup, scanner nem segredos.'
