-- Apenas após retirar todas as conexões convidadas; não executar como ensaio destrutivo.
ALTER TABLE `teleconsultation_peers`
    DROP CHECK `ck_teleconsultation_peers_identity`,
    DROP INDEX `ux_teleconsultation_peers_connection`,
    DROP INDEX `ux_teleconsultation_peers_guest`,
    DROP INDEX `ux_teleconsultation_peers_account`,
    DROP PRIMARY KEY,
    DROP COLUMN `guest_id`,
    DROP COLUMN `id`,
    MODIFY COLUMN `account_id` bigint unsigned NOT NULL,
    ADD PRIMARY KEY (`appointment_id`, `account_id`);

DROP TABLE `teleconsultation_guest_links`;
