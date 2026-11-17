-- Preparado somente para revisão. Não executar como parte da validação.
DROP TABLE patient_birthday_daily_notices;
DELETE FROM application_settings WHERE setting_key = 'appointments.allow_pending_no_show';
