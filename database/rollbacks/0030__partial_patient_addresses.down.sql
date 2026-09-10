UPDATE `account_addresses`
SET `postal_code` = COALESCE(`postal_code`, ''),
    `street` = COALESCE(`street`, ''),
    `number` = COALESCE(`number`, ''),
    `district` = COALESCE(`district`, ''),
    `city` = COALESCE(`city`, ''),
    `state_code` = COALESCE(`state_code`, '');

ALTER TABLE `account_addresses`
    MODIFY COLUMN `postal_code` char(8) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    MODIFY COLUMN `street` varchar(200) NOT NULL,
    MODIFY COLUMN `number` varchar(20) NOT NULL,
    MODIFY COLUMN `district` varchar(100) NOT NULL,
    MODIFY COLUMN `city` varchar(100) NOT NULL,
    MODIFY COLUMN `state_code` char(2) CHARACTER SET ascii COLLATE ascii_bin NOT NULL;
