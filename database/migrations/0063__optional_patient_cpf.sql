-- Apenas pacientes podem dispensar CPF. Valor informado continua sujeito à validação e unicidade.
INSERT INTO application_settings
    (setting_key, value_json, description, is_secret, updated_at_utc, row_version)
VALUES ('patients.allow_registration_without_cpf', 'false',
    'Permitir cadastrar pacientes sem CPF. Aplica-se ao cadastro interno e ao portal, inclusive Google. CPF informado deve ser válido e único; profissionais e gestores continuam obrigados a informá-lo.',
    0, UTC_TIMESTAMP(6), 1);
