-- Fase 17. Eventos explícitos de solicitação e conclusão de reversão.
ALTER TABLE `payment_events` DROP CHECK `ck_payment_events_source`;
ALTER TABLE `payment_events` ADD CONSTRAINT `ck_payment_events_source`
    CHECK (`source_code` IN ('checkout','webhook','reconciliation','refund','manual','reversal'));
