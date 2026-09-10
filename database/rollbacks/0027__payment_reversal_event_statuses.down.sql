ALTER TABLE `payment_events` DROP CHECK `ck_payment_events_status`;
ALTER TABLE `payment_events` ADD CONSTRAINT `ck_payment_events_status`
    CHECK (`normalized_status_code` IN ('pending','authorized','paid','failed','canceled','refunded'));
