-- Rollback documentado. Não executar se houver reaberturas registradas.

DELETE FROM `application_settings`
WHERE `setting_key` IN ('cash.manager_can_reopen', 'cash.manager_can_view_cumulative_totals');

DROP TRIGGER `trg_cash_reopenings_block_delete`;
DROP TRIGGER `trg_cash_reopenings_block_update`;
DROP TABLE `cash_reopenings`;

ALTER TABLE `cash_closures`
    DROP INDEX `ix_cash_closures_date_time`,
    ADD UNIQUE KEY `ux_cash_closures_date` (`operational_date`);
