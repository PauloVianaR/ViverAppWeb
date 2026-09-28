[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^ghcr\.io/zaproxy/zaproxy@sha256:[a-f0-9]{64}$')]
    [string]$PinnedImage
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$reportDirectory = Join-Path $repositoryRoot 'artifacts\dast'
[void](New-Item -ItemType Directory -Path $reportDirectory -Force)
$env:DOCKER_CONFIG = Join-Path $reportDirectory 'docker-config'
[void](New-Item -ItemType Directory -Path $env:DOCKER_CONFIG -Force)

$ready = Invoke-RestMethod -Uri 'http://localhost:5197/health/ready' -TimeoutSec 10
if ($ready.status -ne 'Healthy') { throw 'A API de homologação não está pronta.' }

$randomBytes = [byte[]]::new(24)
$randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $randomGenerator.GetBytes($randomBytes) }
finally { $randomGenerator.Dispose() }
$env:VIVERAPP_HOMOLOG_QA_PASSWORD = ([BitConverter]::ToString($randomBytes) -replace '-', '') + 'Aa1!'
try {
    Push-Location $repositoryRoot
    try {
        dotnet run --project tools/ViverApp.Database --no-build -- seed-homolog --homolog
        if ($LASTEXITCODE -ne 0) { throw 'Não foi possível preparar a conta sintética.' }
    }
    finally { Pop-Location }

    $session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $token = Invoke-RestMethod -Uri 'http://localhost:5197/api/v1/auth/antiforgery' -WebSession $session -TimeoutSec 10
    $body = @{
        identifier = 'qa-manager@viverapp.invalid'
        password = $env:VIVERAPP_HOMOLOG_QA_PASSWORD
    } | ConvertTo-Json -Compress
    $login = Invoke-RestMethod -Uri 'http://localhost:5197/api/v1/auth/login/password' -Method Post `
        -Body $body -ContentType 'application/json' -Headers @{ 'X-CSRF-TOKEN' = $token.requestToken } `
        -WebSession $session -TimeoutSec 15
    if (-not $login.authenticated -or $login.role -ne 'manager') {
        throw 'Login sintético de Gestor recusado; o DAST ativo não foi iniciado.'
    }
    $homeResponse = Invoke-WebRequest -Uri 'http://localhost:5197/api/v1/manager/patients?search=Paciente' -UseBasicParsing `
        -WebSession $session -TimeoutSec 15
    if ($homeResponse.StatusCode -ne 200) { throw 'Rota autenticada não respondeu 200.' }

    $sessionCookie = $session.Cookies.GetCookies('http://localhost:5197') |
        Where-Object { $_.Name -eq 'ViverApp.Session.Local' } | Select-Object -First 1
    if ($null -eq $sessionCookie) { throw 'Cookie de sessão sintética não encontrado.' }
    $env:QA_COOKIE = "$($sessionCookie.Name)=$($sessionCookie.Value)"
    Write-Host 'Conta sintética autenticada; iniciando DAST ativo limitado à busca GET de pacientes do Gestor.'

    $preflightPath = Join-Path $PSScriptRoot 'dast-homolog-manager-preflight.yaml'
    $dockerBase = @('run', '--rm', '--network', 'bridge', '-e', 'QA_COOKIE',
        '-v', "${reportDirectory}:/zap/wrk:rw")
    $rendererPath = Join-Path $PSScriptRoot 'render-zap-homolog-plan.py'
    $generatePlan = 'python3 /zap/wrk/render.py && zap.sh -cmd -autorun /tmp/plan.yaml'
    $preflightArgs = $dockerBase + @('-v', "${preflightPath}:/zap/wrk/plan.template.yaml:ro",
        '-v', "${rendererPath}:/zap/wrk/render.py:ro",
        $PinnedImage, 'sh', '-c', $generatePlan)
    $ErrorActionPreference = 'Continue'
    try {
        $preflightOutput = & docker @preflightArgs 2>&1
        $preflightExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = 'Stop' }
    if ($preflightExitCode -ne 0 -or ($preflightOutput | Out-String) -match 'Expected : 200 Received :') {
        $summary = (($preflightOutput | Select-Object -Last 8) -join '; ') -replace
            [regex]::Escape($env:QA_COOKIE), '[redacted]'
        throw "O ZAP não confirmou HTTP 200 na rota autenticada (código $preflightExitCode): $summary. DAST ativo não foi iniciado."
    }
    Write-Host 'Pré-voo do ZAP confirmou HTTP 200 na rota autenticada.'

    $planPath = Join-Path $PSScriptRoot 'dast-homolog-manager.yaml'
    $dockerArgs = $dockerBase + @('-v', "${planPath}:/zap/wrk/plan.template.yaml:ro",
        '-v', "${rendererPath}:/zap/wrk/render.py:ro",
        $PinnedImage, 'sh', '-c', $generatePlan)
    $ErrorActionPreference = 'Continue'
    try {
        $scanOutput = & docker @dockerArgs 2>$null
        $scanExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = 'Stop' }
    $safeSummary = ($scanOutput | Where-Object {
        $_ -match 'Job requestor requesting|Difference in response code|Job activeScan (started|finished)|Job report (started|finished)|WARN|FAIL|ERROR'
    } | Select-Object -Last 35) -join [Environment]::NewLine
    $safeSummary = $safeSummary -replace [regex]::Escape($env:QA_COOKIE), '[redacted]'
    Write-Host $safeSummary
    if ($scanExitCode -ne 0) {
        $diagnostic = (($scanOutput | Select-Object -Last 8) -join '; ') -replace
            [regex]::Escape($env:QA_COOKIE), '[redacted]'
        throw "DAST ativo retornou código ${scanExitCode}: $diagnostic. Revise o relatório."
    }
    Write-Host 'DAST ativo limitado concluído; relatório em artifacts/dast/phase25-homolog-manager-search-active.json.'
}
finally {
    $env:VIVERAPP_HOMOLOG_QA_PASSWORD = $null
    $env:QA_COOKIE = $null
}
