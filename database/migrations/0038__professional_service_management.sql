INSERT INTO application_settings
    (setting_key, value_json, description, is_secret, updated_at_utc, row_version)
VALUES
    ('manager.professional_services_enabled', 'true',
     'Permite ao Gestor vincular ou desvincular profissionais dos tipos de atendimento.',
     0, UTC_TIMESTAMP(6), 1)
ON DUPLICATE KEY UPDATE setting_key = VALUES(setting_key);

INSERT INTO professional_preferences
    (professional_account_id, email_enabled, sms_enabled, online_enabled,
     max_online_daily, max_in_person_daily, updated_at_utc, row_version)
SELECT profile.account_id, 1, 1, 1, 8, 16, UTC_TIMESTAMP(6), 1
FROM professional_profiles profile
LEFT JOIN professional_preferences preference
    ON preference.professional_account_id = profile.account_id
WHERE preference.professional_account_id IS NULL;

INSERT INTO professional_services
    (professional_account_id, appointment_type_id, is_active,
     created_at_utc, updated_at_utc, row_version)
SELECT profile.account_id, appointment_type.id, 1,
       UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), 1
FROM professional_profiles profile
CROSS JOIN appointment_types appointment_type
WHERE appointment_type.is_active = 1
  AND NOT EXISTS (
      SELECT 1
      FROM professional_services existing
      WHERE existing.professional_account_id = profile.account_id
  );
