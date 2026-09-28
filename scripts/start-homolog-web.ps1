param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 5198
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://0.0.0.0:$Port"
$env:AllowedHosts = 'localhost;127.0.0.1;host.docker.internal'
$env:Security__AllowInsecureLocalHttp = 'true'
$env:Security__AllowedConnectSources__0 = 'http://localhost:5197'
$env:Backend__BaseUrl = 'http://localhost:5197'

Push-Location $repositoryRoot
try {
    Write-Host "Iniciando Web de homologação em http://localhost:$Port."
    dotnet run --project src/ViverApp.Web/ViverApp.Web.csproj --configuration Release --no-launch-profile --no-build
}
finally {
    Pop-Location
}
