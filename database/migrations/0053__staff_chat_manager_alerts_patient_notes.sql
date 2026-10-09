-- Comunicação interna e observação administrativa de paciente — MySQL 8.0.41.
ALTER TABLE `patient_profiles`
    ADD COLUMN `administrative_notes` varchar(2000) NULL AFTER `preferred_name`;

CREATE TABLE `staff_messages` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `sender_account_id` bigint unsigned NOT NULL,
    `recipient_account_id` bigint unsigned NOT NULL,
    `client_nonce` char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `body` varchar(2000) NOT NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `read_at_utc` datetime(6) NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_staff_messages_nonce` (`sender_account_id`, `client_nonce`),
    KEY `ix_staff_messages_inbox` (`recipient_account_id`, `read_at_utc`, `created_at_utc`),
    KEY `ix_staff_messages_conversation` (`sender_account_id`, `recipient_account_id`, `created_at_utc`),
    CONSTRAINT `fk_staff_messages_sender` FOREIGN KEY (`sender_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_staff_messages_recipient` FOREIGN KEY (`recipient_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_staff_messages_participants` CHECK (`sender_account_id` <> `recipient_account_id`),
    CONSTRAINT `ck_staff_messages_body` CHECK (char_length(trim(`body`)) BETWEEN 1 AND 2000)
) ENGINE=InnoDB;

CREATE TABLE `manager_notifications` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `manager_account_id` bigint unsigned NOT NULL,
    `appointment_id` bigint unsigned NOT NULL,
    `type_code` varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `read_at_utc` datetime(6) NULL,
    PRIMARY KEY (`id`),
    KEY `ix_manager_notifications_inbox` (`manager_account_id`, `read_at_utc`, `created_at_utc`),
    KEY `ix_manager_notifications_appointment` (`appointment_id`),
    CONSTRAINT `fk_manager_notifications_manager` FOREIGN KEY (`manager_account_id`) REFERENCES `accounts` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_manager_notifications_appointment` FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE CASCADE,
    CONSTRAINT `ck_manager_notifications_type` CHECK (`type_code` IN ('appointment_started', 'appointment_completed'))
) ENGINE=InnoDB;
