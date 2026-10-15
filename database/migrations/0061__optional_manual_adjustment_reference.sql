-- Ajustes manuais exigem motivo, mas nao precisam compensar um movimento especifico.
-- Preserva todos os vinculos existentes e a referencia obrigatoria dos estornos.
ALTER TABLE cash_movements
    DROP CHECK ck_cash_movements_related_link,
    ADD CONSTRAINT ck_cash_movements_related_link CHECK (
        type_code <> 'payment_reversal' OR related_movement_id IS NOT NULL
    );
