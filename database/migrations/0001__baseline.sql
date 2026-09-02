-- ViverApp Web baseline — MySQL 8.0.41.
-- Fonte de verdade do primeiro scaffold DB-First.

CREATE TABLE `roles` (
    `code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `display_name` varchar(50) NOT NULL,
    `is_privileged` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`code`),
    CONSTRAINT `ck_roles_privileged` CHECK (`is_privileged` IN (0, 1))
) ENGINE=InnoDB;

INSERT INTO `roles` (`code`, `display_name`, `is_privileged`) VALUES
    ('administrator', 'Administrador', 1),
    ('doctor', 'Médico', 0),
    ('manager', 'Gestor', 0),
    ('patient', 'Paciente', 0)
ON DUPLICATE KEY UPDATE
    `display_name` = VALUES(`display_name`),
    `is_privileged` = VALUES(`is_privileged`);

CREATE TABLE `clinic` (
    `singleton_id` tinyint unsigned NOT NULL DEFAULT 1,
    `legal_name` varchar(200) NOT NULL,
    `display_name` varchar(120) NOT NULL,
    `tax_id` char(14) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `email` varchar(254) NULL,
    `phone_e164` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `timezone_name` varchar(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'America/Sao_Paulo',
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    `row_version` bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (`singleton_id`),
    UNIQUE KEY `ux_clinic_tax_id` (`tax_id`),
    CONSTRAINT `ck_clinic_singleton` CHECK (`singleton_id` = 1),
    CONSTRAINT `ck_clinic_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;

CREATE TABLE `accounts` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `role_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `status_code` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'pending_confirmation',
    `full_name` varchar(200) NOT NULL,
    `email` varchar(254) NULL,
    `normalized_email` varchar(254) NULL,
    `phone_e164` varchar(16) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `password_hash` varchar(512) NULL,
    `email_verified` tinyint(1) NOT NULL DEFAULT 0,
    `phone_verified` tinyint(1) NOT NULL DEFAULT 0,
    `preferred_recovery_channel` varchar(10) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `security_stamp` binary(32) NOT NULL,
    `failed_login_count` smallint unsigned NOT NULL DEFAULT 0,
    `lockout_end_utc` datetime(6) NULL,
    `last_login_at_utc` datetime(6) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    `row_version` bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_accounts_normalized_email` (`normalized_email`),
    UNIQUE KEY `ux_accounts_phone_e164` (`phone_e164`),
    KEY `ix_accounts_role_status` (`role_code`, `status_code`),
    CONSTRAINT `fk_accounts_role` FOREIGN KEY (`role_code`) REFERENCES `roles` (`code`) ON DELETE RESTRICT,
    CONSTRAINT `ck_accounts_status` CHECK (`status_code` IN ('pending_confirmation', 'pending_approval', 'active', 'rejected', 'blocked')),
    CONSTRAINT `ck_accounts_contact` CHECK (`normalized_email` IS NOT NULL OR `phone_e164` IS NOT NULL),
    CONSTRAINT `ck_accounts_email_verified` CHECK (`email_verified` IN (0, 1) AND (`email_verified` = 0 OR `normalized_email` IS NOT NULL)),
    CONSTRAINT `ck_accounts_phone_verified` CHECK (`phone_verified` IN (0, 1) AND (`phone_verified` = 0 OR `phone_e164` IS NOT NULL)),
    CONSTRAINT `ck_accounts_recovery_channel` CHECK (`preferred_recovery_channel` IS NULL OR `preferred_recovery_channel` IN ('email', 'sms')),
    CONSTRAINT `ck_accounts_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;

CREATE TABLE `account_addresses` (
    `account_id` bigint unsigned NOT NULL,
    `postal_code` char(8) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `street` varchar(200) NOT NULL,
    `number` varchar(20) NOT NULL,
    `complement` varchar(100) NULL,
    `district` varchar(100) NOT NULL,
    `city` varchar(100) NOT NULL,
    `state_code` char(2) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`account_id`),
    CONSTRAINT `fk_account_addresses_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE `external_logins` (
    `provider_code` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `provider_subject` varchar(255) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `account_id` bigint unsigned NOT NULL,
    `provider_email` varchar(254) NULL,
    `linked_at_utc` datetime(6) NOT NULL,
    `last_used_at_utc` datetime(6) NULL,
    PRIMARY KEY (`provider_code`, `provider_subject`),
    UNIQUE KEY `ux_external_logins_account_provider` (`account_id`, `provider_code`),
    CONSTRAINT `fk_external_logins_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_external_logins_provider` CHECK (`provider_code` IN ('google'))
) ENGINE=InnoDB;

CREATE TABLE `auth_sessions` (
    `id` binary(16) NOT NULL,
    `account_id` bigint unsigned NOT NULL,
    `refresh_token_hash` binary(32) NOT NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `expires_at_utc` datetime(6) NOT NULL,
    `last_seen_at_utc` datetime(6) NULL,
    `revoked_at_utc` datetime(6) NULL,
    `revoke_reason_code` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `ip_address_hash` binary(32) NULL,
    `user_agent_hash` binary(32) NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_auth_sessions_refresh_hash` (`refresh_token_hash`),
    KEY `ix_auth_sessions_account_expiry` (`account_id`, `expires_at_utc`),
    CONSTRAINT `fk_auth_sessions_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_auth_sessions_expiry` CHECK (`expires_at_utc` > `created_at_utc`)
) ENGINE=InnoDB;

CREATE TABLE `account_challenges` (
    `id` binary(16) NOT NULL,
    `account_id` bigint unsigned NOT NULL,
    `purpose_code` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `channel_code` varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `secret_hash` binary(32) NOT NULL,
    `destination_hash` binary(32) NOT NULL,
    `attempt_count` smallint unsigned NOT NULL DEFAULT 0,
    `max_attempts` smallint unsigned NOT NULL DEFAULT 5,
    `created_at_utc` datetime(6) NOT NULL,
    `expires_at_utc` datetime(6) NOT NULL,
    `consumed_at_utc` datetime(6) NULL,
    PRIMARY KEY (`id`),
    KEY `ix_account_challenges_account_purpose` (`account_id`, `purpose_code`, `created_at_utc`),
    CONSTRAINT `fk_account_challenges_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_account_challenges_purpose` CHECK (`purpose_code` IN ('login', 'password_reset', 'contact_verification')),
    CONSTRAINT `ck_account_challenges_channel` CHECK (`channel_code` IN ('email', 'sms')),
    CONSTRAINT `ck_account_challenges_expiry` CHECK (`expires_at_utc` > `created_at_utc`),
    CONSTRAINT `ck_account_challenges_attempts` CHECK (`max_attempts` > 0 AND `attempt_count` <= `max_attempts`)
) ENGINE=InnoDB;

CREATE TABLE `patient_profiles` (
    `account_id` bigint unsigned NOT NULL,
    `tax_id` char(11) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `birth_date` date NULL,
    `preferred_name` varchar(120) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`account_id`),
    UNIQUE KEY `ux_patient_profiles_tax_id` (`tax_id`),
    CONSTRAINT `fk_patient_profiles_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE `doctor_profiles` (
    `account_id` bigint unsigned NOT NULL,
    `license_state_code` char(2) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `license_number` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `biography` text NULL,
    `default_appointment_duration_minutes` smallint unsigned NOT NULL DEFAULT 30,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`account_id`),
    UNIQUE KEY `ux_doctor_profiles_license` (`license_state_code`, `license_number`),
    CONSTRAINT `fk_doctor_profiles_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_doctor_profiles_duration` CHECK (`default_appointment_duration_minutes` BETWEEN 5 AND 480)
) ENGINE=InnoDB;

CREATE TABLE `specialties` (
    `id` int unsigned NOT NULL AUTO_INCREMENT,
    `name` varchar(120) NOT NULL,
    `normalized_name` varchar(120) NOT NULL,
    `is_active` tinyint(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_specialties_normalized_name` (`normalized_name`),
    CONSTRAINT `ck_specialties_active` CHECK (`is_active` IN (0, 1))
) ENGINE=InnoDB;

CREATE TABLE `doctor_specialties` (
    `doctor_account_id` bigint unsigned NOT NULL,
    `specialty_id` int unsigned NOT NULL,
    `is_primary` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`doctor_account_id`, `specialty_id`),
    KEY `ix_doctor_specialties_specialty` (`specialty_id`),
    CONSTRAINT `fk_doctor_specialties_doctor` FOREIGN KEY (`doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_doctor_specialties_specialty` FOREIGN KEY (`specialty_id`) REFERENCES `specialties` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_doctor_specialties_primary` CHECK (`is_primary` IN (0, 1))
) ENGINE=InnoDB;

CREATE TABLE `appointment_types` (
    `id` int unsigned NOT NULL AUTO_INCREMENT,
    `name` varchar(120) NOT NULL,
    `description` varchar(500) NULL,
    `modality_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `duration_minutes` smallint unsigned NOT NULL,
    `price_amount` decimal(13,2) NOT NULL,
    `is_active` tinyint(1) NOT NULL DEFAULT 1,
    `display_order` smallint unsigned NOT NULL DEFAULT 0,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    CONSTRAINT `ck_appointment_types_modality` CHECK (`modality_code` IN ('in_person', 'online', 'both')),
    CONSTRAINT `ck_appointment_types_duration` CHECK (`duration_minutes` BETWEEN 5 AND 480),
    CONSTRAINT `ck_appointment_types_price` CHECK (`price_amount` >= 0),
    CONSTRAINT `ck_appointment_types_active` CHECK (`is_active` IN (0, 1))
) ENGINE=InnoDB;

CREATE TABLE `clinic_weekly_hours` (
    `id` int unsigned NOT NULL AUTO_INCREMENT,
    `day_of_week` tinyint unsigned NOT NULL,
    `start_time` time NOT NULL,
    `end_time` time NOT NULL,
    `is_active` tinyint(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_clinic_weekly_hours` (`day_of_week`, `start_time`, `end_time`),
    CONSTRAINT `ck_clinic_weekly_hours_day` CHECK (`day_of_week` BETWEEN 0 AND 6),
    CONSTRAINT `ck_clinic_weekly_hours_range` CHECK (`end_time` > `start_time`),
    CONSTRAINT `ck_clinic_weekly_hours_active` CHECK (`is_active` IN (0, 1))
) ENGINE=InnoDB;

CREATE TABLE `doctor_weekly_hours` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `doctor_account_id` bigint unsigned NOT NULL,
    `day_of_week` tinyint unsigned NOT NULL,
    `start_time` time NOT NULL,
    `end_time` time NOT NULL,
    `valid_from` date NULL,
    `valid_until` date NULL,
    `is_active` tinyint(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_doctor_weekly_hours` (`doctor_account_id`, `day_of_week`, `start_time`, `end_time`, `valid_from`),
    CONSTRAINT `fk_doctor_weekly_hours_doctor` FOREIGN KEY (`doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE CASCADE,
    CONSTRAINT `ck_doctor_weekly_hours_day` CHECK (`day_of_week` BETWEEN 0 AND 6),
    CONSTRAINT `ck_doctor_weekly_hours_range` CHECK (`end_time` > `start_time`),
    CONSTRAINT `ck_doctor_weekly_hours_validity` CHECK (`valid_until` IS NULL OR `valid_from` IS NULL OR `valid_until` >= `valid_from`),
    CONSTRAINT `ck_doctor_weekly_hours_active` CHECK (`is_active` IN (0, 1))
) ENGINE=InnoDB;

CREATE TABLE `holidays` (
    `id` int unsigned NOT NULL AUTO_INCREMENT,
    `holiday_date` date NOT NULL,
    `name` varchar(120) NOT NULL,
    `start_time` time NULL,
    `end_time` time NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_holidays_date_name` (`holiday_date`, `name`),
    CONSTRAINT `ck_holidays_period` CHECK ((`start_time` IS NULL AND `end_time` IS NULL) OR (`start_time` IS NOT NULL AND `end_time` > `start_time`))
) ENGINE=InnoDB;

CREATE TABLE `appointments` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `patient_account_id` bigint unsigned NOT NULL,
    `doctor_account_id` bigint unsigned NOT NULL,
    `appointment_type_id` int unsigned NOT NULL,
    `created_by_account_id` bigint unsigned NOT NULL,
    `status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'pending',
    `modality_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `starts_at_utc` datetime(6) NOT NULL,
    `ends_at_utc` datetime(6) NOT NULL,
    `price_amount` decimal(13,2) NOT NULL,
    `currency_code` char(3) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'BRL',
    `cancellation_reason` varchar(500) NULL,
    `canceled_by_account_id` bigint unsigned NULL,
    `canceled_at_utc` datetime(6) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    `row_version` bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_appointments_doctor_start` (`doctor_account_id`, `starts_at_utc`),
    UNIQUE KEY `ux_appointments_patient_start` (`patient_account_id`, `starts_at_utc`),
    KEY `ix_appointments_status_start` (`status_code`, `starts_at_utc`),
    KEY `ix_appointments_patient_status` (`patient_account_id`, `status_code`, `starts_at_utc`),
    CONSTRAINT `fk_appointments_patient` FOREIGN KEY (`patient_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_appointments_doctor` FOREIGN KEY (`doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_appointments_type` FOREIGN KEY (`appointment_type_id`) REFERENCES `appointment_types` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_appointments_creator` FOREIGN KEY (`created_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_appointments_canceler` FOREIGN KEY (`canceled_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_appointments_status` CHECK (`status_code` IN ('pending', 'confirmed', 'completed', 'canceled', 'rescheduled', 'no_show')),
    CONSTRAINT `ck_appointments_modality` CHECK (`modality_code` IN ('in_person', 'online')),
    CONSTRAINT `ck_appointments_period` CHECK (`ends_at_utc` > `starts_at_utc`),
    CONSTRAINT `ck_appointments_price` CHECK (`price_amount` >= 0),
    CONSTRAINT `ck_appointments_currency` CHECK (`currency_code` = 'BRL'),
    CONSTRAINT `ck_appointments_cancellation` CHECK ((`status_code` = 'canceled' AND `canceled_at_utc` IS NOT NULL) OR `status_code` <> 'canceled'),
    CONSTRAINT `ck_appointments_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;

CREATE TABLE `appointment_documents` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `appointment_id` bigint unsigned NOT NULL,
    `uploaded_by_account_id` bigint unsigned NOT NULL,
    `category_code` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `object_key` varchar(512) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `original_file_name` varchar(255) NOT NULL,
    `content_type` varchar(127) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `size_bytes` bigint unsigned NOT NULL,
    `sha256` binary(32) NOT NULL,
    `status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'pending_scan',
    `created_at_utc` datetime(6) NOT NULL,
    `available_at_utc` datetime(6) NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_appointment_documents_object_key` (`object_key`),
    KEY `ix_appointment_documents_appointment` (`appointment_id`, `created_at_utc`),
    CONSTRAINT `fk_appointment_documents_appointment` FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_appointment_documents_uploader` FOREIGN KEY (`uploaded_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_appointment_documents_category` CHECK (`category_code` IN ('attachment', 'medical_report')),
    CONSTRAINT `ck_appointment_documents_status` CHECK (`status_code` IN ('pending_scan', 'available', 'quarantined', 'deleted')),
    CONSTRAINT `ck_appointment_documents_size` CHECK (`size_bytes` > 0)
) ENGINE=InnoDB;

CREATE TABLE `payments` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `appointment_id` bigint unsigned NOT NULL,
    `provider_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'pending',
    `amount` decimal(13,2) NOT NULL,
    `currency_code` char(3) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'BRL',
    `idempotency_key` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `provider_checkout_id` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `provider_transaction_id` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `provider_status_code` varchar(50) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    `paid_at_utc` datetime(6) NULL,
    `canceled_at_utc` datetime(6) NULL,
    `row_version` bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_payments_appointment` (`appointment_id`),
    UNIQUE KEY `ux_payments_idempotency_key` (`idempotency_key`),
    UNIQUE KEY `ux_payments_provider_checkout` (`provider_code`, `provider_checkout_id`),
    UNIQUE KEY `ux_payments_provider_transaction` (`provider_code`, `provider_transaction_id`),
    KEY `ix_payments_status_created` (`status_code`, `created_at_utc`),
    CONSTRAINT `fk_payments_appointment` FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_payments_provider` CHECK (`provider_code` IN ('pagbank', 'internal')),
    CONSTRAINT `ck_payments_status` CHECK (`status_code` IN ('pending', 'authorized', 'paid', 'failed', 'canceled', 'refunded')),
    CONSTRAINT `ck_payments_amount` CHECK (`amount` >= 0),
    CONSTRAINT `ck_payments_currency` CHECK (`currency_code` = 'BRL'),
    CONSTRAINT `ck_payments_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;

CREATE TABLE `premium_plans` (
    `id` int unsigned NOT NULL AUTO_INCREMENT,
    `name` varchar(120) NOT NULL,
    `price_amount` decimal(13,2) NOT NULL,
    `appointment_discount_percent` decimal(5,2) NOT NULL DEFAULT 0,
    `validity_days` smallint unsigned NULL,
    `is_active` tinyint(1) NOT NULL DEFAULT 1,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    CONSTRAINT `ck_premium_plans_price` CHECK (`price_amount` >= 0),
    CONSTRAINT `ck_premium_plans_discount` CHECK (`appointment_discount_percent` BETWEEN 0 AND 100),
    CONSTRAINT `ck_premium_plans_validity` CHECK (`validity_days` IS NULL OR `validity_days` > 0),
    CONSTRAINT `ck_premium_plans_active` CHECK (`is_active` IN (0, 1))
) ENGINE=InnoDB;

CREATE TABLE `premium_memberships` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `account_id` bigint unsigned NOT NULL,
    `premium_plan_id` int unsigned NOT NULL,
    `status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'pending',
    `starts_at_utc` datetime(6) NULL,
    `ends_at_utc` datetime(6) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    KEY `ix_premium_memberships_account_status` (`account_id`, `status_code`, `ends_at_utc`),
    CONSTRAINT `fk_premium_memberships_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_premium_memberships_plan` FOREIGN KEY (`premium_plan_id`) REFERENCES `premium_plans` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_premium_memberships_status` CHECK (`status_code` IN ('pending', 'active', 'rejected', 'expired', 'canceled')),
    CONSTRAINT `ck_premium_memberships_period` CHECK (`ends_at_utc` IS NULL OR `starts_at_utc` IS NULL OR `ends_at_utc` > `starts_at_utc`)
) ENGINE=InnoDB;

CREATE TABLE `outbox_messages` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `channel_code` varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `template_key` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `recipient` varchar(254) NOT NULL,
    `payload_json` json NOT NULL,
    `status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'pending',
    `idempotency_key` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `attempt_count` smallint unsigned NOT NULL DEFAULT 0,
    `max_attempts` smallint unsigned NOT NULL DEFAULT 5,
    `next_attempt_at_utc` datetime(6) NOT NULL,
    `lease_owner` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `lease_until_utc` datetime(6) NULL,
    `last_error_code` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `sent_at_utc` datetime(6) NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_outbox_messages_idempotency` (`idempotency_key`),
    KEY `ix_outbox_messages_claim` (`status_code`, `next_attempt_at_utc`, `lease_until_utc`),
    CONSTRAINT `ck_outbox_messages_channel` CHECK (`channel_code` IN ('email', 'sms')),
    CONSTRAINT `ck_outbox_messages_status` CHECK (`status_code` IN ('pending', 'processing', 'sent', 'dead_letter')),
    CONSTRAINT `ck_outbox_messages_attempts` CHECK (`max_attempts` > 0 AND `attempt_count` <= `max_attempts`)
) ENGINE=InnoDB;

CREATE TABLE `audit_events` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `actor_account_id` bigint unsigned NULL,
    `event_code` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `entity_type` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `entity_id` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `correlation_id` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `ip_address_hash` binary(32) NULL,
    `data_json` json NULL,
    `occurred_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    KEY `ix_audit_events_actor_time` (`actor_account_id`, `occurred_at_utc`),
    KEY `ix_audit_events_entity` (`entity_type`, `entity_id`, `occurred_at_utc`),
    KEY `ix_audit_events_correlation` (`correlation_id`),
    CONSTRAINT `fk_audit_events_actor` FOREIGN KEY (`actor_account_id`) REFERENCES `accounts` (`id`) ON DELETE SET NULL
) ENGINE=InnoDB;

CREATE TABLE `application_settings` (
    `setting_key` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `value_json` json NOT NULL,
    `description` varchar(500) NULL,
    `is_secret` tinyint(1) NOT NULL DEFAULT 0,
    `updated_at_utc` datetime(6) NOT NULL,
    `updated_by_account_id` bigint unsigned NULL,
    PRIMARY KEY (`setting_key`),
    CONSTRAINT `fk_application_settings_updater` FOREIGN KEY (`updated_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE SET NULL,
    CONSTRAINT `ck_application_settings_secret` CHECK (`is_secret` = 0)
) ENGINE=InnoDB;

INSERT INTO `application_settings` (`setting_key`, `value_json`, `description`, `is_secret`, `updated_at_utc`) VALUES
    ('system.single_clinic', CAST('true' AS JSON), 'O produto atende exatamente uma clínica.', 0, UTC_TIMESTAMP(6)),
    ('system.timezone', JSON_QUOTE('America/Sao_Paulo'), 'Fuso operacional padrão da clínica.', 0, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    `value_json` = VALUES(`value_json`),
    `description` = VALUES(`description`),
    `is_secret` = 0,
    `updated_at_utc` = UTC_TIMESTAMP(6);

CREATE TABLE `idempotency_records` (
    `scope_code` varchar(50) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `idempotency_key` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `request_hash` binary(32) NOT NULL,
    `response_status_code` smallint unsigned NULL,
    `response_body_json` json NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `expires_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`scope_code`, `idempotency_key`),
    KEY `ix_idempotency_records_expiry` (`expires_at_utc`),
    CONSTRAINT `ck_idempotency_records_expiry` CHECK (`expires_at_utc` > `created_at_utc`)
) ENGINE=InnoDB;
