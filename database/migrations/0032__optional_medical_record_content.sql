-- Fase 18. Conteúdo clínico opcional; o atendimento permanece o vínculo obrigatório.

ALTER TABLE `medical_record_versions`
    DROP CHECK `ck_medical_record_versions_content`;
