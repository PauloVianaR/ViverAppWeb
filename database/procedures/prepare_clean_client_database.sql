-- CORTE ALFA -> CLIENTE. Procedimento avulso: NÃO é migration; NÃO instalar ou executar agora.
-- MySQL 8.0.41. Origem: cópia integral/arquivada do alfa em viverappweb_alpha.
-- Destino: viverappweb NOVO, criado por todas as migrations versionadas e sem dados operacionais.
-- Não apaga nem altera a origem. Não desativa triggers, FKs ou registros append-only.
-- Prévia futura: CALL sp_prepare_clean_client_database(FALSE);
-- Execução futura, após backup, revisão e autorização específica: CALL sp_prepare_clean_client_database(TRUE);
-- Este arquivo deve ser revisto sempre que o schema ganhar uma migration nova.

DELIMITER $$
CREATE PROCEDURE sp_prepare_clean_client_database(IN p_apply BOOLEAN)
BEGIN
    DECLARE v_admin_id BIGINT UNSIGNED;
    DECLARE v_admin_count INT DEFAULT 0;
    DECLARE v_source_migrations INT DEFAULT 0;
    DECLARE v_target_migrations INT DEFAULT 0;

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    IF DATABASE() <> 'viverappweb' OR VERSION() NOT LIKE '8.0.41%' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Requer viverappweb em MySQL 8.0.41.';
    END IF;
    IF (SELECT COUNT(*) FROM information_schema.schemata
        WHERE schema_name = 'viverappweb_alpha') <> 1 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Origem alfa arquivada nao encontrada.';
    END IF;
    SELECT COUNT(*), MIN(id) INTO v_admin_count, v_admin_id
    FROM viverappweb_alpha.accounts
    WHERE role_code = 'administrator' AND status_code = 'active';
    IF v_admin_count <> 1 OR
       (SELECT COUNT(*) FROM viverappweb_alpha.accounts WHERE role_code = 'administrator') <> 1 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'A origem deve possuir exatamente um administrador ativo.';
    END IF;
    IF (SELECT COUNT(*) FROM viverappweb_alpha.clinic) <> 1
        OR (SELECT COUNT(*) FROM viverappweb.clinic) <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Origem sem clinica ou destino ja configurado.';
    END IF;
    SELECT COUNT(*) INTO v_source_migrations FROM viverappweb_alpha.__schema_migrations;
    SELECT COUNT(*) INTO v_target_migrations FROM viverappweb.__schema_migrations;
    IF v_source_migrations <> v_target_migrations OR EXISTS (
        SELECT 1 FROM viverappweb.__schema_migrations target
        LEFT JOIN viverappweb_alpha.__schema_migrations source
          ON source.migration_id = target.migration_id
        WHERE source.migration_id IS NULL OR source.sha256 <> target.sha256
    ) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Migrations de origem e destino nao coincidem.';
    END IF;
    IF (SELECT COUNT(*) FROM viverappweb.accounts) <> 0
        OR (SELECT COUNT(*) FROM viverappweb.appointments) <> 0
        OR (SELECT COUNT(*) FROM viverappweb.appointment_types) <> 0
        OR (SELECT COUNT(*) FROM viverappweb.payments) <> 0
        OR (SELECT COUNT(*) FROM viverappweb.cash_movements) <> 0
        OR (SELECT COUNT(*) FROM viverappweb.cash_closures) <> 0
        OR (SELECT COUNT(*) FROM viverappweb.audit_events) <> 0
        OR (SELECT COUNT(*) FROM viverappweb.medical_record_versions) <> 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Destino nao esta vazio; nao usar sobre dados existentes.';
    END IF;
    IF (SELECT next_value FROM viverappweb.appointment_number_sequence WHERE sequence_key = 1) <> 100 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Sequencia de atendimentos nao inicia em 100.';
    END IF;
    IF p_apply IS NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Informe TRUE ou FALSE.';
    END IF;
    IF p_apply = FALSE THEN
        SELECT v_admin_id AS administrador_origem,
               1 AS administrador_destino,
               (SELECT COUNT(*) FROM viverappweb_alpha.application_settings) AS configuracoes,
               (SELECT COUNT(*) FROM viverappweb_alpha.clinic_weekly_hours) AS horarios_clinica,
               (SELECT COUNT(*) FROM viverappweb_alpha.holidays) AS feriados,
               (SELECT COUNT(*) FROM viverappweb_alpha.premium_plans) AS planos_premium,
               'PREVIA: nenhuma linha foi alterada' AS resultado;
    ELSE
        START TRANSACTION;

        -- O target é novo; apenas substituímos as configurações iniciais das migrations.
        DELETE FROM viverappweb.application_settings;
        DELETE FROM viverappweb.clinic_weekly_hours;
        DELETE FROM viverappweb.holidays;
        DELETE FROM viverappweb.premium_plans;
        DELETE FROM viverappweb.specialties;

        INSERT INTO viverappweb.clinic
            (singleton_id, legal_name, display_name, tax_id, email, phone_e164,
             postal_code, street, number, complement, district, city, state_code,
             timezone_name, created_at_utc, updated_at_utc, row_version)
        SELECT singleton_id, legal_name, display_name, tax_id, email, phone_e164,
               postal_code, street, number, complement, district, city, state_code,
               timezone_name, created_at_utc, updated_at_utc, row_version
        FROM viverappweb_alpha.clinic;

        -- Remapear o único administrador para ID 1 reinicia a sequência de contas em 2.
        INSERT INTO viverappweb.accounts
            (id, role_code, status_code, full_name, email, normalized_email,
             phone_e164, tax_id, birth_date, password_hash, email_verified,
             phone_verified, portal_access_enabled, preferred_recovery_channel,
             security_stamp, failed_login_count, lockout_end_utc, last_login_at_utc,
             created_at_utc, updated_at_utc, row_version)
        SELECT 1, role_code, status_code, full_name, email, normalized_email,
               phone_e164, tax_id, birth_date, password_hash, email_verified,
               phone_verified, portal_access_enabled, preferred_recovery_channel,
               security_stamp, 0, NULL, NULL, created_at_utc, UTC_TIMESTAMP(6), 1
        FROM viverappweb_alpha.accounts WHERE id = v_admin_id;

        INSERT INTO viverappweb.account_authenticators
            (account_id, protected_key, is_enabled, created_at_utc, updated_at_utc, enabled_at_utc)
        SELECT 1, protected_key, is_enabled, created_at_utc, updated_at_utc, enabled_at_utc
        FROM viverappweb_alpha.account_authenticators WHERE account_id = v_admin_id;
        INSERT INTO viverappweb.account_recovery_codes
            (id, account_id, code_hash, created_at_utc, used_at_utc)
        SELECT id, 1, code_hash, created_at_utc, used_at_utc
        FROM viverappweb_alpha.account_recovery_codes WHERE account_id = v_admin_id;
        INSERT INTO viverappweb.external_logins
            (provider_code, provider_subject, account_id, provider_email,
             provider_email_verified, linked_at_utc, last_used_at_utc)
        SELECT provider_code, provider_subject, 1, provider_email,
               provider_email_verified, linked_at_utc, NULL
        FROM viverappweb_alpha.external_logins WHERE account_id = v_admin_id;
        INSERT INTO viverappweb.account_addresses
            (account_id, postal_code, street, number, complement, district,
             city, state_code, updated_at_utc)
        SELECT 1, postal_code, street, number, complement, district,
               city, state_code, updated_at_utc
        FROM viverappweb_alpha.account_addresses WHERE account_id = v_admin_id;
        INSERT INTO viverappweb.account_consents
            (account_id, terms_version, privacy_version, accepted_at_utc, source_code)
        SELECT 1, terms_version, privacy_version, accepted_at_utc, source_code
        FROM viverappweb_alpha.account_consents WHERE account_id = v_admin_id;
        INSERT INTO viverappweb.account_ui_preferences
            (account_id, appointment_view_mode, calendar_view_mode,
             desktop_sidebar_collapsed, updated_at_utc, row_version)
        SELECT 1, appointment_view_mode, calendar_view_mode,
               desktop_sidebar_collapsed, updated_at_utc, row_version
        FROM viverappweb_alpha.account_ui_preferences WHERE account_id = v_admin_id;
        INSERT INTO viverappweb.notification_preferences
            (account_id, reminder_email_enabled, reminder_sms_enabled,
             premium_updates_enabled, updated_at_utc, row_version)
        SELECT 1, reminder_email_enabled, reminder_sms_enabled,
               premium_updates_enabled, updated_at_utc, row_version
        FROM viverappweb_alpha.notification_preferences WHERE account_id = v_admin_id;

        INSERT INTO viverappweb.application_settings
            (setting_key, value_json, description, is_secret, updated_at_utc,
             updated_by_account_id, row_version)
        SELECT setting_key, value_json, description, is_secret, updated_at_utc,
               IF(updated_by_account_id = v_admin_id, 1, NULL), row_version
        FROM viverappweb_alpha.application_settings;
        INSERT INTO viverappweb.clinic_weekly_hours
            (id, day_of_week, start_time, end_time, is_active,
             created_at_utc, updated_at_utc, row_version)
        SELECT id, day_of_week, start_time, end_time, is_active,
               created_at_utc, updated_at_utc, row_version
        FROM viverappweb_alpha.clinic_weekly_hours;
        INSERT INTO viverappweb.holidays
            (id, holiday_date, name, start_time, end_time, created_at_utc,
             updated_at_utc, row_version, is_annual)
        SELECT id, holiday_date, name, start_time, end_time, created_at_utc,
               updated_at_utc, row_version, is_annual
        FROM viverappweb_alpha.holidays;
        INSERT INTO viverappweb.premium_plans
            (id, name, price_amount, appointment_discount_percent, validity_days,
             is_active, created_at_utc, updated_at_utc, row_version)
        SELECT id, name, price_amount, appointment_discount_percent, validity_days,
               is_active, created_at_utc, updated_at_utc, row_version
        FROM viverappweb_alpha.premium_plans;
        INSERT INTO viverappweb.specialties
            (id, name, normalized_name, is_active, created_at_utc, updated_at_utc, row_version)
        SELECT id, name, normalized_name, is_active, created_at_utc, updated_at_utc, row_version
        FROM viverappweb_alpha.specialties;

        -- Nada de pacientes, profissionais, tipos de atendimento, agenda, pagamentos,
        -- caixa, prontuários, anexos, outbox, sessões, notificações ou passkeys é copiado.
        COMMIT;
        SELECT 1 AS administrador_destino,
               (SELECT COUNT(*) FROM viverappweb.accounts) AS contas,
               (SELECT COUNT(*) FROM viverappweb.appointments) AS atendimentos,
               (SELECT COUNT(*) FROM viverappweb.cash_movements) AS movimentos_caixa,
               (SELECT next_value FROM viverappweb.appointment_number_sequence WHERE sequence_key = 1)
                    AS proximo_numero_atendimento;
    END IF;
END$$
DELIMITER ;
