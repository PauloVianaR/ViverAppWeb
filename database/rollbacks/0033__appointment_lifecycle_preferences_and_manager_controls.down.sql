-- Rollback manual documentado; não executar sem autorização explícita.
-- Antes de restaurar a restrição anterior, remova ou recategorize tipos com category_code = 'procedure'.

DELETE FROM `application_settings`
WHERE `setting_key` IN (
    'manager.appointment_types_enabled',
    'manager.doctor_schedules_enabled',
    'appointments.default_procedure_minutes'
);

DROP TABLE `account_ui_preferences`;

ALTER TABLE `appointment_types`
    DROP CHECK `ck_appointment_types_category`,
    ADD CONSTRAINT `ck_appointment_types_category`
        CHECK (`category_code` IN ('consultation','examination','surgery'));
