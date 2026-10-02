-- Correções produção vS1: manter a auditoria append-only e aceitar o papel clínico psychologist.
-- MySQL 8.0.41; nenhuma linha de clinical_access_events é alterada ou removida.
ALTER TABLE `clinical_access_events`
    DROP CHECK `ck_clinical_access_events_role`,
    DROP CHECK `ck_clinical_access_events_purpose`,
    ADD CONSTRAINT `ck_clinical_access_events_role`
        CHECK (`actor_role_code` IN ('doctor','psychologist','manager','administrator')),
    ADD CONSTRAINT `ck_clinical_access_events_purpose`
        CHECK (`actor_role_code` IN ('doctor','psychologist')
            OR CHAR_LENGTH(TRIM(`purpose`)) BETWEEN 10 AND 500);
