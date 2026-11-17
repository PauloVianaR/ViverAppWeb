ALTER TABLE accounts
    ADD COLUMN password_setup_required TINYINT(1) NOT NULL DEFAULT 0 AFTER password_hash,
    ADD CONSTRAINT ck_accounts_password_setup CHECK (
        password_setup_required IN (0, 1)
        AND (password_setup_required = 0 OR
            (password_hash IS NULL AND portal_access_enabled = 1 AND normalized_email IS NOT NULL))
    );

INSERT INTO application_settings
    (setting_key, value_json, description, is_secret, updated_at_utc, row_version)
VALUES ('users.admin_registration_email_enabled', 'true',
    'Enviar e-mail de boas-vindas quando o Administrador cadastrar um usuário.',
    0, UTC_TIMESTAMP(6), 1);
