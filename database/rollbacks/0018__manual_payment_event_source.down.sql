-- Rollback manual da Fase 13. Não executar automaticamente enquanto houver eventos manuais.
ALTER TABLE payment_events DROP CHECK ck_payment_events_source;
ALTER TABLE payment_events ADD CONSTRAINT ck_payment_events_source
    CHECK (source_code IN ('checkout','webhook','reconciliation','refund'));
