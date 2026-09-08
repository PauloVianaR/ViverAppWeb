ALTER TABLE `application_settings`
    ADD COLUMN `row_version` BIGINT UNSIGNED NOT NULL DEFAULT 1,
    ADD CONSTRAINT `ck_application_settings_row_version` CHECK (`row_version` > 0);

ALTER TABLE `premium_plans`
    ADD COLUMN `row_version` BIGINT UNSIGNED NOT NULL DEFAULT 1,
    ADD CONSTRAINT `ck_premium_plans_row_version` CHECK (`row_version` > 0);

ALTER TABLE `holidays`
    ADD COLUMN `is_annual` TINYINT(1) NOT NULL DEFAULT 0,
    ADD CONSTRAINT `ck_holidays_is_annual` CHECK (`is_annual` IN (0, 1));

