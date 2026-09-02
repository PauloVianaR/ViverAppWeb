[CmdletBinding()]
param(
    [switch]$SkipDependencyAudit
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repositoryRoot 'ViverApp.slnx'

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Executable,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "A verificação falhou: $Executable $($Arguments -join ' ')"
    }
}

function Assert-NoSecretsInSettings {
    $sensitiveName = '(?i)(password|passwd|pwd|secret|token|api[_-]?key|connectionstring)'
    $settingsFiles = Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Filter 'appsettings*.json' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }

    foreach ($file in $settingsFiles) {
        $json = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $pending = [Collections.Generic.Stack[object]]::new()
        $pending.Push($json)

        while ($pending.Count -gt 0) {
            $current = $pending.Pop()
            foreach ($property in $current.PSObject.Properties) {
                if ($property.Value -is [Management.Automation.PSCustomObject]) {
                    $pending.Push($property.Value)
                    continue
                }

                if ($property.Name -match $sensitiveName -and
                    -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                    throw "Configuração sensível preenchida em $($file.FullName)."
                }
            }
        }
    }

    $trackedSensitiveFiles = & git -c "safe.directory=$repositoryRoot" -C $repositoryRoot ls-files -- '*.pfx' '*.p12' '*.key' '*.pem' '.env' '.env.*' 'secrets.json'
    if ($LASTEXITCODE -ne 0) {
        throw 'Não foi possível verificar os arquivos rastreados pelo Git.'
    }

    if ($trackedSensitiveFiles) {
        throw 'Há arquivo potencialmente secreto rastreado pelo Git.'
    }
}

Push-Location $repositoryRoot
try {
    Assert-NoSecretsInSettings
    Invoke-CheckedCommand dotnet @('build', $solution, '--configuration', 'Release', '--no-restore')
    Invoke-CheckedCommand dotnet @('test', $solution, '--configuration', 'Release', '--no-build', '--no-restore')
    Invoke-CheckedCommand dotnet @('format', $solution, '--verify-no-changes', '--no-restore')
    Invoke-CheckedCommand dotnet @('run', '--project', 'tools/ViverApp.Database', '--no-build', '--', 'status')
    Invoke-CheckedCommand dotnet @('run', '--project', 'tools/ViverApp.Database', '--no-build', '--', 'verify')

    if (-not $SkipDependencyAudit) {
        Invoke-CheckedCommand dotnet @('list', $solution, 'package', '--vulnerable', '--include-transitive')
    }

    Write-Host 'Baseline automatizada de segurança aprovada.'
}
finally {
    Pop-Location
}
