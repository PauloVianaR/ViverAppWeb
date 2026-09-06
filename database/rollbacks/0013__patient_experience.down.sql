-- Somente recuperação autorizada, com backup e confirmação de que as tabelas novas não contêm dados.
-- Não executar como ensaio. Pode remover dados da fase 11. Nunca executar no legado.
DROP TABLE teleconsultation_peers;
DROP TABLE contact_change_requests;
ALTER TABLE account_challenges DROP CHECK ck_account_challenges_purpose;
ALTER TABLE account_challenges ADD CONSTRAINT ck_account_challenges_purpose CHECK (purpose_code IN ('login','password_reset','contact_verification'));
ALTER TABLE premium_memberships DROP FOREIGN KEY fk_premium_memberships_proof,
    DROP COLUMN proof_document_id, DROP COLUMN rejection_reason, DROP COLUMN reviewed_at_utc, DROP COLUMN row_version;
DROP TABLE private_documents;
DROP TABLE appointment_reviews;
DROP TABLE patient_preferences;
ALTER TABLE payments DROP COLUMN method_code;
ALTER TABLE appointments DROP CHECK ck_appointments_discount, DROP CHECK ck_appointments_payment_location,
    DROP COLUMN base_price_amount, DROP COLUMN discount_percent, DROP COLUMN payment_location_code;
ALTER TABLE appointment_types DROP CHECK ck_appointment_types_category, DROP COLUMN category_code;
-- Settings preservados para não remover valores que possam ter sido configurados pelo proprietário.
