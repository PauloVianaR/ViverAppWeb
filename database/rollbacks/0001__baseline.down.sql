-- Rollback destrutivo da baseline.
-- NÃO executar sem autorização explícita e backup verificado.
-- Alvo permitido: viverappweb em MySQL 8.0.41.

SET FOREIGN_KEY_CHECKS = 0;
DROP TABLE IF EXISTS `idempotency_records`;
DROP TABLE IF EXISTS `application_settings`;
DROP TABLE IF EXISTS `audit_events`;
DROP TABLE IF EXISTS `outbox_messages`;
DROP TABLE IF EXISTS `premium_memberships`;
DROP TABLE IF EXISTS `premium_plans`;
DROP TABLE IF EXISTS `payments`;
DROP TABLE IF EXISTS `appointment_documents`;
DROP TABLE IF EXISTS `appointments`;
DROP TABLE IF EXISTS `holidays`;
DROP TABLE IF EXISTS `doctor_weekly_hours`;
DROP TABLE IF EXISTS `clinic_weekly_hours`;
DROP TABLE IF EXISTS `appointment_types`;
DROP TABLE IF EXISTS `doctor_specialties`;
DROP TABLE IF EXISTS `specialties`;
DROP TABLE IF EXISTS `doctor_profiles`;
DROP TABLE IF EXISTS `patient_profiles`;
DROP TABLE IF EXISTS `account_challenges`;
DROP TABLE IF EXISTS `auth_sessions`;
DROP TABLE IF EXISTS `external_logins`;
DROP TABLE IF EXISTS `account_addresses`;
DROP TABLE IF EXISTS `accounts`;
DROP TABLE IF EXISTS `clinic`;
DROP TABLE IF EXISTS `roles`;
SET FOREIGN_KEY_CHECKS = 1;
