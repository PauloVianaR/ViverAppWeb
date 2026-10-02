-- Reversão manual, somente com autorização e análise de dados.
-- Se houver eventos append-only do Psicólogo, este ALTER deve falhar; nunca apagar,
-- reescrever ou anonimizar a auditoria para forçar o retorno ao schema anterior.
ALTER TABLE `clinical_access_events`
    DROP CHECK `ck_clinical_access_events_role`,
    DROP CHECK `ck_clinical_access_events_purpose`,
    ADD CONSTRAINT `ck_clinical_access_events_role`
        CHECK (`actor_role_code` IN ('doctor','manager','administrator')),
    ADD CONSTRAINT `ck_clinical_access_events_purpose`
        CHECK (`actor_role_code` = 'doctor'
            OR CHAR_LENGTH(TRIM(`purpose`)) BETWEEN 10 AND 500);
