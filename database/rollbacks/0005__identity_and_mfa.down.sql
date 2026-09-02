-- Remove as estruturas de identidade adicionadas pela migration 0005.

ALTER TABLE `external_logins`
    DROP CHECK `ck_external_logins_email_verified`,
    DROP COLUMN `provider_email_verified`;

ALTER TABLE `auth_sessions`
    DROP CHECK `ck_auth_sessions_mfa`,
    DROP CHECK `ck_auth_sessions_method`,
    DROP COLUMN `mfa_satisfied`,
    DROP COLUMN `authentication_method`;

DROP TABLE IF EXISTS `account_passkeys`;
DROP TABLE IF EXISTS `account_recovery_codes`;
DROP TABLE IF EXISTS `account_authenticators`;
