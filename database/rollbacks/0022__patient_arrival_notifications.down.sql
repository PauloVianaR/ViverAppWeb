-- Pré-condição: não pode haver atendimentos/históricos em arrived ou in_progress.
DELETE FROM `application_settings`
WHERE `setting_key` IN (
    'appointments.arrival_notifications_enabled',
    'appointments.arrival_popup_enabled',
    'appointments.arrival_sound_enabled',
    'appointments.arrival_sound_volume',
    'appointments.arrival_sound_key',
    'appointments.arrival_early_minutes',
    'appointments.arrival_late_minutes',
    'appointments.arrival_notification_retention_days',
    'appointments.arrival_mark_read_on_open'
);

DROP TABLE `doctor_notifications`;
DROP TABLE `arrival_queue_sequences`;
DROP TABLE `appointment_number_sequence`;

ALTER TABLE `appointment_status_history`
    DROP CHECK `ck_appointment_status_history_from_status`,
    DROP CHECK `ck_appointment_status_history_to_status`,
    ADD CONSTRAINT `ck_appointment_status_history_from_status`
        CHECK (`from_status_code` IS NULL OR `from_status_code` IN ('pending', 'confirmed', 'completed', 'canceled', 'rescheduled', 'no_show')),
    ADD CONSTRAINT `ck_appointment_status_history_to_status`
        CHECK (`to_status_code` IN ('pending', 'confirmed', 'completed', 'canceled', 'rescheduled', 'no_show'));

ALTER TABLE `appointments`
    DROP CHECK `ck_appointments_arrival`,
    DROP CHECK `ck_appointments_status`,
    DROP FOREIGN KEY `fk_appointments_arrival_actor`,
    DROP INDEX `ix_appointments_arrival_actor`,
    DROP INDEX `ux_appointments_arrival_queue`,
    DROP INDEX `ux_appointments_number`,
    DROP COLUMN `arrival_recorded_by_account_id`,
    DROP COLUMN `arrival_queue_number`,
    DROP COLUMN `arrival_business_date`,
    DROP COLUMN `arrived_at_utc`,
    DROP COLUMN `appointment_number`,
    ADD CONSTRAINT `ck_appointments_status`
        CHECK (`status_code` IN ('pending', 'confirmed', 'completed', 'canceled', 'rescheduled', 'no_show'));
