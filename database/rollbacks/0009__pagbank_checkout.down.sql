-- Rollback da integração PagBank Checkout — executar somente em ambiente controlado.

DROP TABLE `payment_events`;
DROP TABLE `payment_webhook_receipts`;

ALTER TABLE `payments`
    DROP CONSTRAINT `ck_payments_refund_amount`,
    DROP CONSTRAINT `ck_payments_refund`,
    DROP CONSTRAINT `ck_payments_checkout_url`,
    DROP INDEX `ix_payments_reconciliation`,
    DROP COLUMN `refunded_at_utc`,
    DROP COLUMN `refund_amount`,
    DROP COLUMN `reconciliation_attempt_count`,
    DROP COLUMN `next_reconciliation_at_utc`,
    DROP COLUMN `last_reconciled_at_utc`,
    DROP COLUMN `provider_event_at_utc`,
    DROP COLUMN `checkout_expires_at_utc`,
    DROP COLUMN `checkout_url`;

