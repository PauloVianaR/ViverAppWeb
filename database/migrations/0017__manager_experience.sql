-- Fase 13. Executar exclusivamente em viverappweb, pelo runner que valida MySQL 8.0.41.
CREATE TABLE manager_preferences (
    manager_account_id bigint unsigned NOT NULL PRIMARY KEY,
    email_enabled tinyint(1) NOT NULL,
    sms_enabled tinyint(1) NOT NULL,
    updated_at_utc datetime(6) NOT NULL,
    row_version bigint unsigned NOT NULL DEFAULT 1,
    CONSTRAINT fk_manager_preferences_account FOREIGN KEY (manager_account_id) REFERENCES accounts(id) ON DELETE CASCADE,
    CONSTRAINT ck_manager_preferences_role CHECK (email_enabled IN (0, 1) AND sms_enabled IN (0, 1)),
    CONSTRAINT ck_manager_preferences_row_version CHECK (row_version > 0)
) ENGINE=InnoDB;

INSERT INTO manager_preferences (manager_account_id,email_enabled,sms_enabled,updated_at_utc,row_version)
SELECT id,1,1,UTC_TIMESTAMP(6),1 FROM accounts WHERE role_code='manager';

ALTER TABLE payments
    ADD confirmed_by_account_id bigint unsigned NULL,
    ADD card_last_four char(4) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD authorization_reference varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD KEY ix_payments_confirmer (confirmed_by_account_id, paid_at_utc),
    ADD CONSTRAINT fk_payments_confirmer FOREIGN KEY (confirmed_by_account_id) REFERENCES accounts(id) ON DELETE RESTRICT,
    ADD CONSTRAINT ck_payments_card_last_four CHECK (card_last_four IS NULL OR card_last_four REGEXP '^[0-9]{4}$');

ALTER TABLE premium_memberships
    ADD reviewed_by_account_id bigint unsigned NULL,
    ADD review_notes varchar(1000) NULL,
    ADD KEY ix_premium_memberships_reviewer (reviewed_by_account_id, reviewed_at_utc),
    ADD CONSTRAINT fk_premium_memberships_reviewer FOREIGN KEY (reviewed_by_account_id) REFERENCES accounts(id) ON DELETE RESTRICT;

INSERT INTO application_settings (setting_key,value_json,description,is_secret,updated_at_utc) VALUES
('premium.manager_can_decide','true','Permite ao Gestor aprovar ou rejeitar solicitações Premium.',0,UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE setting_key=VALUES(setting_key);
