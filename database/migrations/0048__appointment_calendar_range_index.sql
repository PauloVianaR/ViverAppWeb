-- Fase 25. Calendário da clínica por intervalo; aplicar apenas em viverappweb / MySQL 8.0.41.
ALTER TABLE appointments
    ADD KEY ix_appointments_start_id (starts_at_utc, id);
