-- Somente em recuperacao controlada: remove a autoria individual introduzida nesta versao.
ALTER TABLE medical_report_versions
    DROP FOREIGN KEY fk_medical_report_versions_editor,
    DROP INDEX ix_medical_report_versions_editor,
    DROP COLUMN editor_account_id;
