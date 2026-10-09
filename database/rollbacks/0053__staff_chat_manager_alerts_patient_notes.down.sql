-- Somente para recuperação controlada, com backup verificado.
-- Remove mensagens e alertas desta versão; nunca executar na rotina de deploy.
DROP TABLE manager_notifications;
DROP TABLE staff_messages;
ALTER TABLE patient_profiles DROP COLUMN administrative_notes;
