-- A disponibilidade ignora atendimentos encerrados; eles nao reservam o horario.
-- Mantem a unicidade para reservas ativas sem apagar cancelamentos ou historico.
-- MySQL 8.0.41: a expressao funcional retorna NULL para os estados nao ativos.
ALTER TABLE appointments
    DROP INDEX ux_appointments_doctor_start,
    DROP INDEX ux_appointments_patient_start,
    ADD UNIQUE INDEX ux_appointments_doctor_start (
        professional_account_id, starts_at_utc,
        (CASE WHEN status_code IN ('pending','confirmed','arrived','in_progress') THEN 1 ELSE NULL END)),
    ADD UNIQUE INDEX ux_appointments_patient_start (
        patient_account_id, starts_at_utc,
        (CASE WHEN status_code IN ('pending','confirmed','arrived','in_progress') THEN 1 ELSE NULL END));
