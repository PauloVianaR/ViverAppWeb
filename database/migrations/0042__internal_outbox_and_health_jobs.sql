-- Fase 22. Aplicar somente em viverappweb / MySQL 8.0.41.
ALTER TABLE outbox_messages
    DROP CHECK ck_outbox_messages_channel,
    ADD CONSTRAINT ck_outbox_messages_channel CHECK (channel_code IN ('email', 'sms', 'internal'));

ALTER TABLE scheduled_jobs
    DROP CHECK ck_scheduled_jobs_type,
    ADD CONSTRAINT ck_scheduled_jobs_type CHECK (job_type_code IN ('appointment_reminder', 'notification_health'));
