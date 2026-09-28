[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^ghcr\.io/zaproxy/zaproxy@sha256:[a-f0-9]{64}$')]
    [string]$PinnedImage,

    [Parameter(Mandatory)]
    [ValidatePattern('^http://host\.docker\.internal:[0-9]{2,5}/$')]
    [string]$TargetUrl
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$reportDirectory = Join-Path $repositoryRoot 'artifacts\dast'
[void](New-Item -ItemType Directory -Path $reportDirectory -Force)

& docker info --format '{{.ServerVersion}}' *> $null
if ($LASTEXITCODE -ne 0) { throw 'Docker indisponível; o DAST passivo não foi executado.' }

# Baseline faz spider curto e análise passiva. Nunca usar zap-full-scan ou activeScan aqui.
$dockerArgs = @('run', '--rm', '--network', 'bridge', '-v', "${reportDirectory}:/zap/wrk:rw",
    $PinnedImage, 'zap-baseline.py', '-t', $TargetUrl, '-m', '1', '-T', '3',
    '-J', 'phase25-zap.json', '-r', 'phase25-zap.html')
& docker @dockerArgs
if ($LASTEXITCODE -ne 0) { throw "DAST passivo retornou código $LASTEXITCODE; revise os relatórios antes de avançar." }
Write-Host "Relatórios DAST em $reportDirectory. Nenhum scanner ativo foi executado."
