-- Cadastros e configuração da clínica única — MySQL 8.0.41.

ALTER TABLE `clinic`
    ADD COLUMN `postal_code` char(8) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `phone_e164`,
    ADD COLUMN `street` varchar(200) NULL AFTER `postal_code`,
    ADD COLUMN `number` varchar(20) NULL AFTER `street`,
    ADD COLUMN `complement` varchar(100) NULL AFTER `number`,
    ADD COLUMN `district` varchar(100) NULL AFTER `complement`,
    ADD COLUMN `city` varchar(100) NULL AFTER `district`,
    ADD COLUMN `state_code` char(2) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `city`,
    ADD CONSTRAINT `ck_clinic_address`
        CHECK (
            (`postal_code` IS NULL AND `street` IS NULL AND `number` IS NULL AND `district` IS NULL AND `city` IS NULL AND `state_code` IS NULL)
            OR
            (`postal_code` IS NOT NULL AND `street` IS NOT NULL AND `number` IS NOT NULL AND `district` IS NOT NULL AND `city` IS NOT NULL AND `state_code` IS NOT NULL)
        );

ALTER TABLE `specialties`
    ADD COLUMN `created_at_utc` datetime(6) NULL AFTER `is_active`,
    ADD COLUMN `updated_at_utc` datetime(6) NULL AFTER `created_at_utc`,
    ADD COLUMN `row_version` bigint unsigned NOT NULL DEFAULT 1 AFTER `updated_at_utc`,
    ADD CONSTRAINT `ck_specialties_row_version` CHECK (`row_version` > 0);
UPDATE `specialties`
SET `created_at_utc` = UTC_TIMESTAMP(6), `updated_at_utc` = UTC_TIMESTAMP(6)
WHERE `created_at_utc` IS NULL OR `updated_at_utc` IS NULL;
ALTER TABLE `specialties`
    MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL,
    MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL;

ALTER TABLE `appointment_types`
    ADD COLUMN `row_version` bigint unsigned NOT NULL DEFAULT 1 AFTER `updated_at_utc`,
    ADD CONSTRAINT `ck_appointment_types_row_version` CHECK (`row_version` > 0);

ALTER TABLE `clinic_weekly_hours`
    ADD COLUMN `created_at_utc` datetime(6) NULL AFTER `is_active`,
    ADD COLUMN `updated_at_utc` datetime(6) NULL AFTER `created_at_utc`,
    ADD COLUMN `row_version` bigint unsigned NOT NULL DEFAULT 1 AFTER `updated_at_utc`,
    ADD CONSTRAINT `ck_clinic_weekly_hours_row_version` CHECK (`row_version` > 0);
UPDATE `clinic_weekly_hours`
SET `created_at_utc` = UTC_TIMESTAMP(6), `updated_at_utc` = UTC_TIMESTAMP(6)
WHERE `created_at_utc` IS NULL OR `updated_at_utc` IS NULL;
ALTER TABLE `clinic_weekly_hours`
    MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL,
    MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL;

ALTER TABLE `doctor_profiles`
    ADD COLUMN `row_version` bigint unsigned NOT NULL DEFAULT 1 AFTER `updated_at_utc`,
    ADD CONSTRAINT `ck_doctor_profiles_row_version` CHECK (`row_version` > 0);

ALTER TABLE `doctor_weekly_hours`
    ADD COLUMN `created_at_utc` datetime(6) NULL AFTER `is_active`,
    ADD COLUMN `updated_at_utc` datetime(6) NULL AFTER `created_at_utc`,
    ADD COLUMN `row_version` bigint unsigned NOT NULL DEFAULT 1 AFTER `updated_at_utc`,
    ADD CONSTRAINT `ck_doctor_weekly_hours_row_version` CHECK (`row_version` > 0);
UPDATE `doctor_weekly_hours`
SET `created_at_utc` = UTC_TIMESTAMP(6), `updated_at_utc` = UTC_TIMESTAMP(6)
WHERE `created_at_utc` IS NULL OR `updated_at_utc` IS NULL;
ALTER TABLE `doctor_weekly_hours`
    MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL,
    MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL;

ALTER TABLE `holidays`
    ADD COLUMN `created_at_utc` datetime(6) NULL AFTER `end_time`,
    ADD COLUMN `updated_at_utc` datetime(6) NULL AFTER `created_at_utc`,
    ADD COLUMN `row_version` bigint unsigned NOT NULL DEFAULT 1 AFTER `updated_at_utc`,
    ADD CONSTRAINT `ck_holidays_row_version` CHECK (`row_version` > 0);
UPDATE `holidays`
SET `created_at_utc` = UTC_TIMESTAMP(6), `updated_at_utc` = UTC_TIMESTAMP(6)
WHERE `created_at_utc` IS NULL OR `updated_at_utc` IS NULL;
ALTER TABLE `holidays`
    MODIFY COLUMN `created_at_utc` datetime(6) NOT NULL,
    MODIFY COLUMN `updated_at_utc` datetime(6) NOT NULL;

CREATE TABLE `professional_reviews` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `professional_account_id` bigint unsigned NOT NULL,
    `reviewer_account_id` bigint unsigned NOT NULL,
    `decision_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `reason` varchar(500) NULL,
    `occurred_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    KEY `ix_professional_reviews_professional_time` (`professional_account_id`, `occurred_at_utc`),
    KEY `ix_professional_reviews_reviewer_time` (`reviewer_account_id`, `occurred_at_utc`),
    CONSTRAINT `fk_professional_reviews_professional` FOREIGN KEY (`professional_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_professional_reviews_reviewer` FOREIGN KEY (`reviewer_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_professional_reviews_decision` CHECK (`decision_code` IN ('approved', 'rejected', 'blocked', 'reactivated')),
    CONSTRAINT `ck_professional_reviews_reason` CHECK (`decision_code` IN ('approved', 'reactivated') OR (`reason` IS NOT NULL AND CHAR_LENGTH(TRIM(`reason`)) >= 5))
) ENGINE=InnoDB;
