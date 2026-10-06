[CmdletBinding()]
param(
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repositoryRoot 'ViverApp.slnx'

function Assert-NoSecretsInPublishedSettings {
    param([string]$Directory)
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Filter 'appsettings*.json' -File) {
        $settings = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $pending = [Collections.Generic.Stack[object]]::new()
        $pending.Push($settings)
        while ($pending.Count -gt 0) {
            $current = $pending.Pop()
            foreach ($property in $current.PSObject.Properties) {
                if ($property.Value -is [Management.Automation.PSCustomObject]) {
                    $pending.Push($property.Value)
                }
                elseif ($property.Name -match '(?i)(password|passwd|pwd|secret|token|api[_-]?key|connectionstring)' -and
                    -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                    throw "Configuração sensível preenchida em $($file.Name)."
                }
            }
        }
    }
}

Push-Location $repositoryRoot
try {
    $commit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'Commit Git inválido.' }
    $dirty = @(git status --porcelain=v1)
    if ($LASTEXITCODE -ne 0) { throw 'Estado Git indisponível.' }
    if ($dirty.Count -gt 0 -and -not $AllowDirty) {
        throw 'A publicação exige working tree limpo. -AllowDirty serve somente para ensaio local.'
    }

    $stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
    $suffix = if ($dirty.Count -gt 0) { '-dirty' } else { '' }
    $releaseName = "$stamp-$($commit.Substring(0, 12))$suffix"
    $releaseRoot = Join-Path $repositoryRoot "artifacts\release\$releaseName"
    if (Test-Path -LiteralPath $releaseRoot) { throw 'O diretório de saída já existe.' }
    [void](New-Item -ItemType Directory -Path $releaseRoot)

    dotnet restore $solution --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Restore travado falhou.' }
    foreach ($app in @('Api', 'Web')) {
        $project = Join-Path $repositoryRoot "src\ViverApp.$app\ViverApp.$app.csproj"
        $publishPath = Join-Path $releaseRoot $app.ToLowerInvariant()
        dotnet publish $project --configuration Release --no-restore --output $publishPath
        if ($LASTEXITCODE -ne 0) { throw "Publish de $app falhou." }
        if (-not (Test-Path -LiteralPath (Join-Path $publishPath "ViverApp.$app.dll"))) {
            throw "Publicação de $app incompleta."
        }
        Assert-NoSecretsInPublishedSettings $publishPath
        $developmentSettings = Join-Path $publishPath 'appsettings.Development.json'
        if (Test-Path -LiteralPath $developmentSettings -PathType Leaf) {
            Remove-Item -LiteralPath $developmentSettings
        }
        $archive = Join-Path $releaseRoot "viverapp-$($app.ToLowerInvariant()).zip"
        Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $archive -CompressionLevel Optimal
    }
    $migrationArchive = Join-Path $releaseRoot 'viverapp-database.zip'
    Compress-Archive -Path (Join-Path $repositoryRoot 'database\migrations'), (Join-Path $repositoryRoot 'database\rollbacks') `
        -DestinationPath $migrationArchive -CompressionLevel Optimal

    $artifacts = @('api', 'web', 'database') | ForEach-Object {
        $file = Get-Item -LiteralPath (Join-Path $releaseRoot "viverapp-$_.zip")
        [ordered]@{
            file = $file.Name
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            bytes = $file.Length
        }
    }
    $manifest = [ordered]@{
        schema = 1
        commit = $commit
        dirty = $dirty.Count -gt 0
        createdUtc = (Get-Date).ToUniversalTime().ToString('O')
        target = 'Windows / IIS / .NET 10'
        artifacts = $artifacts
    }
    $manifestPath = Join-Path $releaseRoot 'manifest.json'
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    Write-Host "Pacote criado em $releaseRoot. Manifesto e SHA-256 prontos para verificação no servidor."
}
finally {
    Pop-Location
}
