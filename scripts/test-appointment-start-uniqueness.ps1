[CmdletBinding()]
param(
    [string]$MySql = 'C:\Program Files\MySQL\MySQL Server 8.0\bin\mysql.exe',
    [string]$LoginPath = 'viver-production-setup'
)

$ErrorActionPreference = 'Stop'
# Somente o schema persistente de homologacao. Nunca executar fixtures na producao.
$common = @("--login-path=$LoginPath", '--database=viverappweb_homolog', '--batch', '--skip-column-names')
$identity = & $MySql @common --execute='SELECT VERSION(),DATABASE()'
if ($LASTEXITCODE -ne 0 -or ($identity -join '') -notmatch '^8\.0\.41\s+viverappweb_homolog$') {
    throw 'O teste exige MySQL 8.0.41 / viverappweb_homolog.'
}

function Insert-Appointment([int]$number, [int]$patient, [int]$professional, [string]$status) {
    $canceled = if ($status -eq 'canceled') { 'UTC_TIMESTAMP(6)' } else { 'NULL' }
    $completedActor = if ($status -eq 'completed') { '@qa_professional_1' } else { 'NULL' }
    $completedAt = if ($status -eq 'completed') { 'UTC_TIMESTAMP(6)' } else { 'NULL' }
    $noShowActor = if ($status -eq 'no_show') { '@qa_professional_1' } else { 'NULL' }
    $noShowAt = if ($status -eq 'no_show') { 'UTC_TIMESTAMP(6)' } else { 'NULL' }
    return @"
INSERT INTO appointments
    (appointment_number,patient_account_id,professional_account_id,appointment_type_id,created_by_account_id,
     status_code,modality_code,starts_at_utc,ends_at_utc,price_amount,requires_payment,payment_location_code,
     canceled_at_utc,completed_by_account_id,completed_at_utc,no_show_recorded_by_account_id,no_show_recorded_at_utc,
     created_at_utc,updated_at_utc)
VALUES ($number,@qa_patient_$patient,@qa_professional_$professional,@qa_type,@qa_professional_1,'$status','in_person','2099-11-17 12:00:00','2099-11-17 12:20:00',0,0,'clinic',
    $canceled,$completedActor,$completedAt,$noShowActor,$noShowAt,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
"@
}

function Assert-Sql([string]$sql, [bool]$duplicateExpected, [string]$caseName) {
    $fixtures = @'
INSERT INTO accounts (role_code,status_code,full_name,normalized_email,portal_access_enabled,security_stamp,created_at_utc,updated_at_utc)
VALUES ('patient','active','QA INDEX PATIENT 1',CONCAT(UUID(),'@example.test'),0,RANDOM_BYTES(32),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
SET @qa_patient_1=LAST_INSERT_ID();
INSERT INTO accounts (role_code,status_code,full_name,normalized_email,portal_access_enabled,security_stamp,created_at_utc,updated_at_utc)
VALUES ('patient','active','QA INDEX PATIENT 2',CONCAT(UUID(),'@example.test'),0,RANDOM_BYTES(32),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
SET @qa_patient_2=LAST_INSERT_ID();
INSERT INTO accounts (role_code,status_code,full_name,normalized_email,portal_access_enabled,security_stamp,created_at_utc,updated_at_utc)
VALUES ('doctor','active','QA INDEX PROFESSIONAL 1',CONCAT(UUID(),'@example.test'),0,RANDOM_BYTES(32),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
SET @qa_professional_1=LAST_INSERT_ID();
INSERT INTO accounts (role_code,status_code,full_name,normalized_email,portal_access_enabled,security_stamp,created_at_utc,updated_at_utc)
VALUES ('doctor','active','QA INDEX PROFESSIONAL 2',CONCAT(UUID(),'@example.test'),0,RANDOM_BYTES(32),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
SET @qa_professional_2=LAST_INSERT_ID();
INSERT INTO professional_profiles (account_id,license_type_code,license_state_code,license_number,created_at_utc,updated_at_utc)
VALUES (@qa_professional_1,'CRM','SP',LEFT(REPLACE(UUID(),'-',''),20),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6)),
       (@qa_professional_2,'CRM','SP',LEFT(REPLACE(UUID(),'-',''),20),UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
INSERT INTO appointment_types (name,modality_code,duration_minutes,price_amount,requires_payment,is_active,created_at_utc,updated_at_utc)
VALUES ('QA INDEX SERVICE','in_person',20,0,0,1,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6));
SET @qa_type=LAST_INSERT_ID();
'@
    $output = & $MySql @common --execute="START TRANSACTION; $fixtures $sql ROLLBACK;" 2>&1
    $exitCode = $LASTEXITCODE
    if ($duplicateExpected) {
        if ($exitCode -eq 0 -or ($output -join ' ') -notmatch 'ERROR 1062') {
            throw "A reserva ativa duplicada nao foi recusada: $caseName"
        }
    } elseif ($exitCode -ne 0) {
        throw "Falha em $caseName : $($output -join ' ')"
    }
    Write-Output "PASS $caseName"
}

# Cada execucao usa a mesma conexao/transacao; ate uma falha desfaz os INSERTs.
foreach ($terminal in @('canceled','completed','no_show','rescheduled')) {
    $first = Insert-Appointment 900000001 1 1 $terminal
    $second = Insert-Appointment 900000002 1 1 'pending'
    Assert-Sql "$first $second" $false "reuse-$terminal"
}
foreach ($active in @('pending','confirmed','arrived','in_progress')) {
    $first = Insert-Appointment 900000001 1 1 $active
    Assert-Sql ($first + (Insert-Appointment 900000002 2 1 'confirmed')) $true "professional-$active"
    Assert-Sql ($first + (Insert-Appointment 900000002 1 2 'confirmed')) $true "patient-$active"
}
$canceled = Insert-Appointment 900000001 1 1 'canceled'
$confirmed = Insert-Appointment 900000002 1 1 'confirmed'
$replacement = Insert-Appointment 900000003 1 1 'pending'
Assert-Sql "$canceled $confirmed UPDATE appointments SET status_code='canceled', canceled_at_utc=UTC_TIMESTAMP(6) WHERE appointment_number=900000002; $replacement" $false 'cancel-and-rebook-again'
