-- Rollback manual da migration 0006. Não executar em produção sem backup e autorização.

DROP TABLE IF EXISTS `professional_reviews`;

ALTER TABLE `holidays`
    DROP CHECK `ck_holidays_row_version`,
    DROP COLUMN `row_version`,
    DROP COLUMN `updated_at_utc`,
    DROP COLUMN `created_at_utc`;

ALTER TABLE `doctor_weekly_hours`
    DROP CHECK `ck_doctor_weekly_hours_row_version`,
    DROP COLUMN `row_version`,
    DROP COLUMN `updated_at_utc`,
    DROP COLUMN `created_at_utc`;

ALTER TABLE `doctor_profiles`
    DROP CHECK `ck_doctor_profiles_row_version`,
    DROP COLUMN `row_version`;

ALTER TABLE `clinic_weekly_hours`
    DROP CHECK `ck_clinic_weekly_hours_row_version`,
    DROP COLUMN `row_version`,
    DROP COLUMN `updated_at_utc`,
    DROP COLUMN `created_at_utc`;

ALTER TABLE `appointment_types`
    DROP CHECK `ck_appointment_types_row_version`,
    DROP COLUMN `row_version`;

ALTER TABLE `specialties`
    DROP CHECK `ck_specialties_row_version`,
    DROP COLUMN `row_version`,
    DROP COLUMN `updated_at_utc`,
    DROP COLUMN `created_at_utc`;

ALTER TABLE `clinic`
    DROP CHECK `ck_clinic_address`,
    DROP COLUMN `state_code`,
    DROP COLUMN `city`,
    DROP COLUMN `district`,
    DROP COLUMN `complement`,
    DROP COLUMN `number`,
    DROP COLUMN `street`,
    DROP COLUMN `postal_code`;
