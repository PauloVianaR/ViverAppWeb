-- Procedimento operacional avulso (não é migration). Preparado, NÃO instalado nem executado.
-- MySQL 8.0.41 / viverappweb. Revisar pagamentos externos pendentes antes de aplicar.
-- Instalação futura: executar este arquivo no banco correto; chamada de prévia:
-- CALL sp_cancel_overdue_appointments(<id_do_admin>, FALSE);
-- Chamada efetiva, somente com autorização expressa posterior:
-- CALL sp_cancel_overdue_appointments(<id_do_admin>, TRUE);

DELIMITER $$
CREATE PROCEDURE sp_cancel_overdue_appointments(
    IN p_admin_account_id BIGINT UNSIGNED,
    IN p_apply BOOLEAN
)
BEGIN
    DECLARE v_timezone VARCHAR(100);
    DECLARE v_local_today DATE;
    DECLARE v_cutoff_utc DATETIME(6);
    DECLARE v_now_utc DATETIME(6) DEFAULT UTC_TIMESTAMP(6);
    DECLARE v_pending_count BIGINT UNSIGNED DEFAULT 0;
    DECLARE v_confirmed_count BIGINT UNSIGNED DEFAULT 0;
    DECLARE v_admin_count INT DEFAULT 0;
    DECLARE v_reason VARCHAR(500) DEFAULT
        'Cancelamento automático em massa de atendimentos anteriores que não evoluíram.';

    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        ROLLBACK;
        RESIGNAL;
    END;

    IF DATABASE() <> 'viverappweb' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Execute apenas em viverappweb.';
    END IF;
    IF VERSION() NOT LIKE '8.0.41%' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Requer MySQL 8.0.41.';
    END IF;
    SELECT COUNT(*) INTO v_admin_count FROM accounts
        WHERE id = p_admin_account_id AND role_code = 'administrator' AND status_code = 'active';
    IF v_admin_count <> 1 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Informe uma conta administradora ativa.';
    END IF;
    SELECT timezone_name INTO v_timezone FROM clinic WHERE singleton_id = 1;
    SET v_local_today = DATE(CONVERT_TZ(v_now_utc, '+00:00', v_timezone));
    SET v_cutoff_utc = CONVERT_TZ(CONCAT(v_local_today, ' 00:00:00'), v_timezone, '+00:00');
    IF v_cutoff_utc IS NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Fuso da clinica indisponivel no MySQL.';
    END IF;

    IF p_apply = FALSE THEN
        SELECT COUNT(*) INTO v_pending_count FROM appointments
            WHERE status_code = 'pending' AND starts_at_utc < v_cutoff_utc;
        SELECT COUNT(*) INTO v_confirmed_count FROM appointments
            WHERE status_code = 'confirmed' AND starts_at_utc < v_cutoff_utc;
        SELECT v_local_today AS data_clinica,
               v_pending_count AS pendentes_para_cancelar,
               v_confirmed_count AS confirmados_para_marcar_falta,
               'PREVIA: nenhum registro foi alterado' AS resultado;
    ELSEIF p_apply = TRUE THEN
        SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
        START TRANSACTION;
        SELECT COUNT(*) INTO v_pending_count FROM appointments
            WHERE status_code = 'pending' AND starts_at_utc < v_cutoff_utc;
        SELECT COUNT(*) INTO v_confirmed_count FROM appointments
            WHERE status_code = 'confirmed' AND starts_at_utc < v_cutoff_utc;

        INSERT INTO appointment_status_history
            (appointment_id, actor_account_id, from_status_code, to_status_code,
             reason, starts_at_utc, ends_at_utc, occurred_at_utc)
        SELECT id, p_admin_account_id, status_code,
               IF(status_code = 'pending', 'canceled', 'no_show'),
               v_reason, starts_at_utc, ends_at_utc, v_now_utc
        FROM appointments
        WHERE status_code IN ('pending', 'confirmed') AND starts_at_utc < v_cutoff_utc;

        UPDATE appointments
        SET status_code = 'canceled', cancellation_reason = v_reason,
            canceled_by_account_id = p_admin_account_id, canceled_at_utc = v_now_utc,
            updated_at_utc = v_now_utc, row_version = row_version + 1
        WHERE status_code = 'pending' AND starts_at_utc < v_cutoff_utc;

        UPDATE appointments
        SET status_code = 'no_show', cancellation_reason = v_reason,
            no_show_recorded_by_account_id = p_admin_account_id,
            no_show_recorded_at_utc = v_now_utc,
            updated_at_utc = v_now_utc, row_version = row_version + 1
        WHERE status_code = 'confirmed' AND starts_at_utc < v_cutoff_utc;

        INSERT INTO audit_events
            (actor_account_id, event_code, entity_type, correlation_id, data_json, occurred_at_utc)
        VALUES
            (p_admin_account_id, 'appointment.bulk_overdue_transition', 'appointment', UUID(),
             JSON_OBJECT('clinicDate', v_local_today, 'canceled', v_pending_count,
                         'noShow', v_confirmed_count, 'reason', v_reason), v_now_utc);
        COMMIT;
        SELECT v_local_today AS data_clinica,
               v_pending_count AS pendentes_cancelados,
               v_confirmed_count AS confirmados_marcados_falta;
    ELSE
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Informe p_apply = TRUE ou FALSE.';
    END IF;
END$$
DELIMITER ;
