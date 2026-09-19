-- Rollback documentado. Não executar se houver contas de Psicólogo ou atendimentos sem cobrança.

UPDATE `application_settings`
SET `setting_key` = 'doctor.patient_scheduling_enabled',
    `description` = 'Permite ao Médico agendar atendimentos para seus pacientes.',
    `updated_at_utc` = UTC_TIMESTAMP(6)
WHERE `setting_key` = 'professional.patient_scheduling_enabled';

UPDATE `application_settings`
SET `setting_key` = 'manager.doctor_schedules_enabled',
    `description` = 'Permite ao Gestor administrar a grade semanal dos médicos.',
    `updated_at_utc` = UTC_TIMESTAMP(6)
WHERE `setting_key` = 'manager.professional_schedules_enabled';

DELETE FROM `specialties` WHERE `normalized_name` = 'PSICOLOGIA';

ALTER TABLE `account_ui_preferences`
    DROP CHECK `ck_account_ui_preferences_calendar_view`,
    DROP COLUMN `calendar_view_mode`;

ALTER TABLE `payments`
    DROP FOREIGN KEY `fk_payments_chargeable_appointment`,
    DROP CHECK `ck_payments_chargeable_appointment`,
    DROP INDEX `ix_payments_appointment_chargeable`,
    DROP COLUMN `appointment_requires_payment`;

ALTER TABLE `medical_report_versions`
    CHANGE COLUMN `author_professional_account_id` `author_doctor_account_id` bigint unsigned NOT NULL;

ALTER TABLE `medical_reports`
    CHANGE COLUMN `author_professional_account_id` `author_doctor_account_id` bigint unsigned NOT NULL;

ALTER TABLE `appointment_types`
    DROP CHECK `ck_appointment_types_requires_payment`,
    DROP COLUMN `requires_payment`;

ALTER TABLE `appointments`
    DROP CHECK `ck_appointments_requires_payment`,
    DROP INDEX `ux_appointments_id_requires_payment`,
    DROP COLUMN `requires_payment`,
    CHANGE COLUMN `professional_account_id` `doctor_account_id` bigint unsigned NOT NULL;

ALTER TABLE `professional_notifications`
    CHANGE COLUMN `professional_account_id` `doctor_account_id` bigint unsigned NOT NULL;
ALTER TABLE `professional_patient_links`
    CHANGE COLUMN `professional_account_id` `doctor_account_id` bigint unsigned NOT NULL;
ALTER TABLE `professional_availability_exceptions`
    CHANGE COLUMN `professional_account_id` `doctor_account_id` bigint unsigned NOT NULL;
ALTER TABLE `professional_preferences`
    CHANGE COLUMN `professional_account_id` `doctor_account_id` bigint unsigned NOT NULL;
ALTER TABLE `professional_services`
    CHANGE COLUMN `professional_account_id` `doctor_account_id` bigint unsigned NOT NULL;
ALTER TABLE `professional_weekly_hours`
    CHANGE COLUMN `professional_account_id` `doctor_account_id` bigint unsigned NOT NULL;
ALTER TABLE `professional_specialties`
    CHANGE COLUMN `professional_account_id` `doctor_account_id` bigint unsigned NOT NULL;

ALTER TABLE `professional_profiles`
    DROP CHECK `ck_professional_profiles_license_type`,
    DROP INDEX `ux_professional_profiles_license`,
    DROP COLUMN `license_type_code`,
    ADD UNIQUE KEY `ux_doctor_profiles_license` (`license_state_code`, `license_number`);

RENAME TABLE
    `professional_profiles` TO `doctor_profiles`,
    `professional_specialties` TO `doctor_specialties`,
    `professional_weekly_hours` TO `doctor_weekly_hours`,
    `professional_services` TO `doctor_services`,
    `professional_preferences` TO `doctor_preferences`,
    `professional_availability_exceptions` TO `doctor_availability_exceptions`,
    `professional_patient_links` TO `doctor_patient_links`,
    `professional_notifications` TO `doctor_notifications`;

ALTER TABLE `roles` DROP CHECK `ck_roles_code`;
DELETE FROM `roles` WHERE `code` = 'psychologist';
ALTER TABLE `roles`
    ADD CONSTRAINT `ck_roles_code`
        CHECK (`code` IN ('administrator', 'doctor', 'manager', 'patient'));
