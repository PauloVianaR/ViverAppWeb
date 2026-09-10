-- Histórico não destrutivo de reagendamentos no mesmo atendimento — MySQL 8.0.41.

CREATE TABLE `appointment_reschedule_history` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `appointment_id` bigint unsigned NOT NULL,
    `sequence_number` int unsigned NOT NULL,
    `actor_account_id` bigint unsigned NOT NULL,
    `previous_starts_at_utc` datetime(6) NOT NULL,
    `previous_ends_at_utc` datetime(6) NOT NULL,
    `new_starts_at_utc` datetime(6) NOT NULL,
    `new_ends_at_utc` datetime(6) NOT NULL,
    `reason` varchar(500) NULL,
    `occurred_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_appointment_reschedule_sequence` (`appointment_id`, `sequence_number`),
    KEY `ix_appointment_reschedule_actor` (`actor_account_id`),
    CONSTRAINT `fk_appointment_reschedule_appointment`
        FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE CASCADE,
    CONSTRAINT `fk_appointment_reschedule_actor`
        FOREIGN KEY (`actor_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_appointment_reschedule_sequence` CHECK (`sequence_number` >= 1),
    CONSTRAINT `ck_appointment_reschedule_dates` CHECK (
        `previous_ends_at_utc` > `previous_starts_at_utc`
        AND `new_ends_at_utc` > `new_starts_at_utc`
    )
) ENGINE=InnoDB;

-- Preserva a data original dos reagendamentos realizados pelo modelo antigo.
INSERT INTO `appointment_reschedule_history`
    (`appointment_id`, `sequence_number`, `actor_account_id`,
     `previous_starts_at_utc`, `previous_ends_at_utc`, `new_starts_at_utc`, `new_ends_at_utc`,
     `reason`, `occurred_at_utc`)
SELECT successor.`id`, 1, successor.`created_by_account_id`,
       original.`starts_at_utc`, original.`ends_at_utc`, successor.`starts_at_utc`, successor.`ends_at_utc`,
       'Histórico preservado do modelo de reagendamento anterior.', successor.`created_at_utc`
FROM `appointments` successor
JOIN `appointments` original ON original.`id` = successor.`rescheduled_from_appointment_id`;
