INSERT INTO application_settings
    (setting_key, value_json, description, is_secret, updated_at_utc, row_version)
VALUES ('appointments.allow_pending_no_show', 'false',
    'Permitir marcar Não Compareceu em atendimentos Aguardando Pagamento, somente após o horário de início e respeitando as permissões do usuário.',
    0, UTC_TIMESTAMP(6), 1);

CREATE TABLE patient_birthday_daily_notices (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    account_id BIGINT UNSIGNED NOT NULL,
    operational_date DATE NOT NULL,
    birthday_count INT UNSIGNED NOT NULL,
    created_at_utc DATETIME(6) NOT NULL,
    read_at_utc DATETIME(6) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_patient_birthday_notice_day (account_id, operational_date),
    CONSTRAINT fk_patient_birthday_notice_account FOREIGN KEY (account_id)
        REFERENCES accounts(id) ON DELETE CASCADE
) ENGINE=InnoDB;
