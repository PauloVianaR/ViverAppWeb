-- O padrão original de 120 minutos impedia agendamentos operacionais no próximo intervalo.
-- Converter somente esse padrão; antecedentes personalizados continuam preservados.
START TRANSACTION;
UPDATE application_settings AS lead_setting
JOIN application_settings AS slot_setting
    ON slot_setting.setting_key = 'appointments.slot_interval_minutes'
SET lead_setting.value_json = slot_setting.value_json,
    lead_setting.updated_at_utc = UTC_TIMESTAMP(6),
    lead_setting.row_version = lead_setting.row_version + 1
WHERE lead_setting.setting_key = 'appointments.minimum_lead_minutes'
    AND JSON_UNQUOTE(lead_setting.value_json) = '120'
    AND CAST(JSON_UNQUOTE(slot_setting.value_json) AS UNSIGNED) BETWEEN 5 AND 120;
COMMIT;
