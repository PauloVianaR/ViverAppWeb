[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MigrationDirectory,
    [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$ExpectedPendingIds,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$mysql = 'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe'
$ca = 'C:\Viver\deployment\state\api\mysql-ca.pem'
$protectedFile = 'C:\Viver\deployment\state\db-admin\credentials.dpapi'
foreach ($path in @($mysql, $ca, $protectedFile)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Dependência local ausente: $path" }
}
$files = @(Get-ChildItem -LiteralPath $MigrationDirectory -Filter '*.sql' -File | Sort-Object Name)
if ($files.Count -eq 0) { throw 'Nenhuma migration SQL no pacote.' }
$migrations = @()
foreach ($file in $files) {
    if ($file.Name -cnotmatch '^(\d{4})__([a-z0-9_]+)\.sql$') { throw "Nome inválido: $($file.Name)" }
    $id = $Matches[1]
    $description = $Matches[2]
    $sql = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8).Replace("`r`n", "`n").Replace("`r", "`n")
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = ([BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($sql)))).Replace('-', '').ToLowerInvariant()
    } finally { $hasher.Dispose() }
    $migrations += [pscustomobject]@{ Id = $id; Description = $description; File = $file.FullName; Sha256 = $hash }
}
for ($i = 0; $i -lt $migrations.Count; $i++) {
    if ([int]$migrations[$i].Id -ne ($i + 1)) { throw 'Sequência de migrations incompleta ou duplicada.' }
}

$bytes = [Security.Cryptography.ProtectedData]::Unprotect(
    [IO.File]::ReadAllBytes($protectedFile), $null,
    [Security.Cryptography.DataProtectionScope]::CurrentUser)
try { $credential = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json }
finally { [Array]::Clear($bytes, 0, $bytes.Length) }
if ([string]::IsNullOrWhiteSpace($credential.MigratorPassword)) { throw 'Credencial de migrations ausente.' }
$env:MYSQL_PWD = [string]$credential.MigratorPassword
try {
    $common = @('--no-defaults', '--protocol=tcp', '--host=127.0.0.1', '--port=3306',
        '--user=viver_migrator', '--database=viverappweb', '--ssl-mode=VERIFY_CA',
        "--ssl-ca=$ca", '--batch', '--skip-column-names')
    $identity = @(& $mysql @common --execute='SELECT VERSION(),DATABASE()' 2>&1)
    if ($LASTEXITCODE -ne 0 -or ($identity -join ' ') -notmatch '^8\.0\.41\s+viverappweb$') {
        throw 'MySQL ou banco de produção inesperado.'
    }
    $historyExists = @(& $mysql @common --execute="SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='__schema_migrations'" 2>&1)
    if ($LASTEXITCODE -ne 0 -or ($historyExists -join '').Trim() -ne '1') {
        throw 'Histórico de migrations ausente.'
    }
    $history = @(& $mysql @common --execute='SELECT migration_id,sha256 FROM __schema_migrations ORDER BY migration_id' 2>&1)
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível ler o histórico de migrations.' }
    $applied = @{}
    foreach ($line in $history) {
        $parts = [string]$line -split "`t"
        if ($parts.Count -ne 2 -or $parts[0] -cnotmatch '^\d{4}$' -or
            $parts[1] -cnotmatch '^[a-f0-9]{64}$') { throw 'Histórico de migrations inválido.' }
        $applied[$parts[0]] = $parts[1]
    }
    foreach ($id in $applied.Keys) {
        $match = @($migrations | Where-Object Id -CEQ $id)
        if ($match.Count -ne 1 -or $match[0].Sha256 -cne $applied[$id]) {
            throw "Migration já aplicada diverge do pacote: $id"
        }
    }
    $pending = @($migrations | Where-Object { -not $applied.ContainsKey($_.Id) })
    $actual = @($pending | ForEach-Object Id)
    if (($actual -join ',') -cne (@($ExpectedPendingIds) -join ',')) {
        throw "Migrations pendentes diferem do plano. Banco: $($actual -join ','); plano: $(@($ExpectedPendingIds) -join ',')."
    }
    if (-not $Apply) {
        Write-Output "MySQL 8.0.41 e checksums aprovados; pendentes: $($actual -join ','). Nenhum SQL aplicado."
        return
    }
    foreach ($migration in $pending) {
        $source = $migration.File.Replace('\', '/')
        $output = @(& $mysql @common --execute="source $source" 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Migration $($migration.Id) falhou; pode ter havido DDL parcial. Revisão manual obrigatória antes de repetir."
        }
        $insert = "INSERT INTO __schema_migrations (migration_id,description,sha256,applied_at_utc) VALUES ('$($migration.Id)','$($migration.Description)','$($migration.Sha256)',UTC_TIMESTAMP(6))"
        $output = @(& $mysql @common --execute=$insert 2>&1)
        if ($LASTEXITCODE -ne 0) {
            throw "Migration $($migration.Id) executada sem registro no histórico. Revisão manual obrigatória."
        }
        Write-Output "Migration $($migration.Id) aplicada."
    }
    $total = @(& $mysql @common --execute='SELECT COUNT(*) FROM __schema_migrations' 2>&1)
    if ($LASTEXITCODE -ne 0 -or ($total -join '').Trim() -ne [string]$migrations.Count) {
        throw 'Contagem final de migrations divergente.'
    }
} finally {
    Remove-Item Env:MYSQL_PWD -ErrorAction SilentlyContinue
    $credential = $null
}
