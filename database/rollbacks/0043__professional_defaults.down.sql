-- Plano documental. Restaurar o padrão da coluna sem alterar perfis existentes.
ALTER TABLE professional_profiles
    MODIFY default_appointment_duration_minutes smallint unsigned NOT NULL DEFAULT 30;

-- Não reverter automaticamente o controle de agendamento: o administrador pode tê-lo alterado depois.
