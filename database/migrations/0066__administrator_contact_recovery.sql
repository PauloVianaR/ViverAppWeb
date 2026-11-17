ALTER TABLE accounts
    ADD COLUMN admin_email_verification_pending TINYINT(1) NOT NULL DEFAULT 0 AFTER password_setup_required,
    ADD CONSTRAINT ck_accounts_admin_email_pending CHECK (
        admin_email_verification_pending IN (0, 1)
        AND (admin_email_verification_pending = 0 OR
            (portal_access_enabled = 1 AND normalized_email IS NOT NULL))
    );
