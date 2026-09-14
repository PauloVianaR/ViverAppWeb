-- Fase 17. Endereço administrativo opcional e preenchível de forma parcial.
ALTER TABLE `account_addresses`
    MODIFY COLUMN `postal_code` char(8) CHARACTER SET ascii COLLATE ascii_bin NULL,
    MODIFY COLUMN `street` varchar(200) NULL,
    MODIFY COLUMN `number` varchar(20) NULL,
    MODIFY COLUMN `district` varchar(100) NULL,
    MODIFY COLUMN `city` varchar(100) NULL,
    MODIFY COLUMN `state_code` char(2) CHARACTER SET ascii COLLATE ascii_bin NULL;
