-- Fase 18. Autoria clínica por conta autorizada e controles operacionais — MySQL 8.0.41.

ALTER TABLE `medical_record_drafts`
    DROP FOREIGN KEY `fk_medical_record_drafts_author`,
    DROP INDEX `ux_medical_record_drafts_appointment_author`,
    CHANGE COLUMN `author_doctor_account_id` `author_account_id` bigint unsigned NOT NULL,
    ADD UNIQUE KEY `ux_medical_record_drafts_appointment_author` (`appointment_id`, `author_account_id`),
    ADD CONSTRAINT `fk_medical_record_drafts_author_account`
        FOREIGN KEY (`author_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT;

ALTER TABLE `medical_record_entries`
    DROP FOREIGN KEY `fk_medical_record_entries_author`,
    DROP INDEX `ix_medical_record_entries_author_created`,
    CHANGE COLUMN `author_doctor_account_id` `author_account_id` bigint unsigned NOT NULL,
    ADD KEY `ix_medical_record_entries_author_created` (`author_account_id`, `created_at_utc`),
    ADD CONSTRAINT `fk_medical_record_entries_author_account`
        FOREIGN KEY (`author_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT;

ALTER TABLE `medical_record_versions`
    DROP FOREIGN KEY `fk_medical_record_versions_author`,
    DROP INDEX `ix_medical_record_versions_author_time`,
    CHANGE COLUMN `author_doctor_account_id` `author_account_id` bigint unsigned NOT NULL,
    ADD KEY `ix_medical_record_versions_author_time` (`author_account_id`, `finalized_at_utc`),
    ADD CONSTRAINT `fk_medical_record_versions_author_account`
        FOREIGN KEY (`author_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT;

INSERT INTO `application_settings`
    (`setting_key`, `value_json`, `description`, `is_secret`, `updated_at_utc`, `updated_by_account_id`)
VALUES
    ('doctor.patient_scheduling_enabled', 'false', 'Permite ao Médico agendar atendimentos para seus pacientes.', 0, UTC_TIMESTAMP(6), NULL),
    ('premium.manager_can_manage', 'true', 'Permite ao Gestor tornar ou deixar de tornar pacientes Premium mediante comprovante.', 0, UTC_TIMESTAMP(6), NULL),
    ('manager.medical_records_write_enabled', 'true', 'Permite ao Gestor escrever e versionar a anamnese e evolução do paciente.', 0, UTC_TIMESTAMP(6), NULL);
