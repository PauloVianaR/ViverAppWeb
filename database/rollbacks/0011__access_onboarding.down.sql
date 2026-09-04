DROP TABLE IF EXISTS `account_consents`;

ALTER TABLE `doctor_profiles`
    DROP CHECK `ck_doctor_profiles_experience`,
    DROP CHECK `ck_doctor_profiles_title`,
    DROP COLUMN `years_experience`,
    DROP COLUMN `professional_title`;

ALTER TABLE `accounts`
    DROP CHECK `ck_accounts_birth_date`,
    DROP CHECK `ck_accounts_tax_id`,
    DROP INDEX `ux_accounts_tax_id`,
    DROP COLUMN `birth_date`,
    DROP COLUMN `tax_id`;
