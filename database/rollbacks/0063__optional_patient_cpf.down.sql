-- Não remove pacientes cadastrados sem CPF nem altera dados pessoais existentes.
DELETE FROM application_settings WHERE setting_key = 'patients.allow_registration_without_cpf';
