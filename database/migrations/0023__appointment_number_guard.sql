-- Garante que nenhum caminho de escrita contorne o intervalo operacional iniciado em 100.
ALTER TABLE `appointments`
    ADD CONSTRAINT `ck_appointments_number` CHECK (`appointment_number` >= 100);
