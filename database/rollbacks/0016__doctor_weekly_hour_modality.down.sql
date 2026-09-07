-- Rollback manual da Fase 12. Não executar automaticamente.
ALTER TABLE doctor_weekly_hours
    DROP CHECK ck_doctor_weekly_hours_modality,
    DROP INDEX ux_doctor_weekly_hours,
    DROP COLUMN modality_code,
    ADD UNIQUE KEY ux_doctor_weekly_hours (doctor_account_id, day_of_week, start_time, end_time, valid_from);
