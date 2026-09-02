-- Evita propriedades bool? no scaffold DB-First para flags obrigatórias.

ALTER TABLE `appointment_types` ALTER COLUMN `is_active` DROP DEFAULT;
ALTER TABLE `clinic_weekly_hours` ALTER COLUMN `is_active` DROP DEFAULT;
ALTER TABLE `doctor_weekly_hours` ALTER COLUMN `is_active` DROP DEFAULT;
ALTER TABLE `premium_plans` ALTER COLUMN `is_active` DROP DEFAULT;
ALTER TABLE `specialties` ALTER COLUMN `is_active` DROP DEFAULT;
