-- Plano de reversão documental. Não executar sobre dados reais sem autorização.
DROP TABLE professional_variable_hours;
ALTER TABLE professional_preferences DROP CHECK ck_professional_preferences_availability_mode, DROP COLUMN availability_mode;
ALTER TABLE account_ui_preferences DROP CHECK ck_account_ui_preferences_sidebar_collapsed, DROP COLUMN desktop_sidebar_collapsed;
DELETE FROM application_settings WHERE setting_key = 'web.desktop_sidebar_enabled';
