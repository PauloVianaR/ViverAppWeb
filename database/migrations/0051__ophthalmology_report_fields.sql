-- Campos estruturados do laudo oftalmologico; preserva o resumo legado e todas as versoes.
-- MySQL 8.0.41 / viverappweb. A especialidade e verificada pelo vinculo profissional.
ALTER TABLE medical_reports
    ADD COLUMN ophthalmic_history text NULL,
    ADD COLUMN visual_acuity text NULL,
    ADD COLUMN refraction text NULL,
    ADD COLUMN biomicroscopy text NULL,
    ADD COLUMN tonometry text NULL,
    ADD COLUMN fundus_exam text NULL;

ALTER TABLE medical_report_versions
    ADD COLUMN ophthalmic_history text NULL,
    ADD COLUMN visual_acuity text NULL,
    ADD COLUMN refraction text NULL,
    ADD COLUMN biomicroscopy text NULL,
    ADD COLUMN tonometry text NULL,
    ADD COLUMN fundus_exam text NULL;

UPDATE medical_reports AS report
SET ophthalmic_history = clinical_summary
WHERE EXISTS (
    SELECT 1 FROM appointments AS appointment
    JOIN professional_specialties AS link ON link.professional_account_id = appointment.professional_account_id
    JOIN specialties AS specialty ON specialty.id = link.specialty_id
    WHERE appointment.id = report.appointment_id
      AND specialty.normalized_name = 'OFTALMOLOGIA'
);

UPDATE medical_report_versions AS version
JOIN medical_reports AS report ON report.id = version.medical_report_id
SET version.ophthalmic_history = version.clinical_summary
WHERE EXISTS (
    SELECT 1 FROM appointments AS appointment
    JOIN professional_specialties AS link ON link.professional_account_id = appointment.professional_account_id
    JOIN specialties AS specialty ON specialty.id = link.specialty_id
    WHERE appointment.id = report.appointment_id
      AND specialty.normalized_name = 'OFTALMOLOGIA'
);
