ALTER TABLE `accounts`
    DROP CHECK `ck_accounts_portal_access`,
    DROP CHECK `ck_accounts_contact`,
    DROP COLUMN `portal_access_enabled`,
    ADD CONSTRAINT `ck_accounts_contact`
        CHECK (`normalized_email` IS NOT NULL OR `phone_e164` IS NOT NULL);
