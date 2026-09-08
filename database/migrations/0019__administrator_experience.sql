CREATE TABLE `administrator_notifications` (
    `id` BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    `administrator_account_id` BIGINT UNSIGNED NOT NULL,
    `source_key` VARCHAR(180) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `type_code` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `severity_code` VARCHAR(16) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `title` VARCHAR(160) NOT NULL,
    `message` VARCHAR(500) NOT NULL,
    `entity_type` VARCHAR(40) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `entity_id` VARCHAR(80) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `read_at_utc` DATETIME(6) NULL,
    `dismissed_at_utc` DATETIME(6) NULL,
    `created_at_utc` DATETIME(6) NOT NULL,
    `row_version` BIGINT UNSIGNED NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `uq_administrator_notifications_source` (`administrator_account_id`, `source_key`),
    KEY `ix_administrator_notifications_feed` (`administrator_account_id`, `dismissed_at_utc`, `created_at_utc`),
    CONSTRAINT `fk_administrator_notifications_account` FOREIGN KEY (`administrator_account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_administrator_notifications_type` CHECK (`type_code` IN ('system_update','approval_pending','payment_pending','rescheduled','canceled','completed','payment_approved','premium_pending','premium_decision')),
    CONSTRAINT `ck_administrator_notifications_severity` CHECK (`severity_code` IN ('info','warning','high')),
    CONSTRAINT `ck_administrator_notifications_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;

INSERT INTO `application_settings` (`setting_key`, `value_json`, `description`, `is_secret`, `updated_at_utc`, `updated_by_account_id`)
VALUES
('web.maintenance_mode', 'false', 'Indica se a experiência Web está em manutenção.', 0, UTC_TIMESTAMP(6), NULL),
('appointments.online_calls_enabled', 'true', 'Permite atendimentos por videochamada.', 0, UTC_TIMESTAMP(6), NULL),
('appointments.default_consultation_minutes', '30', 'Duração padrão de consultas em minutos.', 0, UTC_TIMESTAMP(6), NULL),
('appointments.default_examination_minutes', '30', 'Duração padrão de exames em minutos.', 0, UTC_TIMESTAMP(6), NULL),
('appointments.default_surgery_minutes', '60', 'Duração padrão de cirurgias em minutos.', 0, UTC_TIMESTAMP(6), NULL),
('appointments.interval_minutes', '0', 'Intervalo padrão entre atendimentos.', 0, UTC_TIMESTAMP(6), NULL),
('communications.email_enabled', 'true', 'Permite comunicações operacionais por e-mail.', 0, UTC_TIMESTAMP(6), NULL),
('communications.sms_enabled', 'true', 'Permite comunicações operacionais por SMS.', 0, UTC_TIMESTAMP(6), NULL)
ON DUPLICATE KEY UPDATE `setting_key` = VALUES(`setting_key`);

