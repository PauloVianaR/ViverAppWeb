-- PagBank Checkout, trilha financeira e proteção contra replay — MySQL 8.0.41.

ALTER TABLE `payments`
    ADD COLUMN `checkout_url` varchar(500) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `provider_checkout_id`,
    ADD COLUMN `checkout_expires_at_utc` datetime(6) NULL AFTER `checkout_url`,
    ADD COLUMN `provider_event_at_utc` datetime(6) NULL AFTER `provider_status_code`,
    ADD COLUMN `last_reconciled_at_utc` datetime(6) NULL AFTER `provider_event_at_utc`,
    ADD COLUMN `next_reconciliation_at_utc` datetime(6) NULL AFTER `last_reconciled_at_utc`,
    ADD COLUMN `reconciliation_attempt_count` smallint unsigned NOT NULL DEFAULT 0 AFTER `next_reconciliation_at_utc`,
    ADD COLUMN `refund_amount` decimal(13,2) NULL AFTER `canceled_at_utc`,
    ADD COLUMN `refunded_at_utc` datetime(6) NULL AFTER `refund_amount`,
    ADD KEY `ix_payments_reconciliation` (`status_code`, `next_reconciliation_at_utc`),
    ADD CONSTRAINT `ck_payments_checkout_url`
        CHECK (`checkout_url` IS NULL OR `checkout_url` LIKE 'https://%'),
    ADD CONSTRAINT `ck_payments_refund`
        CHECK (
            (`status_code` = 'refunded' AND `refund_amount` IS NOT NULL AND `refund_amount` > 0 AND `refunded_at_utc` IS NOT NULL)
            OR
            (`status_code` <> 'refunded' AND `refund_amount` IS NULL AND `refunded_at_utc` IS NULL)
        ),
    ADD CONSTRAINT `ck_payments_refund_amount`
        CHECK (`refund_amount` IS NULL OR `refund_amount` <= `amount`);

CREATE TABLE `payment_webhook_receipts` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `provider_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `payload_sha256` binary(32) NOT NULL,
    `authenticity_sha256` binary(32) NOT NULL,
    `provider_resource_id` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `processing_status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'received',
    `result_code` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `received_at_utc` datetime(6) NOT NULL,
    `processed_at_utc` datetime(6) NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_payment_webhook_receipts_payload` (`provider_code`, `payload_sha256`),
    KEY `ix_payment_webhook_receipts_status_time` (`processing_status_code`, `received_at_utc`),
    CONSTRAINT `ck_payment_webhook_receipts_provider` CHECK (`provider_code` = 'pagbank'),
    CONSTRAINT `ck_payment_webhook_receipts_status`
        CHECK (`processing_status_code` IN ('received', 'processed', 'ignored', 'failed')),
    CONSTRAINT `ck_payment_webhook_receipts_processed`
        CHECK (
            (`processing_status_code` = 'received' AND `processed_at_utc` IS NULL)
            OR
            (`processing_status_code` <> 'received' AND `processed_at_utc` IS NOT NULL)
        )
) ENGINE=InnoDB;

CREATE TABLE `payment_events` (
    `id` bigint unsigned NOT NULL AUTO_INCREMENT,
    `payment_id` bigint unsigned NOT NULL,
    `webhook_receipt_id` bigint unsigned NULL,
    `source_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `provider_resource_id` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `provider_status_code` varchar(50) CHARACTER SET ascii COLLATE ascii_bin NULL,
    `normalized_status_code` varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `event_fingerprint` binary(32) NOT NULL,
    `provider_occurred_at_utc` datetime(6) NULL,
    `occurred_at_utc` datetime(6) NOT NULL,
    `was_applied` tinyint(1) NOT NULL,
    `ignored_reason_code` varchar(100) CHARACTER SET ascii COLLATE ascii_bin NULL,
    PRIMARY KEY (`id`),
    UNIQUE KEY `ux_payment_events_fingerprint` (`payment_id`, `event_fingerprint`),
    UNIQUE KEY `ux_payment_events_webhook_receipt` (`webhook_receipt_id`),
    KEY `ix_payment_events_payment_time` (`payment_id`, `occurred_at_utc`),
    CONSTRAINT `fk_payment_events_payment`
        FOREIGN KEY (`payment_id`) REFERENCES `payments` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `fk_payment_events_webhook_receipt`
        FOREIGN KEY (`webhook_receipt_id`) REFERENCES `payment_webhook_receipts` (`id`) ON DELETE RESTRICT,
    CONSTRAINT `ck_payment_events_source`
        CHECK (`source_code` IN ('checkout', 'webhook', 'reconciliation', 'refund')),
    CONSTRAINT `ck_payment_events_status`
        CHECK (`normalized_status_code` IN ('pending', 'authorized', 'paid', 'failed', 'canceled', 'refunded')),
    CONSTRAINT `ck_payment_events_applied` CHECK (`was_applied` IN (0, 1)),
    CONSTRAINT `ck_payment_events_ignored_reason`
        CHECK ((`was_applied` = 1 AND `ignored_reason_code` IS NULL) OR `was_applied` = 0)
) ENGINE=InnoDB;

