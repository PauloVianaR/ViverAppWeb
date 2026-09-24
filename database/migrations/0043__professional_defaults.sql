-- Aplicar somente em viverappweb / MySQL 8.0.41. Não modifica o banco legado.
ALTER TABLE professional_profiles
    MODIFY default_appointment_duration_minutes smallint unsigned NOT NULL DEFAULT 10;

UPDATE application_settings
SET value_json = CAST('true' AS JSON), updated_at_utc = UTC_TIMESTAMP(6)
WHERE setting_key = 'professional.patient_scheduling_enabled' AND JSON_UNQUOTE(value_json) = 'false';
