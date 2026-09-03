-- Jornada de agenda e agendamento do paciente — MySQL 8.0.41.

ALTER TABLE `appointments`
    ADD COLUMN `patient_notes` varchar(1000) NULL AFTER `currency_code`,
    ADD COLUMN `rescheduled_from_appointment_id` bigint unsigned NULL AFTER `canceled_at_utc`,
    ADD UNIQUE KEY `ux_appointments_rescheduled_from` (`rescheduled_from_appointment_id`),
    ADD KEY `ix_appointments_doctor_period` (`doctor_account_id`, `status_code`, `starts_at_utc`, `ends_at_utc`),
    ADD KEY `ix_appointments_patient_period` (`patient_account_id`, `status_code`, `starts_at_utc`, `ends_at_utc`),
    ADD CONSTRAINT `fk_appointments_rescheduled_from`
        FOREIGN KEY (`rescheduled_from_appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT;

CREATE TABLE `appointment_status_history` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `appointment_id` bigint unsigned NOT NULL,
    `actor_account_id` bigint unsigned NOT NULL,
    `from_status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `to_status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `reason` varchar(500) NULL,
    `starts_at_utc` datetime(6) NOT NULL,
    `ends_at_utc` datetime(6) NOT NULL,
    `occurred_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    KEY `ix_appointment_status_history_appointment_time` (`appointment_id`, `occurred_at_utc`),
    KEY `ix_appointment_status_history_actor_time` (`actor_account_id`, `occurred_at_utc`),
    CONSTRAINT `fk_appointment_status_history_appointment`
        FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_appointment_status_history_actor`
        FOREIGN KEY (`actor_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_appointment_status_history_from_status`
        CHECK (`from_status_code` IS NULL OR `from_status_code` IN ('pending', 'confirmed', 'completed', 'canceled', 'rescheduled', 'no_show')),
    CONSTRAINT `ck_appointment_status_history_to_status`
        CHECK (`to_status_code` IN ('pending', 'confirmed', 'completed', 'canceled', 'rescheduled', 'no_show')),
    CONSTRAINT `ck_appointment_status_history_period` CHECK (`ends_at_utc` > `starts_at_utc`)
) ENGINE=InnoDB;

INSERT INTO `application_settings`
    (`setting_key`, `value_json`, `description`, `is_secret`, `updated_at_utc`)
VALUES
    ('appointments.booking_horizon_days', CAST('365' AS JSON), 'Antecedência máxima, em dias, para novos agendamentos.', 0, UTC_TIMESTAMP(6)),
    ('appointments.minimum_lead_minutes', CAST('120' AS JSON), 'Antecedência mínima, em minutos, para novos agendamentos.', 0, UTC_TIMESTAMP(6)),
    ('appointments.cancellation_cutoff_hours', CAST('24' AS JSON), 'Antecedência mínima, em horas, para cancelamento pelo paciente.', 0, UTC_TIMESTAMP(6)),
    ('appointments.reschedule_cutoff_hours', CAST('24' AS JSON), 'Antecedência mínima, em horas, para reagendamento pelo paciente.', 0, UTC_TIMESTAMP(6)),
    ('appointments.slot_interval_minutes', CAST('10' AS JSON), 'Intervalo, em minutos, entre o início dos horários oferecidos.', 0, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    `description` = VALUES(`description`),
    `is_secret` = 0,
    `updated_at_utc` = UTC_TIMESTAMP(6);
