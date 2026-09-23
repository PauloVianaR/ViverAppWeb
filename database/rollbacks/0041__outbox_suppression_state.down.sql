-- Plano documental: exige tratar mensagens suppressed antes de executar.
ALTER TABLE outbox_messages
    DROP CHECK ck_outbox_messages_status,
    ADD CONSTRAINT ck_outbox_messages_status
        CHECK (status_code IN ('pending', 'processing', 'sent', 'dead_letter'));
