-- Plano documental: concluir os jobs e eventos internos antes de executar.
ALTER TABLE scheduled_jobs
    DROP CHECK ck_scheduled_jobs_type,
    ADD CONSTRAINT ck_scheduled_jobs_type CHECK (job_type_code IN ('appointment_reminder'));

ALTER TABLE outbox_messages
    DROP CHECK ck_outbox_messages_channel,
    ADD CONSTRAINT ck_outbox_messages_channel CHECK (channel_code IN ('email', 'sms'));
