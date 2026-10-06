-- Serviços combinados, pagamento presencial misto e desconto pontual.
-- MySQL 8.0.41 / viverappweb. Preserva movimentos financeiros e auditoria append-only.
CREATE TABLE `appointment_service_items` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `appointment_id` bigint unsigned NOT NULL,
    `appointment_type_id` int unsigned NOT NULL,
    `ordinal` tinyint unsigned NOT NULL,
    `name_snapshot` varchar(120) NOT NULL,
    `category_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `duration_minutes` smallint unsigned NOT NULL,
    `base_price_amount` decimal(13,2) NOT NULL,
    `requires_payment` tinyint(1) NOT NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_appointment_service_items_order` (`appointment_id`, `ordinal`),
    UNIQUE KEY `ux_appointment_service_items_type` (`appointment_id`, `appointment_type_id`),
    KEY `ix_appointment_service_items_type` (`appointment_type_id`),
    CONSTRAINT `fk_appointment_service_items_appointment` FOREIGN KEY (`appointment_id`) REFERENCES `appointments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_appointment_service_items_type` FOREIGN KEY (`appointment_type_id`) REFERENCES `appointment_types` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_appointment_service_items_duration` CHECK (`duration_minutes` BETWEEN 5 AND 480),
    CONSTRAINT `ck_appointment_service_items_price` CHECK (`base_price_amount` >= 0),
    CONSTRAINT `ck_appointment_service_items_payment` CHECK (`requires_payment` IN (0,1))
) ENGINE=InnoDB;

INSERT INTO `appointment_service_items`
    (`appointment_id`, `appointment_type_id`, `ordinal`, `name_snapshot`,
     `category_code`, `duration_minutes`, `base_price_amount`, `requires_payment`)
SELECT a.`id`, a.`appointment_type_id`, 1, t.`name`, t.`category_code`,
       t.`duration_minutes`, COALESCE(a.`base_price_amount`, a.`price_amount`), a.`requires_payment`
FROM `appointments` a
JOIN `appointment_types` t ON t.`id` = a.`appointment_type_id`;

ALTER TABLE `appointments`
    ADD COLUMN `point_discount_kind_code` varchar(10) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD COLUMN `point_discount_value` decimal(13,2) NOT NULL DEFAULT 0,
    ADD COLUMN `point_discount_amount` decimal(13,2) NOT NULL DEFAULT 0,
    ADD COLUMN `point_discount_by_account_id` bigint unsigned NULL,
    ADD COLUMN `point_discount_at_utc` datetime(6) NULL,
    ADD KEY `ix_appointments_point_discount_actor` (`point_discount_by_account_id`),
    ADD CONSTRAINT `fk_appointments_point_discount_actor` FOREIGN KEY (`point_discount_by_account_id`) REFERENCES `accounts` (`id`) ON DELETE RESTRICT,
    ADD CONSTRAINT `ck_appointments_point_discount` CHECK (
        (`point_discount_kind_code` IS NULL AND `point_discount_value` = 0 AND `point_discount_amount` = 0
            AND `point_discount_by_account_id` IS NULL AND `point_discount_at_utc` IS NULL)
        OR (`point_discount_kind_code` IN ('percent','amount') AND `point_discount_value` > 0
            AND `point_discount_amount` > 0 AND `point_discount_by_account_id` IS NOT NULL
            AND `point_discount_at_utc` IS NOT NULL));

-- O índice exclusivo antigo permitia somente um lançamento por pagamento/tipo.
-- Cada parcela e sua reversão agora têm idempotência própria, sem apagar o ledger.
ALTER TABLE `cash_movements`
    DROP INDEX `ux_cash_movements_payment_type`,
    ADD KEY `ix_cash_movements_payment_type` (`payment_id`, `type_code`),
    ADD COLUMN `card_last_four` char(4) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD COLUMN `authorization_reference` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL;

INSERT INTO `application_settings`
    (`setting_key`, `value_json`, `description`, `is_secret`, `updated_at_utc`)
VALUES
    ('appointments.point_discount_max_percent', '30',
     'Desconto pontual máximo sobre o valor já reduzido pelo Premium, em porcentagem.', 0, UTC_TIMESTAMP(6)),
    ('manager.point_discounts_enabled', 'true',
     'Permitir ao Gestor conceder descontos pontuais em atendimentos.', 0, UTC_TIMESTAMP(6));
