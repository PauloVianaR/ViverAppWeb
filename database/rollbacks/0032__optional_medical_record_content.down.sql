-- Rollback documentado da Fase 18. Não executar sem autorização explícita.
-- Registros vazios criados após 0032 precisam ser revisados antes de restaurar esta regra antiga.
ALTER TABLE `medical_record_versions`
    ADD CONSTRAINT `ck_medical_record_versions_content` CHECK (
        COALESCE(CHAR_LENGTH(TRIM(`chief_complaint`)), 0)
        + COALESCE(CHAR_LENGTH(TRIM(`clinical_evolution`)), 0)
        + COALESCE(CHAR_LENGTH(TRIM(`conduct_and_guidance`)), 0) >= 20
    );
