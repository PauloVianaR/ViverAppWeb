-- Aplicar somente em viverappweb / MySQL 8.0.41. Não modifica viverappmobile.
-- O antigo CHECK aceitava apenas Dr./Dra. e a coluna era ASCII, bloqueando
-- títulos como Psicóloga ou Especialista apesar do formulário aceitar texto.
-- Rollback exige primeiro revisar os títulos fora de Dr./Dra. e só então
-- recriar o CHECK antigo; não é seguro aplicá-lo automaticamente.
ALTER TABLE professional_profiles
    DROP CHECK ck_doctor_profiles_title,
    MODIFY professional_title varchar(30) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL DEFAULT 'Dr.',
    ADD CONSTRAINT ck_professional_profiles_title_nonempty
        CHECK (CHAR_LENGTH(TRIM(professional_title)) BETWEEN 1 AND 30);
