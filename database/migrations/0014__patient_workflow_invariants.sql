-- Mantém a referência original do PagBank ao reagendar, sem cobrar novamente.
ALTER TABLE payments ADD provider_reference_appointment_id bigint unsigned NULL,
    ADD KEY ix_payments_provider_reference (provider_reference_appointment_id),
    ADD CONSTRAINT fk_payments_provider_reference FOREIGN KEY (provider_reference_appointment_id) REFERENCES appointments(id) ON DELETE RESTRICT;
UPDATE payments SET provider_reference_appointment_id = appointment_id WHERE provider_reference_appointment_id IS NULL;

ALTER TABLE private_documents ADD UNIQUE KEY ux_private_documents_owner (id, owner_account_id);
ALTER TABLE premium_memberships
    ADD open_account_id bigint unsigned GENERATED ALWAYS AS (CASE WHEN status_code IN ('pending','active') THEN account_id ELSE NULL END) STORED,
    ADD UNIQUE KEY ux_premium_memberships_open (open_account_id),
    ADD CONSTRAINT fk_premium_memberships_proof_owner FOREIGN KEY (proof_document_id, account_id) REFERENCES private_documents(id, owner_account_id) ON DELETE RESTRICT;

INSERT INTO premium_plans (name,price_amount,appointment_discount_percent,is_active,created_at_utc,updated_at_utc)
SELECT 'Viver Premium',0,0,1,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6)
WHERE NOT EXISTS (SELECT 1 FROM premium_plans WHERE is_active=1);
-- Desconto inicial 0%: a clínica deve configurar seu percentual antes de prometer um valor.
