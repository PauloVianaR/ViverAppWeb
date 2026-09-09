-- O rollback só é seguro enquanto todos os registros ainda preservarem protected_content.
-- A retenção do blob legado não deve ser encerrada antes da reconciliação e do prazo aprovado.
ALTER TABLE `private_documents`
    DROP CHECK `ck_private_documents_row_version`,
    DROP CHECK `ck_private_documents_storage_location`,
    DROP CHECK `ck_private_documents_storage_provider`,
    DROP INDEX `ux_private_documents_object_key`,
    DROP COLUMN `row_version`,
    DROP COLUMN `legacy_content_retained_until_utc`,
    DROP COLUMN `last_verified_at_utc`,
    DROP COLUMN `migrated_at_utc`,
    DROP COLUMN `storage_etag`,
    DROP COLUMN `object_key`,
    DROP COLUMN `storage_provider_code`,
    MODIFY COLUMN `protected_content` MEDIUMBLOB NOT NULL;
