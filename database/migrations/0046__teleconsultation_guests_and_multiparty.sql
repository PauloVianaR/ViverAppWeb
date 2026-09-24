-- Convites temporários de videochamada e presença para até quatro pessoas.
-- MySQL 8.0.41 / viverappweb. Não armazena mídia nem segredo em texto claro.

CREATE TABLE `teleconsultation_guest_links` (
    `id` binary(16) NOT NULL,
    `appointment_id` bigint unsigned NOT NULL,
    `created_by_account_id` bigint unsigned NOT NULL,
    `token_hash` binary(32) NOT NULL,
    `created_at_utc` datetime(6) NOT NULL,
    `expires_at_utc` datetime(6) NOT NULL,
    `revoked_at_utc` datetime(6) NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_teleconsultation_guest_links_appointment` (`appointment_id`),
    UNIQUE KEY `ux_teleconsultation_guest_links_hash` (`token_hash`),
    KEY `ix_teleconsultation_guest_links_creator` (`created_by_account_id`),
    CONSTRAINT `fk_teleconsultation_guest_links_appointment`
        FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_teleconsultation_guest_links_creator`
        FOREIGN KEY (`created_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_teleconsultation_guest_links_expiry`
        CHECK (`expires_at_utc` > `created_at_utc`)
) ENGINE=InnoDB;

ALTER TABLE `teleconsultation_peers`
    DROP PRIMARY KEY,
    MODIFY COLUMN `account_id` bigint unsigned NULL,
    ADD COLUMN `id` bigint unsigned NOT NULL AUTO_INCREMENT FIRST,
    ADD COLUMN `guest_id` binary(16) NULL AFTER `account_id`,
    ADD PRIMARY KEY (`id`),
    ADD UNIQUE KEY `ux_teleconsultation_peers_account` (`appointment_id`, `account_id`),
    ADD UNIQUE KEY `ux_teleconsultation_peers_guest` (`appointment_id`, `guest_id`),
    ADD UNIQUE KEY `ux_teleconsultation_peers_connection` (`connection_id`),
    ADD CONSTRAINT `ck_teleconsultation_peers_identity`
        CHECK ((`account_id` IS NOT NULL AND `guest_id` IS NULL)
            OR (`account_id` IS NULL AND `guest_id` IS NOT NULL));
