[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$components = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$edges = [Collections.Generic.Dictionary[string, Collections.Generic.HashSet[string]]]::new([StringComparer]::Ordinal)
$lockFiles = @('src', 'tests', 'tools') | ForEach-Object {
    Get-ChildItem -LiteralPath (Join-Path $repositoryRoot $_) -Recurse -File -Filter 'packages.lock.json' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }
}
if ($lockFiles.Count -eq 0) { throw 'Nenhum lock file NuGet foi encontrado.' }

function Get-PackageRef([string]$name, [string]$version) {
    return "pkg:nuget/$([uri]::EscapeDataString($name.ToLowerInvariant()))@$([uri]::EscapeDataString($version))"
}

foreach ($file in $lockFiles) {
    $lock = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -AsHashtable
    foreach ($framework in $lock.dependencies.Values) {
        foreach ($name in $framework.Keys) {
            $package = $framework[$name]
            if (-not $package.resolved -or $package.type -eq 'Project') { continue }
            $reference = Get-PackageRef $name $package.resolved
            $hash = [Convert]::ToHexString([Convert]::FromBase64String($package.contentHash)).ToLowerInvariant()
            if ($components.ContainsKey($reference)) {
                if ($components[$reference].hashes[0].content -ne $hash) {
                    throw "Hashes NuGet divergentes para $name $($package.resolved)."
                }
            }
            else {
                $components[$reference] = [ordered]@{
                    type = 'library'
                    'bom-ref' = $reference
                    name = $name
                    version = $package.resolved
                    purl = $reference
                    hashes = @(@{ alg = 'SHA-512'; content = $hash })
                }
            }
            if (-not $edges.ContainsKey($reference)) {
                $edges[$reference] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            }
            if ($package.dependencies) {
                foreach ($dependencyName in $package.dependencies.Keys) {
                    $dependency = $framework[$dependencyName]
                    if ($dependency -and $dependency.resolved -and $dependency.type -ne 'Project') {
                        [void]$edges[$reference].Add((Get-PackageRef $dependencyName $dependency.resolved))
                    }
                }
            }
        }
    }
}

$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Não foi possível identificar o commit do SBOM.' }
$dirty = [bool](& git -C $repositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0) { throw 'Não foi possível verificar o estado do Git.' }
$rootRef = "viverappweb-source:$commit"
$bom = [ordered]@{
    bomFormat = 'CycloneDX'
    specVersion = '1.6'
    serialNumber = "urn:uuid:$([guid]::NewGuid())"
    version = 1
    metadata = [ordered]@{
        timestamp = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        component = @{ type = 'application'; 'bom-ref' = $rootRef; name = 'ViverAppWeb'; version = $commit.Substring(0, 12) }
        properties = @(
            @{ name = 'viverapp.git.commit'; value = $commit },
            @{ name = 'viverapp.git.dirty'; value = $dirty.ToString().ToLowerInvariant() },
            @{ name = 'viverapp.nuget.lockfiles'; value = $lockFiles.Count.ToString() }
        )
    }
    components = @($components.Values | Sort-Object name, version)
    dependencies = @(@{ ref = $rootRef; dependsOn = @($components.Keys | Sort-Object) }) +
        @($edges.Keys | Sort-Object | ForEach-Object { @{ ref = $_; dependsOn = @($edges[$_] | Sort-Object) } })
}
$outputDirectory = Join-Path $repositoryRoot 'artifacts'
[void](New-Item -ItemType Directory -Path $outputDirectory -Force)
$outputPath = Join-Path $outputDirectory 'sbom.cdx.json'
$json = $bom | ConvertTo-Json -Depth 20
[IO.File]::WriteAllText($outputPath, $json + "`n", [Text.UTF8Encoding]::new($false))
$digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($outputPath))).ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $outputDirectory 'sbom.cdx.json.sha256'), "$digest  sbom.cdx.json`n", [Text.UTF8Encoding]::new($false))
Write-Host "SBOM CycloneDX 1.6: $($components.Count) pacotes, $($lockFiles.Count) projetos; SHA-256 $digest."
