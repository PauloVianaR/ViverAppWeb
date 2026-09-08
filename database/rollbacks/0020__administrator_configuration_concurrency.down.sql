ALTER TABLE `holidays`
    DROP CHECK `ck_holidays_is_annual`,
    DROP COLUMN `is_annual`;

ALTER TABLE `premium_plans`
    DROP CHECK `ck_premium_plans_row_version`,
    DROP COLUMN `row_version`;

ALTER TABLE `application_settings`
    DROP CHECK `ck_application_settings_row_version`,
    DROP COLUMN `row_version`;
