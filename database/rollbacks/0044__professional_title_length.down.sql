-- Somente viverappweb. Rever títulos antes de reduzir a coluna; MySQL recusará
-- a alteração se houver valor com mais de quatro caracteres.
ALTER TABLE professional_profiles
    MODIFY professional_title varchar(4) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'Dr.';
