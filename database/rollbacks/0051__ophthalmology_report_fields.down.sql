-- Uso somente em recuperacao controlada antes de novos registros: descarta os seis campos novos.
-- O resumo legado permanece intacto; nao executar sobre dados clinicos preenchidos posteriormente.
ALTER TABLE medical_report_versions
    DROP COLUMN ophthalmic_history,
    DROP COLUMN visual_acuity,
    DROP COLUMN refraction,
    DROP COLUMN biomicroscopy,
    DROP COLUMN tonometry,
    DROP COLUMN fundus_exam;

ALTER TABLE medical_reports
    DROP COLUMN ophthalmic_history,
    DROP COLUMN visual_acuity,
    DROP COLUMN refraction,
    DROP COLUMN biomicroscopy,
    DROP COLUMN tonometry,
    DROP COLUMN fundus_exam;
