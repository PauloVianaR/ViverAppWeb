-- Apenas recuperação autorizada. A referência financeira deve ser reconciliada antes de remover esta coluna.
ALTER TABLE premium_memberships DROP FOREIGN KEY fk_premium_memberships_proof_owner,
    DROP INDEX ux_premium_memberships_open, DROP COLUMN open_account_id;
ALTER TABLE private_documents DROP INDEX ux_private_documents_owner;
ALTER TABLE payments DROP FOREIGN KEY fk_payments_provider_reference,
    DROP INDEX ix_payments_provider_reference, DROP COLUMN provider_reference_appointment_id;
-- Não excluir plano Premium: pode estar em uso ou ter sido configurado pelo proprietário.
