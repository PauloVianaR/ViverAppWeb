-- Restaura os defaults removidos pela migration 0002.
-- NÃO executar sem autorização explícita e backup verificado.

ALTER TABLE `appointment_types` ALTER COLUMN `is_active` SET DEFAULT 1;
ALTER TABLE `clinic_weekly_hours` ALTER COLUMN `is_active` SET DEFAULT 1;
ALTER TABLE `doctor_weekly_hours` ALTER COLUMN `is_active` SET DEFAULT 1;
ALTER TABLE `premium_plans` ALTER COLUMN `is_active` SET DEFAULT 1;
ALTER TABLE `specialties` ALTER COLUMN `is_active` SET DEFAULT 1;
