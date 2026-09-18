ALTER TABLE `appointment_types`
    DROP CHECK `ck_appointment_types_category`,
    ADD CONSTRAINT `ck_appointment_types_category`
        CHECK (`category_code` IN ('consultation','examination','surgery','procedure'));

CREATE TABLE `account_ui_preferences` (
    `account_id` BIGINT UNSIGNED NOT NULL,
    `appointment_view_mode` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'cards',
    `updated_at_utc` DATETIME(6) NOT NULL,
    `row_version` BIGINT UNSIGNED NOT NULL DEFAULT 1,
    PRIMARY KEY (`account_id`),
    CONSTRAINT `fk_account_ui_preferences_account`
        FOREIGN KEY (`account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_account_ui_preferences_appointment_view`
        CHECK (`appointment_view_mode` IN ('cards','compact')),
    CONSTRAINT `ck_account_ui_preferences_row_version`
        CHECK (`row_version` > 0)
) ENGINE=InnoDB;

INSERT INTO `account_ui_preferences` (`account_id`, `appointment_view_mode`, `updated_at_utc`, `row_version`)
SELECT `id`, 'cards', UTC_TIMESTAMP(6), 1
FROM `accounts`;

INSERT INTO `application_settings`
    (`setting_key`, `value_json`, `description`, `is_secret`, `updated_at_utc`, `updated_by_account_id`)
VALUES
    ('manager.appointment_types_enabled', 'true', 'Permite ao Gestor cadastrar, precificar e ativar tipos de atendimento.', 0, UTC_TIMESTAMP(6), NULL),
    ('manager.doctor_schedules_enabled', 'true', 'Permite ao Gestor administrar a grade semanal dos médicos.', 0, UTC_TIMESTAMP(6), NULL),
    ('appointments.default_procedure_minutes', '30', 'Duração padrão de procedimentos em minutos.', 0, UTC_TIMESTAMP(6), NULL)
ON DUPLICATE KEY UPDATE `setting_key` = VALUES(`setting_key`);
