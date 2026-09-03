-- Rollback manual da migration 0007. Não executar em produção sem backup e autorização.

DELETE FROM `application_settings`
WHERE `setting_key` IN (
    'appointments.booking_horizon_days',
    'appointments.minimum_lead_minutes',
    'appointments.cancellation_cutoff_hours',
    'appointments.reschedule_cutoff_hours',
    'appointments.slot_interval_minutes'
);

DROP TABLE IF EXISTS `appointment_status_history`;

ALTER TABLE `appointments`
    DROP FOREIGN KEY `fk_appointments_rescheduled_from`,
    DROP INDEX `ix_appointments_patient_period`,
    DROP INDEX `ix_appointments_doctor_period`,
    DROP INDEX `ux_appointments_rescheduled_from`,
    DROP COLUMN `rescheduled_from_appointment_id`,
    DROP COLUMN `patient_notes`;
