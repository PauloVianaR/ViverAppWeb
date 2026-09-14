ALTER TABLE `accounts`
    DROP CHECK `ck_accounts_contact`,
    ADD COLUMN `portal_access_enabled` tinyint(1) NOT NULL DEFAULT 1 AFTER `phone_verified`,
    ADD CONSTRAINT `ck_accounts_contact`
        CHECK ((`role_code` = 'patient' AND `portal_access_enabled` = 0)
            OR `normalized_email` IS NOT NULL
            OR `phone_e164` IS NOT NULL),
    ADD CONSTRAINT `ck_accounts_portal_access`
        CHECK (`portal_access_enabled` IN (0, 1));
