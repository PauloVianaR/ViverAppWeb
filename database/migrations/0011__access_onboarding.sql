ALTER TABLE `accounts`
    ADD COLUMN `tax_id` char(11) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `phone_e164`,
    ADD COLUMN `birth_date` date NULL AFTER `tax_id`;

UPDATE `accounts` AS `account`
INNER JOIN `patient_profiles` AS `profile` ON `profile`.`account_id` = `account`.`id`
SET
    `account`.`tax_id` = `profile`.`tax_id`,
    `account`.`birth_date` = `profile`.`birth_date`
WHERE `profile`.`tax_id` IS NOT NULL OR `profile`.`birth_date` IS NOT NULL;

ALTER TABLE `accounts`
    ADD UNIQUE KEY `ux_accounts_tax_id` (`tax_id`),
    ADD CONSTRAINT `ck_accounts_tax_id` CHECK (`tax_id` IS NULL OR `tax_id` REGEXP '^[0-9]{11}$'),
    ADD CONSTRAINT `ck_accounts_birth_date` CHECK (`birth_date` IS NULL OR `birth_date` >= '1900-01-01');

ALTER TABLE `doctor_profiles`
    ADD COLUMN `professional_title` varchar(4) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Dr.' AFTER `account_id`,
    ADD COLUMN `years_experience` smallint unsigned NOT NULL DEFAULT 0 AFTER `biography`,
    ADD CONSTRAINT `ck_doctor_profiles_title` CHECK (`professional_title` IN ('Dr.', 'Dra.')),
    ADD CONSTRAINT `ck_doctor_profiles_experience` CHECK (`years_experience` <= 80);

CREATE TABLE `account_consents` (
    `account_id` bigint unsigned NOT NULL,
    `terms_version` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `privacy_version` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `accepted_at_utc` datetime(6) NOT NULL,
    `source_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    PRIMARY KEY (`account_id`),
    CONSTRAINT `fk_account_consents_account` FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_account_consents_source` CHECK (`source_code` IN ('local', 'google'))
) ENGINE=InnoDB;
