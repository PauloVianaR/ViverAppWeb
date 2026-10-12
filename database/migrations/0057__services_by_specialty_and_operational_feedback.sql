-- Catálogo por especialidade: vínculos individuais antigos deixam de ser autoridade.
SET @ophthalmology = (SELECT id FROM specialties WHERE normalized_name = 'OFTALMOLOGIA' LIMIT 1);
SET @psychology = (SELECT id FROM specialties WHERE normalized_name = 'PSICOLOGIA' LIMIT 1);
ALTER TABLE appointment_types ADD COLUMN specialty_id INT UNSIGNED NULL;
UPDATE appointment_types SET specialty_id = CASE
    WHEN name COLLATE utf8mb4_0900_ai_ci LIKE '%consulta psicol%' THEN @psychology
    ELSE @ophthalmology END;
-- Default preserva compatibilidade com cargas antigas; os contratos validam a especialidade ativa.
SET @catalog_alter = CONCAT('ALTER TABLE appointment_types MODIFY specialty_id INT UNSIGNED NOT NULL DEFAULT ',
    @ophthalmology, ', ADD KEY ix_appointment_types_specialty (specialty_id, is_active), ',
    'ADD CONSTRAINT fk_appointment_types_specialty FOREIGN KEY (specialty_id) REFERENCES specialties(id)');
PREPARE catalog_alter FROM @catalog_alter;
EXECUTE catalog_alter;
DEALLOCATE PREPARE catalog_alter;

-- Avaliação da equipe é distinta da avaliação enviada pelo paciente.
CREATE TABLE appointment_operational_reviews (
    appointment_id BIGINT UNSIGNED NOT NULL,
    rating TINYINT UNSIGNED NOT NULL,
    feedback VARCHAR(2000) NULL,
    updated_by_account_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    row_version BIGINT UNSIGNED NOT NULL,
    PRIMARY KEY (appointment_id),
    CONSTRAINT fk_operational_review_appointment FOREIGN KEY (appointment_id) REFERENCES appointments(id) ON DELETE CASCADE,
    CONSTRAINT fk_operational_review_editor FOREIGN KEY (updated_by_account_id) REFERENCES accounts(id) ON DELETE SET NULL,
    CONSTRAINT ck_operational_review_rating CHECK (rating BETWEEN 1 AND 5),
    CONSTRAINT ck_operational_review_version CHECK (row_version > 0)
) ENGINE=InnoDB;
CREATE TABLE appointment_operational_review_versions (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    appointment_id BIGINT UNSIGNED NOT NULL,
    version_number BIGINT UNSIGNED NOT NULL,
    rating TINYINT UNSIGNED NOT NULL,
    feedback VARCHAR(2000) NULL,
    editor_account_id BIGINT UNSIGNED NULL,
    editor_role_code VARCHAR(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_operational_review_version (appointment_id, version_number),
    CONSTRAINT fk_operational_version_appointment FOREIGN KEY (appointment_id) REFERENCES appointments(id) ON DELETE CASCADE,
    CONSTRAINT fk_operational_version_editor FOREIGN KEY (editor_account_id) REFERENCES accounts(id) ON DELETE SET NULL,
    CONSTRAINT ck_operational_version_rating CHECK (rating BETWEEN 1 AND 5),
    CONSTRAINT ck_operational_version_role CHECK (editor_role_code IN ('doctor','psychologist','manager','administrator')),
    CONSTRAINT ck_operational_version_number CHECK (version_number > 0)
) ENGINE=InnoDB;
