-- Estruturas DB-First para Identity, MFA TOTP, recovery codes e passkeys.

CREATE TABLE `account_authenticators` (
    `account_id` bigint unsigned NOT NULL,
    `protected_key` varbinary(2048) NULL,
    `is_enabled` tinyint(1) NOT NULL DEFAULT 0,
    `created_at_utc` datetime(6) NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    `enabled_at_utc` datetime(6) NULL,
    PRIMARY KEY (`account_id`),
    CONSTRAINT `fk_account_authenticators_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_account_authenticators_enabled` CHECK (`is_enabled` IN (0, 1)),
    CONSTRAINT `ck_account_authenticators_key` CHECK (`is_enabled` = 0 OR `protected_key` IS NOT NULL)
) ENGINE=InnoDB;

CREATE TABLE `account_recovery_codes` (
    `id` binary(16) NOT NULL,
    `account_id` bigint unsigned NOT NULL,
    `code_hash` binary(32) NOT NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `used_at_utc` datetime(6) NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_account_recovery_codes_hash` (`account_id`, `code_hash`),
    KEY `ix_account_recovery_codes_available` (`account_id`, `used_at_utc`),
    CONSTRAINT `fk_account_recovery_codes_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE
) ENGINE=InnoDB;

CREATE TABLE `account_passkeys` (
    `credential_id` varbinary(1024) NOT NULL,
    `account_id` bigint unsigned NOT NULL,
    `public_key` varbinary(2048) NOT NULL,
    `display_name` varchar(100) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `sign_count` int unsigned NOT NULL DEFAULT 0,
    `transports_json` json NOT NULL,
    `is_user_verified` tinyint(1) NOT NULL,
    `is_backup_eligible` tinyint(1) NOT NULL,
    `is_backed_up` tinyint(1) NOT NULL,
    `attestation_object` blob NOT NULL,
    `client_data_json` blob NOT NULL,
    PRIMARY KEY (`credential_id`),
    KEY `ix_account_passkeys_account` (`account_id`, `created_at_utc`),
    CONSTRAINT `fk_account_passkeys_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_account_passkeys_flags` CHECK (
        `is_user_verified` IN (0, 1)
        AND `is_backup_eligible` IN (0, 1)
        AND `is_backed_up` IN (0, 1))
) ENGINE=InnoDB;

ALTER TABLE `auth_sessions`
    ADD COLUMN `authentication_method` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'password' AFTER `refresh_token_hash`,
    ADD COLUMN `mfa_satisfied` tinyint(1) NOT NULL DEFAULT 0 AFTER `authentication_method`,
    ADD CONSTRAINT `ck_auth_sessions_method` CHECK (`authentication_method` IN ('password', 'email_code', 'sms_code', 'google', 'passkey')),
    ADD CONSTRAINT `ck_auth_sessions_mfa` CHECK (`mfa_satisfied` IN (0, 1));

ALTER TABLE `external_logins`
    ADD COLUMN `provider_email_verified` tinyint(1) NOT NULL DEFAULT 0 AFTER `provider_email`,
    ADD CONSTRAINT `ck_external_logins_email_verified` CHECK (`provider_email_verified` IN (0, 1));
