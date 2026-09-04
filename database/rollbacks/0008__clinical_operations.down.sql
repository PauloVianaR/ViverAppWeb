-- Rollback manual da Fase 8. Usar somente em ambiente controlado e após backup.

DROP TABLE IF EXISTS `medical_reports`;

ALTER TABLE `appointments`
    DROP CHECK `ck_appointments_no_show_record`,
    DROP CHECK `ck_appointments_completion`,
    DROP FOREIGN KEY `fk_appointments_no_show_actor`,
    DROP FOREIGN KEY `fk_appointments_completer`,
    DROP INDEX `ix_appointments_patient_doctor_status`,
    DROP INDEX `ix_appointments_doctor_status_end`,
    DROP COLUMN `no_show_recorded_at_utc`,
    DROP COLUMN `no_show_recorded_by_account_id`,
    DROP COLUMN `completed_at_utc`,
    DROP COLUMN `completed_by_account_id`;
