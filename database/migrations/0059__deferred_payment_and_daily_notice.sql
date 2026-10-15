-- Pagamento posterior mantém cobrança, preço e evolução clínica independentes.
ALTER TABLE appointments
    ADD COLUMN payment_deferred BOOLEAN NOT NULL DEFAULT FALSE,
    ADD CONSTRAINT ck_appointments_payment_deferred CHECK (
        payment_deferred IN (0, 1) AND
        (payment_deferred = 0 OR (requires_payment = 1 AND billing_form_code = 'private'))
    );

-- Uma conferência por conta/dia no fuso da clínica, inclusive quando o saldo é zero.
CREATE TABLE payment_pending_daily_notices (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    account_id BIGINT UNSIGNED NOT NULL,
    operational_date DATE NOT NULL,
    pending_count INT UNSIGNED NOT NULL,
    created_at_utc DATETIME(6) NOT NULL,
    read_at_utc DATETIME(6) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_payment_pending_notice_day (account_id, operational_date),
    CONSTRAINT fk_payment_pending_notice_account FOREIGN KEY (account_id)
        REFERENCES accounts(id) ON DELETE CASCADE
) ENGINE=InnoDB;
