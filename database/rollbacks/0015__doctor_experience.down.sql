-- Rollback manual da Fase 12. Não executar automaticamente.
ALTER TABLE private_documents DROP CHECK ck_private_documents_size;
ALTER TABLE private_documents ADD CONSTRAINT ck_private_documents_size CHECK (size_bytes BETWEEN 1 AND 5242880);
ALTER TABLE appointment_documents
    DROP FOREIGN KEY fk_appointment_documents_deleted_by,
    DROP CHECK ck_appointment_documents_deleted_state,
    DROP CHECK ck_appointment_documents_row_version,
    DROP COLUMN deleted_at_utc,
    DROP COLUMN deleted_by_account_id,
    DROP COLUMN row_version;
DROP TABLE medical_report_versions;
DROP TABLE doctor_patient_links;
DROP TABLE doctor_availability_exceptions;
DROP TABLE doctor_preferences;
DROP TABLE doctor_services;
