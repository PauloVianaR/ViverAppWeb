-- Rollback documentado. Não executar sem comprovar que não há autoria clínica de Gestor.

DELETE FROM `application_settings`
WHERE `setting_key` IN (
    'doctor.patient_scheduling_enabled',
    'premium.manager_can_manage',
    'manager.medical_records_write_enabled'
);

ALTER TABLE `medical_record_versions`
    DROP FOREIGN KEY `fk_medical_record_versions_author_account`,
    DROP INDEX `ix_medical_record_versions_author_time`,
    CHANGE COLUMN `author_account_id` `author_doctor_account_id` bigint unsigned NOT NULL,
    ADD KEY `ix_medical_record_versions_author_time` (`author_doctor_account_id`, `finalized_at_utc`),
    ADD CONSTRAINT `fk_medical_record_versions_author`
        FOREIGN KEY (`author_doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE RESTRICT;

ALTER TABLE `medical_record_entries`
    DROP FOREIGN KEY `fk_medical_record_entries_author_account`,
    DROP INDEX `ix_medical_record_entries_author_created`,
    CHANGE COLUMN `author_account_id` `author_doctor_account_id` bigint unsigned NOT NULL,
    ADD KEY `ix_medical_record_entries_author_created` (`author_doctor_account_id`, `created_at_utc`),
    ADD CONSTRAINT `fk_medical_record_entries_author`
        FOREIGN KEY (`author_doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE RESTRICT;

ALTER TABLE `medical_record_drafts`
    DROP FOREIGN KEY `fk_medical_record_drafts_author_account`,
    DROP INDEX `ux_medical_record_drafts_appointment_author`,
    CHANGE COLUMN `author_account_id` `author_doctor_account_id` bigint unsigned NOT NULL,
    ADD UNIQUE KEY `ux_medical_record_drafts_appointment_author` (`appointment_id`, `author_doctor_account_id`),
    ADD CONSTRAINT `fk_medical_record_drafts_author`
        FOREIGN KEY (`author_doctor_account_id`) REFERENCES `doctor_profiles` (`account_id`) ON DELETE RESTRICT;
