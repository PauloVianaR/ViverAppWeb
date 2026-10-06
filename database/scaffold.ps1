[CmdletBinding()]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\', '/')
$projectPath = Join-Path $repositoryRoot 'src\ViverApp.Api\ViverApp.Api.csproj'
$generatedRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'src\ViverApp.Api\Infrastructure\Persistence\Generated'))
$stagingRoot = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'src\ViverApp.Api\Infrastructure\Persistence\.ScaffoldStaging'))
$toolRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.local\tools'))
$dotnetEf = Join-Path $toolRoot $(if ($IsWindows) { 'dotnet-ef.exe' } else { 'dotnet-ef' })
$expectedPrefix = [IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'src\ViverApp.Api\Infrastructure\Persistence')) + [IO.Path]::DirectorySeparatorChar

foreach ($safePath in @($generatedRoot, $stagingRoot)) {
    if (-not $safePath.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Um diretório de scaffold foi resolvido fora de Infrastructure/Persistence.'
    }
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

$tables = @(
    'roles',
    'clinic',
    'accounts',
    'account_consents',
    'account_addresses',
    'account_ui_preferences',
    'external_logins',
    'auth_sessions',
    'account_challenges',
    'account_authenticators',
    'account_recovery_codes',
    'account_passkeys',
    'patient_profiles',
    'professional_profiles',
    'specialties',
    'professional_specialties',
    'appointment_types',
    'clinic_weekly_hours',
    'professional_weekly_hours',
    'professional_variable_hours',
    'professional_services',
    'professional_preferences',
    'manager_preferences',
    'professional_availability_exceptions',
    'professional_patient_links',
    'holidays',
    'professional_reviews',
    'appointments',
    'appointment_service_items',
    'appointment_number_sequence',
    'appointment_reschedule_history',
    'arrival_queue_sequences',
    'appointment_status_history',
    'professional_notifications',
    'medical_reports',
    'medical_report_versions',
    'electronic_health_records',
    'medical_record_drafts',
    'medical_record_entries',
    'medical_record_versions',
    'medical_record_documents',
    'clinical_access_events',
    'appointment_documents',
    'payments',
    'payment_webhook_receipts',
    'payment_events',
    'payment_reversals',
    'payment_reversal_events',
    'cash_movements',
    'cash_closures',
    'cash_reopenings',
    'premium_plans',
    'premium_memberships',
    'outbox_messages',
    'notification_preferences',
    'notification_suppressions',
    'scheduled_jobs',
    'audit_events',
    'application_settings',
    'idempotency_records'
    'patient_preferences'
    'appointment_reviews'
    'private_documents'
    'contact_change_requests'
    'teleconsultation_peers'
    'teleconsultation_guest_links'
    'administrator_notifications'
    'administrator_analytics_exports'
)

$arguments = @(
    'dbcontext', 'scaffold',
    'Name=ConnectionStrings:LocalConnection',
    'MySql.EntityFrameworkCore',
    '--project', $projectPath,
    '--startup-project', $projectPath,
    '--configuration', 'Release',
    '--context', 'ViverAppDbContext',
    '--context-dir', 'Infrastructure/Persistence/.ScaffoldStaging',
    '--output-dir', 'Infrastructure/Persistence/.ScaffoldStaging/Entities',
    '--namespace', 'ViverApp.Api.Infrastructure.Persistence.Generated.Entities',
    '--context-namespace', 'ViverApp.Api.Infrastructure.Persistence.Generated',
    '--no-onconfiguring',
    '--force'
)

if ($NoBuild) {
    $arguments += '--no-build'
}

foreach ($table in $tables) {
    $arguments += @('--table', $table)
}

Push-Location $repositoryRoot
try {
    if (-not (Test-Path -LiteralPath $dotnetEf -PathType Leaf)) {
        & dotnet tool install dotnet-ef --tool-path $toolRoot --version 10.0.9
        if ($LASTEXITCODE -ne 0) {
            throw "A instalação local do dotnet-ef falhou com exit code $LASTEXITCODE."
        }
    }

    & $dotnetEf @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "O scaffold DB-First falhou com exit code $LASTEXITCODE."
    }

    $stagedContext = Join-Path $stagingRoot 'ViverAppDbContext.cs'
    $stagedEntities = Join-Path $stagingRoot 'Entities'
    if (-not (Test-Path -LiteralPath $stagedContext -PathType Leaf) -or
        -not (Test-Path -LiteralPath $stagedEntities -PathType Container)) {
        throw 'O scaffold terminou sem gerar o contexto e as entidades esperadas.'
    }

    $entityCount = (Get-ChildItem -LiteralPath $stagedEntities -Filter '*.cs' -File).Count
    if ($entityCount -ne $tables.Count) {
        throw "O scaffold gerou $entityCount entidades; eram esperadas $($tables.Count)."
    }

    if (Test-Path -LiteralPath $generatedRoot) {
        Remove-Item -LiteralPath $generatedRoot -Recurse -Force
    }

    Move-Item -LiteralPath $stagingRoot -Destination $generatedRoot
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
