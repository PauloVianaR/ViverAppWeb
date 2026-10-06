[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleaseDirectory,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($ReleaseDirectory)
& (Join-Path $PSScriptRoot 'verify-windows-release.ps1') -ReleaseDirectory $package
if ($LASTEXITCODE -ne 0) { throw 'Pacote inválido.' }
$manifest = Get-Content -LiteralPath (Join-Path $package 'manifest.json') -Raw | ConvertFrom-Json
$plan = Get-Content -LiteralPath (Join-Path $package 'deployment-plan.json') -Raw | ConvertFrom-Json
if ($plan.schema -ne 1 -or $plan.commit -cne $manifest.commit -or
    $null -eq $plan.expectedPendingMigrations -or $null -eq $plan.binaryRollbackCompatible) {
    throw 'Plano de implantação inválido.'
}
$commit = [string]$manifest.commit
$root = [IO.Path]::GetDirectoryName($package)
if ($package -cne [IO.Path]::GetFullPath((Join-Path $root $commit))) {
    throw 'Diretório do pacote deve ter o nome do commit da release.'
}
& (Join-Path $PSScriptRoot 'Download-ProductionRelease.ps1') -Tag "viverapp-$commit" -DestinationRoot $root | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Release não verificável no GitHub.' }
$expected = @($plan.expectedPendingMigrations)
if (@($expected | Where-Object { $_ -cnotmatch '^\d{4}$' }).Count -gt 0 -or
    (@($expected | Sort-Object -Unique) -join ',') -cne ($expected -join ',')) {
    throw 'Lista de migrations inválida.'
}

$unpacked = Join-Path $package ("database-" + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $unpacked)
Expand-Archive -LiteralPath (Join-Path $package 'viverapp-database.zip') -DestinationPath $unpacked
$migrationDirectory = Join-Path $unpacked 'migrations'
& (Join-Path $PSScriptRoot 'Invoke-ProductionMigrations.ps1') -MigrationDirectory $migrationDirectory -ExpectedPendingIds $expected
if ($LASTEXITCODE -ne 0) { throw 'Pré-voo das migrations falhou.' }
if (-not $Apply) {
    Write-Output "Pré-voo aprovado para $commit. Use -Apply em sessão administrativa para implantar."
    return
}

$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'A implantação requer PowerShell como Administrador.'
}
foreach ($serviceName in @('MySQL80', 'W3SVC', 'cloudflared')) {
    if ((Get-Service -Name $serviceName -ErrorAction Stop).Status -ne 'Running') {
        throw "Serviço indisponível: $serviceName"
    }
}

$backupScript = 'C:\Viver\ViverAppWeb\.local\backup-current-production.ps1'
$backupStatus = 'C:\Viver\ViverAppWeb\.local\backup-current-production.status.json'
if (-not (Test-Path -LiteralPath $backupScript -PathType Leaf)) { throw 'Script local de backup ausente.' }
$backupStart = [DateTime]::UtcNow
& $backupScript
if ($LASTEXITCODE -ne 0) { throw 'Backup pré-deploy falhou.' }
$backup = Get-Content -LiteralPath $backupStatus -Raw | ConvertFrom-Json
if ($backup.Database -cne 'viverappweb' -or $backup.MySqlVersion -cne '8.0.41' -or
    $backup.DecryptionVerified -ne $true -or
    ([DateTime]$backup.CompletedAtUtc).ToUniversalTime() -lt $backupStart -or
    -not (Test-Path -LiteralPath $backup.EncryptedFile -PathType Leaf) -or
    (Get-FileHash -LiteralPath $backup.EncryptedFile -Algorithm SHA256).Hash.ToLowerInvariant() -cne $backup.EncryptedSha256) {
    throw 'Backup novo ausente ou não conferido.'
}

$releaseRoot = 'C:\Viver\deployment\releases'
$release = Join-Path $releaseRoot $commit
if (Test-Path -LiteralPath $release) { throw 'Diretório de release já existe; conferir antes de repetir.' }
[void](New-Item -ItemType Directory -Path $release)
foreach ($app in @('api', 'web')) {
    $directory = Join-Path $release $app
    Expand-Archive -LiteralPath (Join-Path $package "viverapp-$app.zip") -DestinationPath $directory
    $dll = if ($app -eq 'api') { 'ViverApp.Api.dll' } else { 'ViverApp.Web.dll' }
    if (-not (Test-Path -LiteralPath (Join-Path $directory $dll) -PathType Leaf)) {
        throw "Publicação incompleta: $app"
    }
    $poolName = if ($app -eq 'api') { 'ViverApi' } else { 'ViverWeb' }
    & icacls.exe $directory /grant "IIS APPPOOL\${poolName}:(OI)(CI)(RX)" /T /Q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Falha na ACL de $app" }
}

& (Join-Path $PSScriptRoot 'Invoke-ProductionMigrations.ps1') -MigrationDirectory $migrationDirectory -ExpectedPendingIds $expected -Apply
if ($LASTEXITCODE -ne 0) { throw 'Aplicação das migrations falhou; nenhum binário foi trocado.' }

Add-Type -Path 'C:\Windows\System32\inetsrv\Microsoft.Web.Administration.dll'
$manager = [Microsoft.Web.Administration.ServerManager]::new()
try {
    $apiSite = $manager.Sites['ViverApi']
    $webSite = $manager.Sites['ViverWeb']
    if ($null -eq $apiSite -or $null -eq $webSite) { throw 'Sites IIS ausentes.' }
    $api = $apiSite.Applications['/'].VirtualDirectories['/']
    $web = $webSite.Applications['/'].VirtualDirectories['/']
    $oldApi = [string]$api.PhysicalPath
    $oldWeb = [string]$web.PhysicalPath
    foreach ($oldPath in @($oldApi, $oldWeb)) {
        $full = [IO.Path]::GetFullPath($oldPath)
        if (-not $full.StartsWith($releaseRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Site ativo aponta para fora da árvore de releases.'
        }
    }
    $api.PhysicalPath = Join-Path $release 'api'
    $web.PhysicalPath = Join-Path $release 'web'
    $manager.CommitChanges()
    $manager.ApplicationPools['ViverApi'].Recycle()
    $manager.ApplicationPools['ViverWeb'].Recycle()
} finally { $manager.Dispose() }

function Test-Health([string]$url) {
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15 `
                -Headers @{ 'Cache-Control' = 'no-cache' }
            if ($response.StatusCode -eq 200) { return $true }
        } catch { }
        if ($attempt -lt 3) { Start-Sleep -Seconds 3 }
    }
    return $false
}

$healthUrls = @('https://api.viveralmenara.com/health/ready',
    'https://viveralmenara.com/health/ready', 'https://viveralmenara.com/')
$failed = @($healthUrls | Where-Object { -not (Test-Health $_) })
if ($failed.Count -gt 0) {
    if ($expected.Count -eq 0 -or $plan.binaryRollbackCompatible -eq $true) {
        $rollback = [Microsoft.Web.Administration.ServerManager]::new()
        try {
            $rollback.Sites['ViverApi'].Applications['/'].VirtualDirectories['/'].PhysicalPath = $oldApi
            $rollback.Sites['ViverWeb'].Applications['/'].VirtualDirectories['/'].PhysicalPath = $oldWeb
            $rollback.CommitChanges()
            $rollback.ApplicationPools['ViverApi'].Recycle()
            $rollback.ApplicationPools['ViverWeb'].Recycle()
        } finally { $rollback.Dispose() }
        throw "Health falhou ($($failed -join ',')). Binários anteriores restaurados; schema e backup preservados."
    }
    throw "Health falhou ($($failed -join ',')). Migrations não declaradas compatíveis com rollback; parar e avaliar incidente."
}

[ordered]@{
    Commit = $commit
    CompletedAtUtc = [DateTime]::UtcNow.ToString('o')
    PreviousApi = $oldApi
    PreviousWeb = $oldWeb
    ActiveApi = (Join-Path $release 'api')
    ActiveWeb = (Join-Path $release 'web')
    Migrations = $expected
    Backup = $backup.EncryptedFile
    Health = 'ok'
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $package 'deployment-result.json') -Encoding utf8
Write-Output "Implantação concluída: $commit; API/Web e health aprovados."
