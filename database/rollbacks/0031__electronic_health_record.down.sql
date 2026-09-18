-- Rollback documentado da Fase 18. Não executar sem autorização explícita e plano de retenção clínica.
DROP TRIGGER IF EXISTS `trg_clinical_access_events_block_delete`;
DROP TRIGGER IF EXISTS `trg_clinical_access_events_block_update`;
DROP TRIGGER IF EXISTS `trg_medical_record_versions_block_delete`;
DROP TRIGGER IF EXISTS `trg_medical_record_versions_block_update`;
DROP TABLE IF EXISTS `clinical_access_events`;
DROP TABLE IF EXISTS `medical_record_documents`;
ALTER TABLE `medical_record_entries` DROP FOREIGN KEY `fk_medical_record_entries_current_version`;
DROP TABLE IF EXISTS `medical_record_versions`;
DROP TABLE IF EXISTS `medical_record_entries`;
DROP TABLE IF EXISTS `medical_record_drafts`;
DROP TABLE IF EXISTS `electronic_health_records`;
