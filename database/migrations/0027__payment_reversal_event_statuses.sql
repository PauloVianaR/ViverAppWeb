-- Fase 17. Estados normalizados da trilha de reversão.
ALTER TABLE `payment_events` DROP CHECK `ck_payment_events_status`;
ALTER TABLE `payment_events` ADD CONSTRAINT `ck_payment_events_status`
    CHECK (`normalized_status_code` IN ('pending','authorized','paid','failed','canceled','reversal_pending','reversed','refunded'));
