-- Eventos automáticos de pagamento não representam ação de uma conta — MySQL 8.0.41.

ALTER TABLE `appointment_status_history`
    DROP FOREIGN KEY `fk_appointment_status_history_actor`;

ALTER TABLE `appointment_status_history`
    MODIFY COLUMN `actor_account_id` bigint unsigned NULL;

ALTER TABLE `appointment_status_history`
    ADD CONSTRAINT `fk_appointment_status_history_actor`
        FOREIGN KEY (`actor_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT;
