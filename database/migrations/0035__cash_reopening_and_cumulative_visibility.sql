-- Fase 18. Reabertura auditável do caixa e permissões financeiras — MySQL 8.0.41.

ALTER TABLE `cash_closures`
    DROP INDEX `ux_cash_closures_date`,
    ADD KEY `ix_cash_closures_date_time` (`operational_date`, `closed_at_utc`);

CREATE TABLE `cash_reopenings` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `cash_closure_id` bigint unsigned NOT NULL,
    `reopened_by_account_id` bigint unsigned NOT NULL,
    `reason` varchar(500) NOT NULL,
    `reopened_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_cash_reopenings_closure` (`cash_closure_id`),
    KEY `ix_cash_reopenings_actor_time` (`reopened_by_account_id`, `reopened_at_utc`),
    CONSTRAINT `fk_cash_reopenings_closure` FOREIGN KEY (`cash_closure_id`) REFERENCES `cash_closures` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_cash_reopenings_actor` FOREIGN KEY (`reopened_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_cash_reopenings_reason` CHECK (CHAR_LENGTH(TRIM(`reason`)) >= 5)
) ENGINE=InnoDB;

CREATE TRIGGER `trg_cash_reopenings_block_update`
BEFORE UPDATE ON `cash_reopenings`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'cash_reopenings is append-only';

CREATE TRIGGER `trg_cash_reopenings_block_delete`
BEFORE DELETE ON `cash_reopenings`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'cash_reopenings is append-only';

INSERT INTO `application_settings`
    (`setting_key`, `value_json`, `description`, `is_secret`, `updated_at_utc`, `updated_by_account_id`)
VALUES
    ('cash.manager_can_reopen', 'false', 'Permite ao Gestor reabrir o caixa do dia atual.', 0, UTC_TIMESTAMP(6), NULL),
    ('cash.manager_can_view_cumulative_totals', 'true', 'Permite ao Gestor consultar os totalizadores gerais acumulados do caixa.', 0, UTC_TIMESTAMP(6), NULL);
