[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^viverapp-[a-f0-9]{40}$')]
    [string]$Tag,
    [string]$DestinationRoot = (Join-Path $PSScriptRoot '..\artifacts\incoming')
)

$ErrorActionPreference = 'Stop'
$repository = 'PauloVianaR/ViverAppWeb'
$sha = $Tag.Substring('viverapp-'.Length)
$headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'ViverApp-production-installer' }
$release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/releases/tags/$Tag" -Headers $headers -TimeoutSec 30
if ($release.tag_name -cne $Tag -or $release.target_commitish -cne $sha -or
    $release.immutable -ne $true -or $release.draft -or $release.prerelease) {
    throw 'A release não é imutável, publicada ou vinculada ao commit solicitado.'
}
$tagRef = Invoke-RestMethod -Uri "https://api.github.com/repos/$repository/git/ref/tags/$Tag" -Headers $headers -TimeoutSec 30
if ($tagRef.object.type -cne 'commit' -or $tagRef.object.sha -cne $sha) {
    throw 'A tag da release não aponta para o commit do manifesto.'
}

$names = @('manifest.json', 'deployment-plan.json', 'viverapp-api.zip', 'viverapp-web.zip', 'viverapp-database.zip')
if (@($release.assets).Count -ne $names.Count) { throw 'Quantidade inesperada de arquivos na release.' }
$destination = [IO.Path]::GetFullPath((Join-Path $DestinationRoot $sha))
[void](New-Item -ItemType Directory -Path $destination -Force)
foreach ($name in $names) {
    $matches = @($release.assets | Where-Object name -CEQ $name)
    if ($matches.Count -ne 1) { throw "Arquivo ausente ou duplicado na release: $name" }
    $asset = $matches[0]
    if ($asset.digest -cnotmatch '^sha256:([a-f0-9]{64})$') {
        throw "Digest SHA-256 ausente no GitHub: $name"
    }
    $expected = $Matches[1]
    $url = [uri]$asset.browser_download_url
    $expectedPath = "/$repository/releases/download/$Tag/$name"
    if ($url.Scheme -cne 'https' -or $url.Host -cne 'github.com' -or
        $url.AbsolutePath -cne $expectedPath) {
        throw "URL de download inesperada: $name"
    }
    $path = Join-Path $destination $name
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected) {
            throw "Arquivo local divergente: $name"
        }
        continue
    }
    $part = "$path.part"
    Invoke-WebRequest -Uri $url.AbsoluteUri -OutFile $part -TimeoutSec 180 -UseBasicParsing
    if ((Get-FileHash -LiteralPath $part -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected) {
        throw "Download corrompido: $name"
    }
    Move-Item -LiteralPath $part -Destination $path
}

& (Join-Path $PSScriptRoot 'verify-windows-release.ps1') -ReleaseDirectory $destination
if ($LASTEXITCODE -ne 0) { throw 'Manifesto ou ZIPs inválidos.' }
$manifest = Get-Content -LiteralPath (Join-Path $destination 'manifest.json') -Raw | ConvertFrom-Json
$plan = Get-Content -LiteralPath (Join-Path $destination 'deployment-plan.json') -Raw | ConvertFrom-Json
if ($manifest.commit -cne $sha -or $plan.schema -ne 1 -or $plan.commit -cne $sha -or
    $null -eq $plan.expectedPendingMigrations -or $null -eq $plan.binaryRollbackCompatible) {
    throw 'Plano de implantação incompatível com a release.'
}
Write-Output "Release verificada: $destination"
