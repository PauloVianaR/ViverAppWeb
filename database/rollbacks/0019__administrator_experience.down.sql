DROP TABLE IF EXISTS `administrator_notifications`;

DELETE FROM `application_settings`
WHERE `setting_key` IN (
    'web.maintenance_mode',
    'appointments.online_calls_enabled',
    'appointments.default_consultation_minutes',
    'appointments.default_examination_minutes',
    'appointments.default_surgery_minutes',
    'appointments.interval_minutes',
    'communications.email_enabled',
    'communications.sms_enabled'
)
AND `updated_by_account_id` IS NULL;
