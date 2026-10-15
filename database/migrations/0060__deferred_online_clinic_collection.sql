-- A cobrança posterior autorizada pela gestão pode ser recebida no caixa
-- mesmo para uma consulta online. Online comum continua com pagamento Web.
ALTER TABLE appointments
    DROP CHECK ck_appointments_payment_location,
    ADD CONSTRAINT ck_appointments_payment_location CHECK (
        payment_location_code IN ('web', 'clinic') AND
        (modality_code <> 'online' OR payment_location_code = 'web' OR payment_deferred = 1)
    );
