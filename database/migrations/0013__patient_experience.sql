-- Fase 11. Executar exclusivamente em viverappweb, pelo runner que valida MySQL 8.0.41.
ALTER TABLE appointment_types
    ADD category_code varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'consultation',
    ADD CONSTRAINT ck_appointment_types_category CHECK (category_code IN ('consultation','examination','surgery'));
ALTER TABLE appointments
    ADD base_price_amount decimal(13,2) NULL,
    ADD discount_percent decimal(5,2) NOT NULL DEFAULT 0,
    ADD payment_location_code varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL DEFAULT 'web',
    ADD CONSTRAINT ck_appointments_discount CHECK (discount_percent BETWEEN 0 AND 100),
    ADD CONSTRAINT ck_appointments_payment_location CHECK (payment_location_code IN ('web','clinic') AND (modality_code <> 'online' OR payment_location_code = 'web'));
ALTER TABLE payments ADD method_code varchar(30) CHARACTER SET ascii COLLATE ascii_bin NULL;

CREATE TABLE patient_preferences (
    account_id bigint unsigned NOT NULL PRIMARY KEY,
    email_enabled tinyint(1) NOT NULL,
    sms_enabled tinyint(1) NOT NULL,
    updated_at_utc datetime(6) NOT NULL,
    CONSTRAINT fk_patient_preferences_account FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE RESTRICT
) ENGINE=InnoDB;

CREATE TABLE appointment_reviews (
    appointment_id bigint unsigned NOT NULL PRIMARY KEY,
    rating tinyint unsigned NOT NULL,
    comment varchar(1000) NULL,
    created_at_utc datetime(6) NOT NULL,
    CONSTRAINT fk_appointment_reviews_appointment FOREIGN KEY (appointment_id) REFERENCES appointments(id) ON DELETE RESTRICT,
    CONSTRAINT ck_appointment_reviews_rating CHECK (rating BETWEEN 1 AND 5)
) ENGINE=InnoDB;

-- Adaptador privado local: conteúdo cifrado por Data Protection, nunca exposto como URL pública.
CREATE TABLE private_documents (
    id char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    owner_account_id bigint unsigned NOT NULL,
    content_type varchar(127) NOT NULL,
    original_file_name varchar(255) NOT NULL,
    size_bytes int unsigned NOT NULL,
    sha256 binary(32) NOT NULL,
    protected_content mediumblob NOT NULL,
    status_code varchar(20) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at_utc datetime(6) NOT NULL,
    CONSTRAINT fk_private_documents_owner FOREIGN KEY (owner_account_id) REFERENCES accounts(id) ON DELETE RESTRICT,
    CONSTRAINT ck_private_documents_size CHECK (size_bytes BETWEEN 1 AND 5242880),
    CONSTRAINT ck_private_documents_status CHECK (status_code IN ('available','quarantined','deleted'))
) ENGINE=InnoDB;

ALTER TABLE premium_memberships
    ADD proof_document_id char(36) CHARACTER SET ascii COLLATE ascii_bin NULL,
    ADD rejection_reason varchar(1000) NULL,
    ADD reviewed_at_utc datetime(6) NULL,
    ADD row_version bigint unsigned NOT NULL DEFAULT 1,
    ADD CONSTRAINT fk_premium_memberships_proof FOREIGN KEY (proof_document_id) REFERENCES private_documents(id) ON DELETE RESTRICT;

CREATE TABLE contact_change_requests (
    id char(36) CHARACTER SET ascii COLLATE ascii_bin NOT NULL PRIMARY KEY,
    account_id bigint unsigned NOT NULL,
    challenge_id binary(16) NOT NULL,
    channel_code varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    destination varchar(254) NOT NULL,
    created_at_utc datetime(6) NOT NULL,
    completed_at_utc datetime(6) NULL,
    CONSTRAINT fk_contact_change_account FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE RESTRICT,
    CONSTRAINT ck_contact_change_channel CHECK (channel_code IN ('email','sms'))
) ENGINE=InnoDB;
ALTER TABLE account_challenges DROP CHECK ck_account_challenges_purpose;
ALTER TABLE account_challenges ADD CONSTRAINT ck_account_challenges_purpose CHECK (purpose_code IN ('login','password_reset','contact_verification','contact_change'));

-- Presença durável; a mídia nunca é gravada. Uma conexão ativa por participante/atendimento.
CREATE TABLE teleconsultation_peers (
    appointment_id bigint unsigned NOT NULL,
    account_id bigint unsigned NOT NULL,
    connection_id varchar(128) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    expires_at_utc datetime(6) NOT NULL,
    PRIMARY KEY (appointment_id, account_id),
    CONSTRAINT fk_teleconsultation_appointment FOREIGN KEY (appointment_id) REFERENCES appointments(id) ON DELETE RESTRICT,
    CONSTRAINT fk_teleconsultation_account FOREIGN KEY (account_id) REFERENCES accounts(id) ON DELETE RESTRICT
) ENGINE=InnoDB;

INSERT INTO application_settings (setting_key,value_json,description,is_secret,updated_at_utc) VALUES
('appointments.allow_clinic_payment','true','Permitir escolha de pagamento presencial, sem marcar como pago.',0,UTC_TIMESTAMP(6)),
('appointments.patient_daily_limit','3','Máximo de agendamentos ativos por paciente por dia.',0,UTC_TIMESTAMP(6)),
('patient.promotions','[]','Cards promocionais aprovados: title, description, url (HTTPS e host autorizado).',0,UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE setting_key=VALUES(setting_key);
