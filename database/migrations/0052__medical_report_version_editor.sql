-- A assinatura profissional original permanece; cada versao identifica quem editou de fato.
ALTER TABLE medical_report_versions
    ADD COLUMN editor_account_id bigint unsigned NULL;

UPDATE medical_report_versions
SET editor_account_id = author_professional_account_id;

ALTER TABLE medical_report_versions
    MODIFY COLUMN editor_account_id bigint unsigned NOT NULL,
    ADD KEY ix_medical_report_versions_editor (editor_account_id, created_at_utc),
    ADD CONSTRAINT fk_medical_report_versions_editor
        FOREIGN KEY (editor_account_id) REFERENCES accounts (id) ON DELETE RESTRICT;
