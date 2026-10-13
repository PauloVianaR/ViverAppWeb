-- Permite horários anteriores SOMENTE no dia atual da clínica.
-- Não remove validações de disponibilidade, conflitos, feriados e limites.
INSERT INTO application_settings
    (setting_key, value_json, description, is_secret, updated_at_utc, row_version)
VALUES ('appointments.allow_elapsed_same_day_booking', 'true',
    'Permite agendar em horários já passados do dia atual, respeitando a disponibilidade da clínica e do profissional. Não permite dias anteriores.',
    0, UTC_TIMESTAMP(6), 1);
