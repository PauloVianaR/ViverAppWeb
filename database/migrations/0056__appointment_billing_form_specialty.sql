-- A forma pertence ao atendimento, sem modificar o preço/cobrança do catálogo.
ALTER TABLE appointments
    ADD COLUMN billing_form_code VARCHAR(20) NOT NULL DEFAULT 'private',
    ADD COLUMN specialty_id INT UNSIGNED NULL,
    ADD CONSTRAINT ck_appointments_billing_form CHECK (billing_form_code IN ('private', 'insurance')),
    ADD CONSTRAINT ck_appointments_insurance CHECK (billing_form_code <> 'insurance' OR (requires_payment = 0 AND price_amount = 0)),
    ADD CONSTRAINT fk_appointments_specialty FOREIGN KEY (specialty_id) REFERENCES specialties(id);

START TRANSACTION;
-- Preservar a interpretação oftalmológica usada pelos laudos anteriores.
UPDATE appointments a
SET specialty_id = (
    SELECT ps.specialty_id FROM professional_specialties ps
    JOIN specialties s ON s.id = ps.specialty_id
    WHERE ps.professional_account_id = a.professional_account_id
    ORDER BY (s.normalized_name = 'OFTALMOLOGIA') DESC, ps.is_primary DESC, ps.specialty_id
    LIMIT 1
);
INSERT INTO application_settings (setting_key, value_json, description, is_secret, updated_at_utc, row_version)
SELECT 'appointments.default_specialty_id', CAST(id AS JSON),
    'Especialidade padrão do agendamento (ID do catálogo de especialidades)', 0, UTC_TIMESTAMP(6), 1
FROM specialties WHERE normalized_name = 'OFTALMOLOGIA' AND is_active = 1 LIMIT 1;
COMMIT;
