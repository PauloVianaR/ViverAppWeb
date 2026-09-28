param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 5197
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$secretPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\37c6d69b-4302-409b-a84a-621af30b7867\secrets.json'
if (-not (Test-Path -LiteralPath $secretPath)) {
    throw 'Os user-secrets da API não foram encontrados. Nenhuma instância foi iniciada.'
}

$secrets = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
$source = $secrets.'ConnectionStrings:LocalConnection'
if ([string]::IsNullOrWhiteSpace($source)) {
    throw 'LocalConnection não está configurada. Nenhuma instância foi iniciada.'
}

$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection.set_ConnectionString($source)
if (-not $connection.ContainsKey('Database') -or
    -not [string]::Equals([string]$connection['Database'], 'viverappweb', [StringComparison]::Ordinal)) {
    throw 'LocalConnection não aponta para viverappweb; homologação recusada.'
}
$connection['Database'] = 'viverappweb_homolog'

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://0.0.0.0:$Port"
$env:AllowedHosts = 'localhost;127.0.0.1;host.docker.internal'
$env:ConnectionStrings__LocalConnection = $connection.get_ConnectionString()
$env:Homologation__Enabled = 'true'
$env:Security__AllowInsecureLocalHttp = 'true'
$env:Security__AllowedCorsOrigins__0 = 'http://localhost:5198'
[Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command', 'Warning', 'Process')
[Environment]::SetEnvironmentVariable('Logging__LogLevel__ViverApp.Security.SafeRequestLoggingMiddleware', 'Warning', 'Process')
$env:Authentication__WebReturnUrl = 'http://localhost:5198/auth/result'
$env:Authentication__Delivery__Enabled = 'false'
$env:Notifications__BusinessDelivery__Enabled = 'false'
$env:Notifications__Scheduler__Enabled = 'false'
$env:PagBank__Enabled = 'false'
$env:PagBank__ProductionEnabled = 'false'
$env:Storage__Private__Provider = 'Database'
$env:GoogleOAuth__ClientID = ' '
$env:GoogleOAuth__ClientSecret = ' '
$env:GoogleOAuth__ProjectID = ' '
$env:GoogleOAuth__RedirectURI = ' '

try {
    Push-Location $repositoryRoot
    Write-Host "Iniciando API de homologação sintética em http://localhost:$Port (integrações externas desativadas)."
    dotnet run --project src/ViverApp.Api/ViverApp.Api.csproj --no-launch-profile --no-build
}
finally {
    Pop-Location
    $env:ConnectionStrings__LocalConnection = $null
}
