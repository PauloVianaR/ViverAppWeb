-- Fase 18. Prontuário eletrônico versionado e trilha de acesso — MySQL 8.0.41.

CREATE TABLE `electronic_health_records` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `patient_account_id` bigint unsigned NOT NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    `row_version` bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_electronic_health_records_patient` (`patient_account_id`),
    CONSTRAINT `fk_electronic_health_records_patient`
        FOREIGN KEY (`patient_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_electronic_health_records_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;

CREATE TABLE `medical_record_drafts` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `health_record_id` bigint unsigned NOT NULL,
    `appointment_id` bigint unsigned NOT NULL,
    `author_doctor_account_id` bigint unsigned NOT NULL,
    `chief_complaint` text NULL,
    `present_illness_history` text NULL,
    `personal_history` text NULL,
    `family_history` text NULL,
    `allergies` text NULL,
    `medications` text NULL,
    `relevant_habits` text NULL,
    `physical_examination` text NULL,
    `diagnostic_hypotheses` text NULL,
    `conduct_and_guidance` text NULL,
    `follow_up_plan` text NULL,
    `clinical_evolution` text NULL,
    `additional_notes` text NULL,
    `systolic_pressure_mmhg` smallint unsigned NULL,
    `diastolic_pressure_mmhg` smallint unsigned NULL,
    `heart_rate_bpm` smallint unsigned NULL,
    `temperature_celsius` decimal(4,1) NULL,
    `weight_kg` decimal(6,2) NULL,
    `height_cm` decimal(5,1) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    `expires_at_utc` datetime(6) NOT NULL,
    `row_version` bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_medical_record_drafts_appointment_author` (`appointment_id`, `author_doctor_account_id`),
    KEY `ix_medical_record_drafts_record_updated` (`health_record_id`, `updated_at_utc`),
    CONSTRAINT `fk_medical_record_drafts_record` FOREIGN KEY (`health_record_id`) REFERENCES `electronic_health_records` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_drafts_appointment` FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_drafts_author` FOREIGN KEY (`author_doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_medical_record_drafts_pressure` CHECK (
        (`systolic_pressure_mmhg` IS NULL OR `systolic_pressure_mmhg` BETWEEN 40 AND 300)
        AND (`diastolic_pressure_mmhg` IS NULL OR `diastolic_pressure_mmhg` BETWEEN 20 AND 200)
    ),
    CONSTRAINT `ck_medical_record_drafts_vitals` CHECK (
        (`heart_rate_bpm` IS NULL OR `heart_rate_bpm` BETWEEN 20 AND 300)
        AND (`temperature_celsius` IS NULL OR `temperature_celsius` BETWEEN 25 AND 45)
        AND (`weight_kg` IS NULL OR `weight_kg` BETWEEN 0.10 AND 500)
        AND (`height_cm` IS NULL OR `height_cm` BETWEEN 20 AND 260)
    ),
    CONSTRAINT `ck_medical_record_drafts_expiration` CHECK (`expires_at_utc` > `created_at_utc`),
    CONSTRAINT `ck_medical_record_drafts_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;

CREATE TABLE `medical_record_entries` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `health_record_id` bigint unsigned NOT NULL,
    `appointment_id` bigint unsigned NOT NULL,
    `author_doctor_account_id` bigint unsigned NOT NULL,
    `current_version_id` bigint unsigned NULL,
    `created_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_medical_record_entries_appointment` (`appointment_id`),
    KEY `ix_medical_record_entries_record_created` (`health_record_id`, `created_at_utc`),
    KEY `ix_medical_record_entries_author_created` (`author_doctor_account_id`, `created_at_utc`),
    CONSTRAINT `fk_medical_record_entries_record` FOREIGN KEY (`health_record_id`) REFERENCES `electronic_health_records` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_entries_appointment` FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_entries_author` FOREIGN KEY (`author_doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE `medical_record_versions` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `medical_record_entry_id` bigint unsigned NOT NULL,
    `version_number` int unsigned NOT NULL,
    `supersedes_version_id` bigint unsigned NULL,
    `author_doctor_account_id` bigint unsigned NOT NULL,
    `correction_reason` varchar(1000) NULL,
    `chief_complaint` text NULL,
    `present_illness_history` text NULL,
    `personal_history` text NULL,
    `family_history` text NULL,
    `allergies` text NULL,
    `medications` text NULL,
    `relevant_habits` text NULL,
    `physical_examination` text NULL,
    `diagnostic_hypotheses` text NULL,
    `conduct_and_guidance` text NULL,
    `follow_up_plan` text NULL,
    `clinical_evolution` text NULL,
    `additional_notes` text NULL,
    `systolic_pressure_mmhg` smallint unsigned NULL,
    `diastolic_pressure_mmhg` smallint unsigned NULL,
    `heart_rate_bpm` smallint unsigned NULL,
    `temperature_celsius` decimal(4,1) NULL,
    `weight_kg` decimal(6,2) NULL,
    `height_cm` decimal(5,1) NULL,
    `content_sha256` binary(32) NOT NULL,
    `finalized_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_medical_record_versions_number` (`medical_record_entry_id`, `version_number`),
    UNIQUE KEY `ux_medical_record_versions_superseded_once` (`supersedes_version_id`),
    KEY `ix_medical_record_versions_author_time` (`author_doctor_account_id`, `finalized_at_utc`),
    CONSTRAINT `fk_medical_record_versions_entry` FOREIGN KEY (`medical_record_entry_id`) REFERENCES `medical_record_entries` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_versions_supersedes` FOREIGN KEY (`supersedes_version_id`) REFERENCES `medical_record_versions` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_versions_author` FOREIGN KEY (`author_doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_medical_record_versions_number` CHECK (`version_number` > 0),
    CONSTRAINT `ck_medical_record_versions_reason` CHECK (
        (`version_number` = 1 AND `correction_reason` IS NULL)
        OR (`version_number` > 1 AND CHAR_LENGTH(TRIM(`correction_reason`)) BETWEEN 5 AND 1000)
    ),
    CONSTRAINT `ck_medical_record_versions_content` CHECK (
        COALESCE(CHAR_LENGTH(TRIM(`chief_complaint`)), 0)
        + COALESCE(CHAR_LENGTH(TRIM(`clinical_evolution`)), 0)
        + COALESCE(CHAR_LENGTH(TRIM(`conduct_and_guidance`)), 0) >= 20
    )
) ENGINE=InnoDB;

ALTER TABLE `medical_record_entries`
    ADD CONSTRAINT `fk_medical_record_entries_current_version`
        FOREIGN KEY (`current_version_id`) REFERENCES `medical_record_versions` (`id`) ON DELETE RESTRICT;

CREATE TABLE `medical_record_documents` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `health_record_id` bigint unsigned NOT NULL,
    `appointment_id` bigint unsigned NULL,
    `medical_record_version_id` bigint unsigned NULL,
    `private_document_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `uploaded_by_account_id` bigint unsigned NOT NULL,
    `category_code` varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'available',
    `created_at_utc` datetime(6) NOT NULL,
    `deleted_at_utc` datetime(6) NULL,
    `deleted_by_account_id` bigint unsigned NULL,
    `row_version` bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_medical_record_documents_private` (`private_document_id`),
    KEY `ix_medical_record_documents_record_time` (`health_record_id`, `created_at_utc`),
    KEY `ix_medical_record_documents_appointment` (`appointment_id`),
    CONSTRAINT `fk_medical_record_documents_record` FOREIGN KEY (`health_record_id`) REFERENCES `electronic_health_records` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_documents_appointment` FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_documents_version` FOREIGN KEY (`medical_record_version_id`) REFERENCES `medical_record_versions` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_documents_private` FOREIGN KEY (`private_document_id`) REFERENCES `private_documents` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_documents_uploader` FOREIGN KEY (`uploaded_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_medical_record_documents_deleted_by` FOREIGN KEY (`deleted_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_medical_record_documents_category` CHECK (`category_code` IN ('attachment','report','exam','administrative')),
    CONSTRAINT `ck_medical_record_documents_status` CHECK (`status_code` IN ('available','deleted')),
    CONSTRAINT `ck_medical_record_documents_deleted` CHECK (
        (`status_code` = 'available' AND `deleted_at_utc` IS NULL AND `deleted_by_account_id` IS NULL)
        OR (`status_code` = 'deleted' AND `deleted_at_utc` IS NOT NULL AND `deleted_by_account_id` IS NOT NULL)
    ),
    CONSTRAINT `ck_medical_record_documents_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;

CREATE TABLE `clinical_access_events` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `patient_account_id` bigint unsigned NOT NULL,
    `actor_account_id` bigint unsigned NOT NULL,
    `actor_role_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `scope_code` varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `outcome_code` varchar(12) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `purpose` varchar(500) NULL,
    `entity_type` varchar(40) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `entity_id` varchar(80) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `occurred_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    KEY `ix_clinical_access_events_patient_time` (`patient_account_id`, `occurred_at_utc`),
    KEY `ix_clinical_access_events_actor_time` (`actor_account_id`, `occurred_at_utc`),
    CONSTRAINT `fk_clinical_access_events_patient` FOREIGN KEY (`patient_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_clinical_access_events_actor` FOREIGN KEY (`actor_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_clinical_access_events_role` CHECK (`actor_role_code` IN ('doctor','manager','administrator')),
    CONSTRAINT `ck_clinical_access_events_scope` CHECK (`scope_code` IN ('summary','timeline','clinical','financial','document','pdf','audit')),
    CONSTRAINT `ck_clinical_access_events_outcome` CHECK (`outcome_code` IN ('allowed','denied')),
    CONSTRAINT `ck_clinical_access_events_purpose` CHECK (
        (`actor_role_code` = 'doctor')
        OR CHAR_LENGTH(TRIM(`purpose`)) BETWEEN 10 AND 500
    )
) ENGINE=InnoDB;

CREATE TRIGGER `trg_medical_record_versions_block_update`
BEFORE UPDATE ON `medical_record_versions`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'medical_record_versions is append-only';

CREATE TRIGGER `trg_medical_record_versions_block_delete`
BEFORE DELETE ON `medical_record_versions`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'medical_record_versions is append-only';

CREATE TRIGGER `trg_clinical_access_events_block_update`
BEFORE UPDATE ON `clinical_access_events`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'clinical_access_events is append-only';

CREATE TRIGGER `trg_clinical_access_events_block_delete`
BEFORE DELETE ON `clinical_access_events`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'clinical_access_events is append-only';
