-- Rollback documental da Fase 17. Não executar em ambiente com fatos financeiros sem autorização explícita.
DROP TRIGGER IF EXISTS `trg_payment_reversal_events_block_delete`;
DROP TRIGGER IF EXISTS `trg_payment_reversal_events_block_update`;
DROP TRIGGER IF EXISTS `trg_cash_closures_block_delete`;
DROP TRIGGER IF EXISTS `trg_cash_closures_block_update`;
DROP TRIGGER IF EXISTS `trg_cash_movements_block_delete`;
DROP TRIGGER IF EXISTS `trg_cash_movements_block_update`;
DROP TABLE IF EXISTS `cash_closures`;
DROP TABLE IF EXISTS `cash_movements`;
DROP TABLE IF EXISTS `payment_reversal_events`;
DROP TABLE IF EXISTS `payment_reversals`;
ALTER TABLE `appointments` DROP FOREIGN KEY `fk_appointments_current_payment`, DROP INDEX `ux_appointments_current_payment`, DROP COLUMN `current_payment_id`;
ALTER TABLE `payments`
    DROP FOREIGN KEY `fk_payments_supersedes`,
    DROP FOREIGN KEY `fk_payments_reversed_by`,
    DROP CHECK `ck_payments_reversal`,
    DROP CHECK `ck_payments_refund`,
    DROP CHECK `ck_payments_status`,
    DROP INDEX `ux_payments_active_appointment`,
    DROP INDEX `ux_payments_superseded_once`,
    DROP INDEX `ix_payments_appointment_created`,
    DROP INDEX `ix_payments_reversed_by`,
    DROP COLUMN `active_appointment_id`,
    DROP COLUMN `reversed_by_account_id`,
    DROP COLUMN `reversal_requested_at_utc`,
    DROP COLUMN `reversal_reason`,
    DROP COLUMN `supersedes_payment_id`,
    ADD UNIQUE KEY `ux_payments_appointment` (`appointment_id`),
    ADD CONSTRAINT `ck_payments_status` CHECK (`status_code` IN ('pending','authorized','paid','failed','canceled','refunded')),
    ADD CONSTRAINT `ck_payments_refund`
        CHECK ((`status_code` = 'refunded' AND `refund_amount` IS NOT NULL AND `refund_amount` > 0 AND `refunded_at_utc` IS NOT NULL)
            OR (`status_code` <> 'refunded' AND `refund_amount` IS NULL AND `refunded_at_utc` IS NULL));
