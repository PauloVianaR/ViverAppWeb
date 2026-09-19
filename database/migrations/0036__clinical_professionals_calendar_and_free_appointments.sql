-- Fase 19. Profissionais clínicos, preferência de calendário e atendimentos sem cobrança — MySQL 8.0.41.

ALTER TABLE `roles`
    DROP CHECK `ck_roles_code`;

INSERT INTO `roles` (`code`, `display_name`, `is_privileged`)
VALUES ('psychologist', 'Psicólogo', 0)
ON DUPLICATE KEY UPDATE
    `display_name` = VALUES(`display_name`),
    `is_privileged` = VALUES(`is_privileged`);

ALTER TABLE `roles`
    ADD CONSTRAINT `ck_roles_code`
        CHECK (`code` IN ('administrator', 'doctor', 'manager', 'patient', 'psychologist'));

RENAME TABLE
    `doctor_profiles` TO `professional_profiles`,
    `doctor_specialties` TO `professional_specialties`,
    `doctor_weekly_hours` TO `professional_weekly_hours`,
    `doctor_services` TO `professional_services`,
    `doctor_preferences` TO `professional_preferences`,
    `doctor_availability_exceptions` TO `professional_availability_exceptions`,
    `doctor_patient_links` TO `professional_patient_links`,
    `doctor_notifications` TO `professional_notifications`;

ALTER TABLE `professional_profiles`
    DROP INDEX `ux_doctor_profiles_license`,
    ADD COLUMN `license_type_code` varchar(3) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `account_id`;

UPDATE `professional_profiles`
SET `license_type_code` = 'CRM'
WHERE `license_type_code` IS NULL;

ALTER TABLE `professional_profiles`
    MODIFY COLUMN `license_type_code` varchar(3) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    ADD UNIQUE KEY `ux_professional_profiles_license` (`license_type_code`, `license_state_code`, `license_number`),
    ADD CONSTRAINT `ck_professional_profiles_license_type`
        CHECK (`license_type_code` IN ('CRM', 'CRP'));

ALTER TABLE `professional_specialties`
    DROP FOREIGN KEY `fk_doctor_specialties_doctor`;
ALTER TABLE `professional_specialties`
    CHANGE COLUMN `doctor_account_id` `professional_account_id` bigint unsigned NOT NULL,
    ALGORITHM=COPY;
ALTER TABLE `professional_specialties`
    ADD CONSTRAINT `fk_professional_specialties_professional`
        FOREIGN KEY (`professional_account_id`) REFERENCES `professional_profiles` (`account_id`) ON DELETE CASCADE;

ALTER TABLE `professional_weekly_hours`
    DROP FOREIGN KEY `fk_doctor_weekly_hours_doctor`;
ALTER TABLE `professional_weekly_hours`
    CHANGE COLUMN `doctor_account_id` `professional_account_id` bigint unsigned NOT NULL,
    ALGORITHM=COPY;
ALTER TABLE `professional_weekly_hours`
    ADD CONSTRAINT `fk_professional_weekly_hours_professional`
        FOREIGN KEY (`professional_account_id`) REFERENCES `professional_profiles` (`account_id`) ON DELETE CASCADE;

ALTER TABLE `professional_services`
    DROP FOREIGN KEY `fk_doctor_services_doctor`;
ALTER TABLE `professional_services`
    CHANGE COLUMN `doctor_account_id` `professional_account_id` bigint unsigned NOT NULL,
    ALGORITHM=COPY;
ALTER TABLE `professional_services`
    ADD CONSTRAINT `fk_professional_services_professional`
        FOREIGN KEY (`professional_account_id`) REFERENCES `professional_profiles` (`account_id`) ON DELETE CASCADE;

ALTER TABLE `professional_preferences`
    DROP FOREIGN KEY `fk_doctor_preferences_doctor`;
ALTER TABLE `professional_preferences`
    CHANGE COLUMN `doctor_account_id` `professional_account_id` bigint unsigned NOT NULL,
    ALGORITHM=COPY;
ALTER TABLE `professional_preferences`
    ADD CONSTRAINT `fk_professional_preferences_professional`
        FOREIGN KEY (`professional_account_id`) REFERENCES `professional_profiles` (`account_id`) ON DELETE CASCADE;

ALTER TABLE `professional_availability_exceptions`
    DROP FOREIGN KEY `fk_doctor_availability_exception_doctor`;
ALTER TABLE `professional_availability_exceptions`
    CHANGE COLUMN `doctor_account_id` `professional_account_id` bigint unsigned NOT NULL,
    ALGORITHM=COPY;
ALTER TABLE `professional_availability_exceptions`
    ADD CONSTRAINT `fk_professional_availability_exception_professional`
        FOREIGN KEY (`professional_account_id`) REFERENCES `professional_profiles` (`account_id`) ON DELETE CASCADE;

ALTER TABLE `professional_patient_links`
    DROP FOREIGN KEY `fk_doctor_patient_links_doctor`;
ALTER TABLE `professional_patient_links`
    CHANGE COLUMN `doctor_account_id` `professional_account_id` bigint unsigned NOT NULL,
    ALGORITHM=COPY;
ALTER TABLE `professional_patient_links`
    ADD CONSTRAINT `fk_professional_patient_links_professional`
        FOREIGN KEY (`professional_account_id`) REFERENCES `professional_profiles` (`account_id`) ON DELETE CASCADE;

ALTER TABLE `professional_notifications`
    DROP FOREIGN KEY `fk_doctor_notifications_doctor`;
ALTER TABLE `professional_notifications`
    CHANGE COLUMN `doctor_account_id` `professional_account_id` bigint unsigned NOT NULL,
    ALGORITHM=COPY;
ALTER TABLE `professional_notifications`
    ADD CONSTRAINT `fk_professional_notifications_professional`
        FOREIGN KEY (`professional_account_id`) REFERENCES `professional_profiles` (`account_id`) ON DELETE CASCADE;

ALTER TABLE `appointments`
    DROP FOREIGN KEY `fk_appointments_doctor`;
ALTER TABLE `appointments`
    CHANGE COLUMN `doctor_account_id` `professional_account_id` bigint unsigned NOT NULL,
    ADD COLUMN `requires_payment` tinyint(1) NOT NULL DEFAULT 1 AFTER `price_amount`,
    ADD UNIQUE KEY `ux_appointments_id_requires_payment` (`id`, `requires_payment`),
    ADD CONSTRAINT `ck_appointments_requires_payment`
        CHECK (`requires_payment` IN (0, 1) AND (`requires_payment` = 1 OR `price_amount` = 0)),
    ALGORITHM=COPY;
ALTER TABLE `appointments`
    ADD CONSTRAINT `fk_appointments_professional`
        FOREIGN KEY (`professional_account_id`) REFERENCES `professional_profiles` (`account_id`) ON DELETE RESTRICT;

ALTER TABLE `appointment_types`
    ADD COLUMN `requires_payment` tinyint(1) NOT NULL DEFAULT 1 AFTER `price_amount`,
    ADD CONSTRAINT `ck_appointment_types_requires_payment`
        CHECK (`requires_payment` IN (0, 1) AND (`requires_payment` = 1 OR `price_amount` = 0));

ALTER TABLE `medical_reports`
    DROP FOREIGN KEY `fk_medical_reports_author`;
ALTER TABLE `medical_reports`
    CHANGE COLUMN `author_doctor_account_id` `author_professional_account_id` bigint unsigned NOT NULL,
    ALGORITHM=COPY;
ALTER TABLE `medical_reports`
    ADD CONSTRAINT `fk_medical_reports_author_professional`
        FOREIGN KEY (`author_professional_account_id`) REFERENCES `professional_profiles` (`account_id`) ON DELETE RESTRICT;

ALTER TABLE `medical_report_versions`
    DROP FOREIGN KEY `fk_medical_report_versions_author`;
ALTER TABLE `medical_report_versions`
    CHANGE COLUMN `author_doctor_account_id` `author_professional_account_id` bigint unsigned NOT NULL,
    ALGORITHM=COPY;
ALTER TABLE `medical_report_versions`
    ADD CONSTRAINT `fk_medical_report_versions_author_professional`
        FOREIGN KEY (`author_professional_account_id`) REFERENCES `professional_profiles` (`account_id`) ON DELETE RESTRICT;

ALTER TABLE `payments`
    ADD COLUMN `appointment_requires_payment` tinyint(1) NOT NULL DEFAULT 1 AFTER `appointment_id`,
    ADD KEY `ix_payments_appointment_chargeable` (`appointment_id`, `appointment_requires_payment`),
    ADD CONSTRAINT `fk_payments_chargeable_appointment`
        FOREIGN KEY (`appointment_id`, `appointment_requires_payment`)
        REFERENCES `appointments` (`id`, `requires_payment`) ON DELETE RESTRICT,
    ADD CONSTRAINT `ck_payments_chargeable_appointment`
        CHECK (`appointment_requires_payment` = 1);

ALTER TABLE `account_ui_preferences`
    ADD COLUMN `calendar_view_mode` varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'month' AFTER `appointment_view_mode`,
    ADD CONSTRAINT `ck_account_ui_preferences_calendar_view`
        CHECK (`calendar_view_mode` IN ('day', 'week', 'month', 'year'));

INSERT INTO `specialties`
    (`name`, `normalized_name`, `is_active`, `created_at_utc`, `updated_at_utc`, `row_version`)
VALUES
    ('Psicologia', 'PSICOLOGIA', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 1)
ON DUPLICATE KEY UPDATE
    `name` = VALUES(`name`),
    `is_active` = 1,
    `updated_at_utc` = UTC_TIMESTAMP(6),
    `row_version` = `row_version` + 1;

UPDATE `application_settings`
SET `setting_key` = 'professional.patient_scheduling_enabled',
    `description` = 'Permite ao Médico ou Psicólogo agendar atendimentos para seus pacientes.',
    `updated_at_utc` = UTC_TIMESTAMP(6)
WHERE `setting_key` = 'doctor.patient_scheduling_enabled';

UPDATE `application_settings`
SET `setting_key` = 'manager.professional_schedules_enabled',
    `description` = 'Permite ao Gestor administrar a grade semanal dos profissionais clínicos.',
    `updated_at_utc` = UTC_TIMESTAMP(6)
WHERE `setting_key` = 'manager.doctor_schedules_enabled';
