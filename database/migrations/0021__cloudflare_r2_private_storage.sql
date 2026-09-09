ALTER TABLE `private_documents`
    MODIFY COLUMN `protected_content` MEDIUMBLOB NULL,
    ADD COLUMN `storage_provider_code` VARCHAR(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'database' AFTER `sha256`,
    ADD COLUMN `object_key` VARCHAR(512) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `storage_provider_code`,
    ADD COLUMN `storage_etag` VARCHAR(128) CHARACTER SET ascii COLLATE ascii_bin NULL AFTER `object_key`,
    ADD COLUMN `migrated_at_utc` DATETIME(6) NULL AFTER `storage_etag`,
    ADD COLUMN `last_verified_at_utc` DATETIME(6) NULL AFTER `migrated_at_utc`,
    ADD COLUMN `legacy_content_retained_until_utc` DATETIME(6) NULL AFTER `last_verified_at_utc`,
    ADD COLUMN `row_version` BIGINT UNSIGNED NOT NULL DEFAULT 1 AFTER `legacy_content_retained_until_utc`,
    ADD UNIQUE KEY `ux_private_documents_object_key` (`object_key`),
    ADD CONSTRAINT `ck_private_documents_storage_provider` CHECK (`storage_provider_code` IN ('database', 'r2')),
    ADD CONSTRAINT `ck_private_documents_storage_location` CHECK (
        (`storage_provider_code` = 'database' AND `protected_content` IS NOT NULL)
        OR (`storage_provider_code` = 'r2' AND `object_key` IS NOT NULL)
    ),
    ADD CONSTRAINT `ck_private_documents_row_version` CHECK (`row_version` > 0);
