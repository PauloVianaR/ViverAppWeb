-- Plano documental: não executar sem autorização explícita para remoção de dados.
DROP TABLE scheduled_jobs;
DROP TABLE notification_suppressions;
DROP TABLE notification_preferences;
ALTER TABLE outbox_messages
    DROP FOREIGN KEY fk_outbox_messages_account,
    DROP CHECK ck_outbox_messages_template_version,
    DROP INDEX ix_outbox_messages_account,
    DROP COLUMN completed_at_utc,
    DROP COLUMN provider_reference,
    DROP COLUMN template_version,
    DROP COLUMN account_id;
