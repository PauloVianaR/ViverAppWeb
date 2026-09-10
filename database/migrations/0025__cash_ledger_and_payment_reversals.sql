-- Fase 17. Livro-caixa, fechamento diário e cadeia imutável de pagamentos — MySQL 8.0.41.

ALTER TABLE `payments`
    DROP INDEX `ux_payments_appointment`,
    DROP CHECK `ck_payments_status`,
    DROP CHECK `ck_payments_refund`,
    ADD COLUMN `supersedes_payment_id` bigint unsigned NULL AFTER `appointment_id`,
    ADD COLUMN `reversal_reason` varchar(500) NULL AFTER `refunded_at_utc`,
    ADD COLUMN `reversal_requested_at_utc` datetime(6) NULL AFTER `reversal_reason`,
    ADD COLUMN `reversed_by_account_id` bigint unsigned NULL AFTER `reversal_requested_at_utc`,
    ADD COLUMN `active_appointment_id` bigint unsigned
        GENERATED ALWAYS AS (
            CASE WHEN `status_code` IN ('pending','authorized','paid','reversal_pending')
                THEN `appointment_id` ELSE NULL END
        ) STORED AFTER `reversed_by_account_id`,
    ADD UNIQUE KEY `ux_payments_active_appointment` (`active_appointment_id`),
    ADD UNIQUE KEY `ux_payments_superseded_once` (`supersedes_payment_id`),
    ADD KEY `ix_payments_appointment_created` (`appointment_id`, `created_at_utc`),
    ADD KEY `ix_payments_reversed_by` (`reversed_by_account_id`, `reversal_requested_at_utc`),
    ADD CONSTRAINT `fk_payments_supersedes`
        FOREIGN KEY (`supersedes_payment_id`) REFERENCES `payments` (`id`) ON DELETE RESTRICT,
    ADD CONSTRAINT `fk_payments_reversed_by`
        FOREIGN KEY (`reversed_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    ADD CONSTRAINT `ck_payments_status`
        CHECK (`status_code` IN ('pending','authorized','paid','failed','canceled','reversal_pending','reversed','refunded')),
    ADD CONSTRAINT `ck_payments_refund`
        CHECK (
            (`status_code` = 'refunded' AND `refund_amount` IS NOT NULL AND `refund_amount` > 0 AND `refunded_at_utc` IS NOT NULL)
            OR (`status_code` <> 'refunded' AND (`refund_amount` IS NULL OR `status_code` = 'reversal_pending'))
        ),
    ADD CONSTRAINT `ck_payments_reversal`
        CHECK (
            (`status_code` IN ('reversal_pending','reversed','refunded') AND `reversal_reason` IS NOT NULL
                AND `reversal_requested_at_utc` IS NOT NULL AND `reversed_by_account_id` IS NOT NULL)
            OR (`status_code` NOT IN ('reversal_pending','reversed','refunded'))
        );

ALTER TABLE `appointments`
    ADD COLUMN `current_payment_id` bigint unsigned NULL AFTER `payment_location_code`,
    ADD UNIQUE KEY `ux_appointments_current_payment` (`current_payment_id`),
    ADD CONSTRAINT `fk_appointments_current_payment`
        FOREIGN KEY (`current_payment_id`) REFERENCES `payments` (`id`) ON DELETE RESTRICT;

UPDATE `appointments` a
JOIN `payments` p ON p.`appointment_id` = a.`id`
SET a.`current_payment_id` = p.`id`;

CREATE TABLE `payment_reversals` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `payment_id` bigint unsigned NOT NULL,
    `requested_by_account_id` bigint unsigned NOT NULL,
    `status_code` varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `reason` varchar(500) NOT NULL,
    `idempotency_key` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `provider_reference` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `failure_code` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `requested_at_utc` datetime(6) NOT NULL,
    `completed_at_utc` datetime(6) NULL,
    `row_version` bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_payment_reversals_payment` (`payment_id`),
    UNIQUE KEY `ux_payment_reversals_idempotency` (`requested_by_account_id`, `idempotency_key`),
    KEY `ix_payment_reversals_status_time` (`status_code`, `requested_at_utc`),
    CONSTRAINT `fk_payment_reversals_payment` FOREIGN KEY (`payment_id`) REFERENCES `payments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_payment_reversals_actor` FOREIGN KEY (`requested_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_payment_reversals_status` CHECK (`status_code` IN ('pending','confirmed','failed')),
    CONSTRAINT `ck_payment_reversals_reason` CHECK (CHAR_LENGTH(TRIM(`reason`)) >= 5),
    CONSTRAINT `ck_payment_reversals_completion`
        CHECK ((`status_code` = 'pending' AND `completed_at_utc` IS NULL) OR (`status_code` <> 'pending' AND `completed_at_utc` IS NOT NULL)),
    CONSTRAINT `ck_payment_reversals_row_version` CHECK (`row_version` > 0)
) ENGINE=InnoDB;

CREATE TABLE `payment_reversal_events` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `payment_reversal_id` bigint unsigned NOT NULL,
    `from_status_code` varchar(24) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `to_status_code` varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `source_code` varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `occurred_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    KEY `ix_payment_reversal_events_reversal_time` (`payment_reversal_id`, `occurred_at_utc`),
    CONSTRAINT `fk_payment_reversal_events_reversal` FOREIGN KEY (`payment_reversal_id`) REFERENCES `payment_reversals` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_payment_reversal_events_status` CHECK (`to_status_code` IN ('pending','confirmed','failed')),
    CONSTRAINT `ck_payment_reversal_events_source` CHECK (`source_code` IN ('manual','pagbank','webhook','reconciliation'))
) ENGINE=InnoDB;

CREATE TABLE `cash_movements` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `operational_date` date NOT NULL,
    `direction_code` varchar(12) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `type_code` varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `method_code` varchar(24) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `amount` decimal(13,2) NOT NULL,
    `currency_code` char(3) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'BRL',
    `appointment_id` bigint unsigned NULL,
    `payment_id` bigint unsigned NULL,
    `related_movement_id` bigint unsigned NULL,
    `responsible_account_id` bigint unsigned NULL,
    `description` varchar(240) NOT NULL,
    `reason` varchar(500) NULL,
    `idempotency_key` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `occurred_at_utc` datetime(6) NOT NULL,
    `after_closure` tinyint(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_cash_movements_idempotency` (`idempotency_key`),
    UNIQUE KEY `ux_cash_movements_payment_type` (`payment_id`, `type_code`),
    KEY `ix_cash_movements_operational_date` (`operational_date`, `occurred_at_utc`),
    KEY `ix_cash_movements_method_date` (`method_code`, `operational_date`),
    KEY `ix_cash_movements_appointment` (`appointment_id`, `occurred_at_utc`),
    KEY `ix_cash_movements_responsible` (`responsible_account_id`, `operational_date`),
    CONSTRAINT `fk_cash_movements_appointment` FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_cash_movements_payment` FOREIGN KEY (`payment_id`) REFERENCES `payments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_cash_movements_related` FOREIGN KEY (`related_movement_id`) REFERENCES `cash_movements` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_cash_movements_responsible` FOREIGN KEY (`responsible_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_cash_movements_direction` CHECK (`direction_code` IN ('entry','outflow')),
    CONSTRAINT `ck_cash_movements_type` CHECK (`type_code` IN ('payment_received','payment_reversal','supply','withdrawal','adjustment','provider_fee')),
    CONSTRAINT `ck_cash_movements_method` CHECK (`method_code` IN ('cash','pix','debit_card','credit_card','pagbank_online','other')),
    CONSTRAINT `ck_cash_movements_amount` CHECK (`amount` > 0),
    CONSTRAINT `ck_cash_movements_currency` CHECK (`currency_code` = 'BRL'),
    CONSTRAINT `ck_cash_movements_after_closure` CHECK (`after_closure` IN (0,1)),
    CONSTRAINT `ck_cash_movements_payment_link`
        CHECK ((`type_code` IN ('payment_received','payment_reversal','provider_fee') AND `payment_id` IS NOT NULL AND `appointment_id` IS NOT NULL)
            OR (`type_code` NOT IN ('payment_received','payment_reversal','provider_fee'))),
    CONSTRAINT `ck_cash_movements_related_link`
        CHECK ((`type_code` IN ('payment_reversal','adjustment') AND `related_movement_id` IS NOT NULL)
            OR (`type_code` NOT IN ('payment_reversal','adjustment')))
) ENGINE=InnoDB;

CREATE TABLE `cash_closures` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `operational_date` date NOT NULL,
    `closed_by_account_id` bigint unsigned NOT NULL,
    `last_movement_id` bigint unsigned NULL,
    `gross_entries` decimal(13,2) NOT NULL,
    `payment_reversals` decimal(13,2) NOT NULL,
    `supplies` decimal(13,2) NOT NULL,
    `withdrawals` decimal(13,2) NOT NULL,
    `adjustments_net` decimal(13,2) NOT NULL,
    `net_total` decimal(13,2) NOT NULL,
    `movement_count` int unsigned NOT NULL,
    `totals_by_method_json` json NOT NULL,
    `closed_at_utc` datetime(6) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_cash_closures_date` (`operational_date`),
    KEY `ix_cash_closures_actor_time` (`closed_by_account_id`, `closed_at_utc`),
    CONSTRAINT `fk_cash_closures_actor` FOREIGN KEY (`closed_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_cash_closures_last_movement` FOREIGN KEY (`last_movement_id`) REFERENCES `cash_movements` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_cash_closures_amounts` CHECK (`gross_entries` >= 0 AND `payment_reversals` >= 0 AND `supplies` >= 0 AND `withdrawals` >= 0)
) ENGINE=InnoDB;

INSERT INTO `cash_movements`
    (`operational_date`,`direction_code`,`type_code`,`method_code`,`amount`,`appointment_id`,`payment_id`,
     `responsible_account_id`,`description`,`idempotency_key`,`occurred_at_utc`,`after_closure`)
SELECT DATE(COALESCE(p.`paid_at_utc`, p.`updated_at_utc`)), 'entry', 'payment_received',
       CASE WHEN p.`provider_code` = 'pagbank' THEN 'pagbank_online'
            WHEN p.`method_code` IN ('cash','pix','debit_card','credit_card') THEN p.`method_code` ELSE 'other' END,
       p.`amount`, p.`appointment_id`, p.`id`, p.`confirmed_by_account_id`,
       CONCAT('Pagamento do atendimento ', a.`appointment_number`), CONCAT('backfill-payment-', p.`id`),
       COALESCE(p.`paid_at_utc`, p.`updated_at_utc`), 0
FROM `payments` p
JOIN `appointments` a ON a.`id` = p.`appointment_id`
WHERE p.`status_code` = 'paid';

CREATE TRIGGER `trg_cash_movements_block_update`
BEFORE UPDATE ON `cash_movements`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'cash_movements is append-only';

CREATE TRIGGER `trg_cash_movements_block_delete`
BEFORE DELETE ON `cash_movements`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'cash_movements is append-only';

CREATE TRIGGER `trg_cash_closures_block_update`
BEFORE UPDATE ON `cash_closures`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'cash_closures is append-only';

CREATE TRIGGER `trg_cash_closures_block_delete`
BEFORE DELETE ON `cash_closures`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'cash_closures is append-only';

CREATE TRIGGER `trg_payment_reversal_events_block_update`
BEFORE UPDATE ON `payment_reversal_events`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'payment_reversal_events is append-only';

CREATE TRIGGER `trg_payment_reversal_events_block_delete`
BEFORE DELETE ON `payment_reversal_events`
FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'payment_reversal_events is append-only';
