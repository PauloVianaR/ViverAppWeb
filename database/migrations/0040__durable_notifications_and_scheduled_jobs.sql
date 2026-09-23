-- Fase 22. Aplicar somente em viverappweb / MySQL 8.0.41.
ALTER TABLE outbox_messages
    ADD COLUMN account_id bigint unsigned NULL AFTER recipient,
    ADD COLUMN template_version smallint unsigned NOT NULL DEFAULT 1 AFTER template_key,
    ADD COLUMN provider_reference varchar(150) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER last_error_code,
    ADD COLUMN completed_at_utc datetime(6) NULL AFTER sent_at_utc,
    ADD KEY ix_outbox_messages_account (account_id, created_at_utc),
    ADD CONSTRAINT fk_outbox_messages_account FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE SET NULL,
    ADD CONSTRAINT ck_outbox_messages_template_version CHECK (template_version > 0);

CREATE TABLE notification_preferences (
    account_id bigint unsigned NOT NULL,
    reminder_email_enabled tinyint(1) NOT NULL DEFAULT 1,
    reminder_sms_enabled tinyint(1) NOT NULL DEFAULT 0,
    premium_updates_enabled tinyint(1) NOT NULL DEFAULT 1,
    updated_at_utc datetime(6) NOT NULL,
    row_version bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (account_id),
    CONSTRAINT fk_notification_preferences_account FOREIGN KEY (account_id) REFERENCES accounts (id) ON DELETE CASCADE,
    CONSTRAINT ck_notification_preferences_email CHECK (reminder_email_enabled IN (0, 1)),
    CONSTRAINT ck_notification_preferences_sms CHECK (reminder_sms_enabled IN (0, 1)),
    CONSTRAINT ck_notification_preferences_premium CHECK (premium_updates_enabled IN (0, 1)),
    CONSTRAINT ck_notification_preferences_version CHECK (row_version > 0)
) ENGINE=InnoDB;

CREATE TABLE notification_suppressions (
    id bigint unsigned NOT NULL AUTO_INCREMENT,
    channel_code varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    recipient_hash binary(32) NOT NULL,
    reason_code varchar(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at_utc datetime(6) NOT NULL,
    expires_at_utc datetime(6) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_notification_suppressions_destination (channel_code, recipient_hash),
    CONSTRAINT ck_notification_suppressions_channel CHECK (channel_code IN ('email', 'sms'))
) ENGINE=InnoDB;

CREATE TABLE scheduled_jobs (
    id bigint unsigned NOT NULL AUTO_INCREMENT,
    job_key varchar(150) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    job_type_code varchar(50) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    appointment_id bigint unsigned NULL,
    due_at_utc datetime(6) NOT NULL,
    status_code varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'pending',
    attempt_count smallint unsigned NOT NULL DEFAULT 0,
    max_attempts smallint unsigned NOT NULL DEFAULT 5,
    lease_owner varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    lease_until_utc datetime(6) NULL,
    next_attempt_at_utc datetime(6) NOT NULL,
    last_error_code varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    created_at_utc datetime(6) NOT NULL,
    completed_at_utc datetime(6) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_scheduled_jobs_key (job_key),
    KEY ix_scheduled_jobs_claim (status_code, next_attempt_at_utc, lease_until_utc),
    KEY ix_scheduled_jobs_appointment (appointment_id, due_at_utc),
    CONSTRAINT fk_scheduled_jobs_appointment FOREIGN KEY (appointment_id) REFERENCES appointments (id) ON DELETE RESTRICT,
    CONSTRAINT ck_scheduled_jobs_type CHECK (job_type_code IN ('appointment_reminder')),
    CONSTRAINT ck_scheduled_jobs_status CHECK (status_code IN ('pending', 'processing', 'succeeded', 'skipped', 'dead_letter')),
    CONSTRAINT ck_scheduled_jobs_attempts CHECK (max_attempts > 0 AND attempt_count <= max_attempts)
) ENGINE=InnoDB;
