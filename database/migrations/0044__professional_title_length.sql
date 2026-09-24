-- Aplicar somente em viverappweb / MySQL 8.0.41. Não modifica viverappmobile.
-- O contrato e o formulário aceitam até 30 caracteres; o schema anterior aceitava apenas 4.
-- Rollback (somente após verificar que todos os títulos cabem em 4 caracteres):
-- ALTER TABLE professional_profiles MODIFY professional_title varchar(4) NOT NULL DEFAULT 'Dr.';
ALTER TABLE professional_profiles
    MODIFY professional_title varchar(30) NOT NULL DEFAULT 'Dr.';
