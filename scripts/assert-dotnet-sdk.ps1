[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdk = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk
if ($sdk.rollForward -cne 'disable') {
    throw 'O SDK deve estar fixado para manter as dependências implícitas compatíveis com os lock files.'
}
Push-Location $repositoryRoot
try {
    $actual = dotnet --version
    if ($LASTEXITCODE -ne 0 -or $actual.Trim() -cne $sdk.version) {
        throw "SDK divergente: esperado $($sdk.version). Instale a versão do global.json."
    }
    Write-Output "SDK verificado: $($sdk.version)."
} finally { Pop-Location }
