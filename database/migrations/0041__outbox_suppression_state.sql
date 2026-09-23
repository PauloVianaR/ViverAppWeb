-- Fase 22. Aplicar somente em viverappweb / MySQL 8.0.41.
ALTER TABLE outbox_messages
    DROP CHECK ck_outbox_messages_status,
    ADD CONSTRAINT ck_outbox_messages_status
        CHECK (status_code IN ('pending', 'processing', 'sent', 'suppressed', 'dead_letter'));
