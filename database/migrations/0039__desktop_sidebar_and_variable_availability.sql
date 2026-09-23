-- Fase 21. Executar exclusivamente no viverappweb pelo runner MySQL 8.0.41.
INSERT INTO application_settings
    (setting_key, value_json, description, is_secret, updated_at_utc, row_version)
VALUES ('web.desktop_sidebar_enabled', 'true',
        'Exibe a navegação lateral ampliada nas telas desktop.', 0, UTC_TIMESTAMP(6), 1)
ON DUPLICATE KEY UPDATE setting_key = VALUES(setting_key);

ALTER TABLE account_ui_preferences
    ADD COLUMN desktop_sidebar_collapsed tinyint(1) NOT NULL DEFAULT 0 AFTER calendar_view_mode,
    ADD CONSTRAINT ck_account_ui_preferences_sidebar_collapsed
        CHECK (desktop_sidebar_collapsed IN (0, 1));

ALTER TABLE professional_preferences
    ADD COLUMN availability_mode varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'recurring' AFTER online_enabled,
    ADD CONSTRAINT ck_professional_preferences_availability_mode
        CHECK (availability_mode IN ('recurring', 'variable'));

CREATE TABLE professional_variable_hours (
    id bigint unsigned NOT NULL AUTO_INCREMENT,
    professional_account_id bigint unsigned NOT NULL,
    available_date date NOT NULL,
    start_time time NOT NULL,
    end_time time NOT NULL,
    modality_code varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at_utc datetime(6) NOT NULL,
    updated_at_utc datetime(6) NOT NULL,
    row_version bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    KEY ix_professional_variable_hours_date (professional_account_id, available_date, modality_code),
    CONSTRAINT fk_professional_variable_hours_professional
        FOREIGN KEY (professional_account_id) REFERENCES professional_profiles (account_id) ON DELETE CASCADE,
    CONSTRAINT ck_professional_variable_hours_range CHECK (end_time > start_time),
    CONSTRAINT ck_professional_variable_hours_modality CHECK (modality_code IN ('in_person', 'online', 'both')),
    CONSTRAINT ck_professional_variable_hours_version CHECK (row_version > 0)
) ENGINE=InnoDB;
