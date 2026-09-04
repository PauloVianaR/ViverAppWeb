-- Jornadas clínicas de médico e gestor — MySQL 8.0.41.

ALTER TABLE `appointments`
    ADD COLUMN `completed_by_account_id` bigint unsigned NULL AFTER `rescheduled_from_appointment_id`,
    ADD COLUMN `completed_at_utc` datetime(6) NULL AFTER `completed_by_account_id`,
    ADD COLUMN `no_show_recorded_by_account_id` bigint unsigned NULL AFTER `completed_at_utc`,
    ADD COLUMN `no_show_recorded_at_utc` datetime(6) NULL AFTER `no_show_recorded_by_account_id`,
    ADD KEY `ix_appointments_doctor_status_end` (`doctor_account_id`, `status_code`, `ends_at_utc`),
    ADD KEY `ix_appointments_patient_doctor_status` (`patient_account_id`, `doctor_account_id`, `status_code`),
    ADD CONSTRAINT `fk_appointments_completer`
        FOREIGN KEY (`completed_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    ADD CONSTRAINT `fk_appointments_no_show_actor`
        FOREIGN KEY (`no_show_recorded_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    ADD CONSTRAINT `ck_appointments_completion`
        CHECK (
            (`status_code` = 'completed' AND `completed_by_account_id` IS NOT NULL AND `completed_at_utc` IS NOT NULL)
            OR
            (`status_code` <> 'completed' AND `completed_by_account_id` IS NULL AND `completed_at_utc` IS NULL)
        ),
    ADD CONSTRAINT `ck_appointments_no_show_record`
        CHECK (
            (`status_code` = 'no_show' AND `no_show_recorded_by_account_id` IS NOT NULL AND `no_show_recorded_at_utc` IS NOT NULL)
            OR
            (`status_code` <> 'no_show' AND `no_show_recorded_by_account_id` IS NULL AND `no_show_recorded_at_utc` IS NULL)
        );

CREATE TABLE `medical_reports` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `appointment_id` bigint unsigned NOT NULL,
    `author_doctor_account_id` bigint unsigned NOT NULL,
    `status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'draft',
    `clinical_summary` text NOT NULL,
    `recommendations` text NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    `published_at_utc` datetime(6) NULL,
    `row_version` bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_medical_reports_appointment` (`appointment_id`),
    KEY `ix_medical_reports_author_updated` (`author_doctor_account_id`, `updated_at_utc`),
    KEY `ix_medical_reports_status_published` (`status_code`, `published_at_utc`),
    CONSTRAINT `fk_medical_reports_appointment`
        FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_reports_author`
        FOREIGN KEY (`author_doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_medical_reports_status` CHECK (`status_code` IN ('draft', 'published')),
    CONSTRAINT `ck_medical_reports_summary`
        CHECK (CHAR_LENGTH(TRIM(`clinical_summary`)) BETWEEN 1 AND 12000),
    CONSTRAINT `ck_medical_reports_recommendations`
        CHECK (`recommendations` IS NULL OR CHAR_LENGTH(`recommendations`) <= 8000),
    CONSTRAINT `ck_medical_reports_publication`
        CHECK (
            (`status_code` = 'published' AND `published_at_utc` IS NOT NULL)
            OR
            (`status_code` = 'draft' AND `published_at_utc` IS NULL)
        ),
    CONSTRAINT `ck_medical_reports_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;
