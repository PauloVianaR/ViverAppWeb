-- Reversão manual restrita da Fase 25; não executar durante a implantação normal.
ALTER TABLE appointments
    DROP INDEX ix_appointments_start_id;
