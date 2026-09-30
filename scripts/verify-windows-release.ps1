[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReleaseDirectory
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ReleaseDirectory)
$manifestPath = Join-Path $root 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Manifesto da release ausente.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.commit -notmatch '^[a-f0-9]{40}$' -or $manifest.dirty) {
    throw 'Manifesto inválido ou publicação gerada de working tree sujo.'
}
$expected = @('viverapp-api.zip', 'viverapp-web.zip', 'viverapp-database.zip')
if (@($manifest.artifacts).Count -ne 3) { throw 'A release deve ter exatamente três artefatos.' }
foreach ($item in $manifest.artifacts) {
    if ($item.file -cnotin $expected -or $item.sha256 -notmatch '^[a-f0-9]{64}$') {
        throw 'Artefato ou digest inválido no manifesto.'
    }
    $path = Join-Path $root $item.file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Artefato ausente: $($item.file)." }
    $file = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($file.Length -ne $item.bytes -or $hash -cne $item.sha256) {
        throw "Falha de integridade: $($item.file)."
    }
}
$actualNames = @($manifest.artifacts.file | Sort-Object) -join ','
$expectedNames = @($expected | Sort-Object) -join ','
if ($actualNames -cne $expectedNames) {
    throw 'O manifesto não contém API, Web e migrations exatamente uma vez.'
}
Write-Host "Integridade SHA-256 aprovada para o commit $($manifest.commit)."
