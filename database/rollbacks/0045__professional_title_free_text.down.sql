-- Somente viverappweb. Execute apenas depois de verificar que todos os títulos
-- são Dr. ou Dra.; caso contrário o CHECK impedirá a reversão, preservando os dados.
ALTER TABLE professional_profiles
    DROP CHECK ck_professional_profiles_title_nonempty,
    MODIFY professional_title varchar(30) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Dr.',
    ADD CONSTRAINT ck_doctor_profiles_title CHECK (professional_title IN ('Dr.', 'Dra.'));
