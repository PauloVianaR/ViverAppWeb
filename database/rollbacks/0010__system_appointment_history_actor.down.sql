-- Só pode voltar a NOT NULL se não houver eventos automáticos.

ALTER TABLE `appointment_status_history`
    DROP FOREIGN KEY `fk_appointment_status_history_actor`;

ALTER TABLE `appointment_status_history`
    MODIFY COLUMN `actor_account_id` bigint unsigned NOT NULL;

ALTER TABLE `appointment_status_history`
    ADD CONSTRAINT `fk_appointment_status_history_actor`
        FOREIGN KEY (`actor_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT;
