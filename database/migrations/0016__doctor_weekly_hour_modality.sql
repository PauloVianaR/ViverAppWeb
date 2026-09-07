-- Fase 12. Distingue faixas semanais presenciais e online.
ALTER TABLE doctor_weekly_hours
    DROP INDEX ux_doctor_weekly_hours,
    ADD modality_code varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'both' AFTER day_of_week,
    ADD UNIQUE KEY ux_doctor_weekly_hours (doctor_account_id, day_of_week, modality_code, start_time, end_time, valid_from),
    ADD CONSTRAINT ck_doctor_weekly_hours_modality CHECK (modality_code IN ('in_person', 'online', 'both'));
