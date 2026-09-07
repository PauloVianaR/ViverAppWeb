-- Rollback manual da Fase 13. Não executar automaticamente.
DELETE FROM application_settings WHERE setting_key='premium.manager_can_decide';
ALTER TABLE premium_memberships
    DROP FOREIGN KEY fk_premium_memberships_reviewer,
    DROP INDEX ix_premium_memberships_reviewer,
    DROP COLUMN reviewed_by_account_id,
    DROP COLUMN review_notes;
ALTER TABLE payments
    DROP FOREIGN KEY fk_payments_confirmer,
    DROP CHECK ck_payments_card_last_four,
    DROP INDEX ix_payments_confirmer,
    DROP COLUMN confirmed_by_account_id,
    DROP COLUMN card_last_four,
    DROP COLUMN authorization_reference;
DROP TABLE manager_preferences;
