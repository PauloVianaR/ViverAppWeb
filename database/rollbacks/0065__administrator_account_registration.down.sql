DELETE FROM application_settings WHERE setting_key = 'users.admin_registration_email_enabled';
ALTER TABLE accounts DROP CHECK ck_accounts_password_setup, DROP COLUMN password_setup_required;
