-- Pacientes, numeração operacional, chegada e notificação do médico — MySQL 8.0.41.

ALTER TABLE `appointments`
    DROP CHECK `ck_appointments_status`,
    ADD COLUMN `appointment_number` bigint unsigned NULL AFTER `id`,
    ADD COLUMN `arrived_at_utc` datetime(6) NULL AFTER `no_show_recorded_at_utc`,
    ADD COLUMN `arrival_business_date` date NULL AFTER `arrived_at_utc`,
    ADD COLUMN `arrival_queue_number` int unsigned NULL AFTER `arrival_business_date`,
    ADD COLUMN `arrival_recorded_by_account_id` bigint unsigned NULL AFTER `arrival_queue_number`,
    ADD UNIQUE KEY `ux_appointments_number` (`appointment_number`),
    ADD UNIQUE KEY `ux_appointments_arrival_queue` (`arrival_business_date`, `arrival_queue_number`),
    ADD KEY `ix_appointments_arrival_actor` (`arrival_recorded_by_account_id`),
    ADD CONSTRAINT `fk_appointments_arrival_actor`
        FOREIGN KEY (`arrival_recorded_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    ADD CONSTRAINT `ck_appointments_status`
        CHECK (`status_code` IN ('pending', 'confirmed', 'arrived', 'in_progress', 'completed', 'canceled', 'rescheduled', 'no_show')),
    ADD CONSTRAINT `ck_appointments_arrival`
        CHECK (
            (`arrived_at_utc` IS NULL AND `arrival_business_date` IS NULL AND `arrival_queue_number` IS NULL AND `arrival_recorded_by_account_id` IS NULL)
            OR
            (`arrived_at_utc` IS NOT NULL AND `arrival_business_date` IS NOT NULL AND `arrival_queue_number` >= 100 AND `arrival_recorded_by_account_id` IS NOT NULL)
        );

UPDATE `appointments` AS target
JOIN (
    SELECT `id`, ROW_NUMBER() OVER (ORDER BY `created_at_utc`, `id`) + 99 AS `number_value`
    FROM `appointments`
) AS ranked ON ranked.`id` = target.`id`
SET target.`appointment_number` = ranked.`number_value`;

ALTER TABLE `appointments`
    MODIFY COLUMN `appointment_number` bigint unsigned NOT NULL;

ALTER TABLE `appointment_status_history`
    DROP CHECK `ck_appointment_status_history_from_status`,
    DROP CHECK `ck_appointment_status_history_to_status`,
    ADD CONSTRAINT `ck_appointment_status_history_from_status`
        CHECK (`from_status_code` IS NULL OR `from_status_code` IN ('pending', 'confirmed', 'arrived', 'in_progress', 'completed', 'canceled', 'rescheduled', 'no_show')),
    ADD CONSTRAINT `ck_appointment_status_history_to_status`
        CHECK (`to_status_code` IN ('pending', 'confirmed', 'arrived', 'in_progress', 'completed', 'canceled', 'rescheduled', 'no_show'));

CREATE TABLE `appointment_number_sequence` (
    `sequence_key` tinyint unsigned NOT NULL,
    `next_value` bigint unsigned NOT NULL,
    PRIMARY KEY (`sequence_key`),
    CONSTRAINT `ck_appointment_number_sequence_key` CHECK (`sequence_key` = 1),
    CONSTRAINT `ck_appointment_number_sequence_value` CHECK (`next_value` >= 100)
) ENGINE=InnoDB;

INSERT INTO `appointment_number_sequence` (`sequence_key`, `next_value`)
SELECT 1, GREATEST(COALESCE(MAX(`appointment_number`), 99) + 1, 100)
FROM `appointments`;

CREATE TABLE `arrival_queue_sequences` (
    `business_date` date NOT NULL,
    `next_value` int unsigned NOT NULL,
    `updated_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`business_date`),
    CONSTRAINT `ck_arrival_queue_sequences_value` CHECK (`next_value` >= 100)
) ENGINE=InnoDB;

CREATE TABLE `doctor_notifications` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `doctor_account_id` bigint unsigned NOT NULL,
    `appointment_id` bigint unsigned NOT NULL,
    `source_key` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `type_code` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `read_at_utc` datetime(6) NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `row_version` bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_doctor_notifications_source` (`doctor_account_id`, `source_key`),
    KEY `ix_doctor_notifications_feed` (`doctor_account_id`, `read_at_utc`, `created_at_utc`),
    KEY `ix_doctor_notifications_appointment` (`appointment_id`),
    CONSTRAINT `fk_doctor_notifications_doctor`
        FOREIGN KEY (`doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE CASCADE,
    CONSTRAINT `fk_doctor_notifications_appointment`
        FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_doctor_notifications_type` CHECK (`type_code` IN ('patient_arrived')),
    CONSTRAINT `ck_doctor_notifications_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;

INSERT INTO `application_settings`
    (`setting_key`, `value_json`, `description`, `is_secret`, `updated_at_utc`)
VALUES
    ('appointments.arrival_notifications_enabled', CAST('true' AS JSON), 'Habilita notificações duráveis de chegada para o médico.', 0, UTC_TIMESTAMP(6)),
    ('appointments.arrival_popup_enabled', CAST('true' AS JSON), 'Exibe aviso visual de chegada nas sessões do médico.', 0, UTC_TIMESTAMP(6)),
    ('appointments.arrival_sound_enabled', CAST('true' AS JSON), 'Habilita o aviso sonoro de chegada nas sessões do médico.', 0, UTC_TIMESTAMP(6)),
    ('appointments.arrival_sound_volume', CAST('60' AS JSON), 'Volume padrão do aviso sonoro, entre 0 e 100.', 0, UTC_TIMESTAMP(6)),
    ('appointments.arrival_sound_key', JSON_QUOTE('soft_chime'), 'Som local aprovado para o aviso de chegada.', 0, UTC_TIMESTAMP(6)),
    ('appointments.arrival_early_minutes', CAST('120' AS JSON), 'Antecedência máxima, em minutos, para registrar a chegada.', 0, UTC_TIMESTAMP(6)),
    ('appointments.arrival_late_minutes', CAST('30' AS JSON), 'Tolerância, em minutos, para registrar a chegada após o horário.', 0, UTC_TIMESTAMP(6)),
    ('appointments.arrival_notification_retention_days', CAST('30' AS JSON), 'Retenção das notificações de chegada, em dias.', 0, UTC_TIMESTAMP(6)),
    ('appointments.arrival_mark_read_on_open', CAST('true' AS JSON), 'Marca a notificação como lida ao abrir o atendimento.', 0, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    `description` = VALUES(`description`),
    `is_secret` = 0,
    `updated_at_utc` = UTC_TIMESTAMP(6);
