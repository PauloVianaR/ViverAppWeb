-- Fase 12. Executar exclusivamente em viverappweb, pelo runner que valida MySQL 8.0.41.
CREATE TABLE doctor_services (
    doctor_account_id bigint unsigned NOT NULL,
    appointment_type_id int unsigned NOT NULL,
    is_active tinyint(1) NOT NULL,
    created_at_utc datetime(6) NOT NULL,
    updated_at_utc datetime(6) NOT NULL,
    row_version bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (doctor_account_id, appointment_type_id),
    KEY ix_doctor_services_type_active (appointment_type_id, is_active),
    CONSTRAINT fk_doctor_services_doctor FOREIGN KEY (doctor_account_id) REFERENCES doctor_profiles(account_id) ON DELETE CASCADE,
    CONSTRAINT fk_doctor_services_type FOREIGN KEY (appointment_type_id) REFERENCES appointment_types(id) ON DELETE RESTRICT,
    CONSTRAINT ck_doctor_services_active CHECK (is_active IN (0, 1)),
    CONSTRAINT ck_doctor_services_row_version CHECK (row_version > 0)
) ENGINE=InnoDB;

CREATE TABLE doctor_preferences (
    doctor_account_id bigint unsigned NOT NULL PRIMARY KEY,
    email_enabled tinyint(1) NOT NULL,
    sms_enabled tinyint(1) NOT NULL,
    online_enabled tinyint(1) NOT NULL,
    max_online_daily smallint unsigned NOT NULL DEFAULT 8,
    max_in_person_daily smallint unsigned NOT NULL DEFAULT 16,
    updated_at_utc datetime(6) NOT NULL,
    row_version bigint unsigned NOT NULL DEFAULT 1,
    CONSTRAINT fk_doctor_preferences_doctor FOREIGN KEY (doctor_account_id) REFERENCES doctor_profiles(account_id) ON DELETE CASCADE,
    CONSTRAINT ck_doctor_preferences_booleans CHECK (email_enabled IN (0, 1) AND sms_enabled IN (0, 1) AND online_enabled IN (0, 1)),
    CONSTRAINT ck_doctor_preferences_limits CHECK (max_online_daily BETWEEN 0 AND 100 AND max_in_person_daily BETWEEN 0 AND 100),
    CONSTRAINT ck_doctor_preferences_row_version CHECK (row_version > 0)
) ENGINE=InnoDB;

CREATE TABLE doctor_availability_exceptions (
    id bigint unsigned NOT NULL AUTO_INCREMENT PRIMARY KEY,
    doctor_account_id bigint unsigned NOT NULL,
    exception_date date NOT NULL,
    modality_code varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    is_available tinyint(1) NOT NULL,
    start_time time NULL,
    end_time time NULL,
    created_at_utc datetime(6) NOT NULL,
    updated_at_utc datetime(6) NOT NULL,
    row_version bigint unsigned NOT NULL DEFAULT 1,
    KEY ix_doctor_availability_exception_date (doctor_account_id, exception_date),
    CONSTRAINT fk_doctor_availability_exception_doctor FOREIGN KEY (doctor_account_id) REFERENCES doctor_profiles(account_id) ON DELETE CASCADE,
    CONSTRAINT ck_doctor_availability_exception_modality CHECK (modality_code IN ('in_person', 'online', 'both')),
    CONSTRAINT ck_doctor_availability_exception_state CHECK ((is_available = 0 AND start_time IS NULL AND end_time IS NULL) OR (is_available = 1 AND start_time IS NOT NULL AND end_time > start_time)),
    CONSTRAINT ck_doctor_availability_exception_row_version CHECK (row_version > 0)
) ENGINE=InnoDB;

CREATE TABLE doctor_patient_links (
    doctor_account_id bigint unsigned NOT NULL,
    patient_account_id bigint unsigned NOT NULL,
    created_by_account_id bigint unsigned NOT NULL,
    status_code varchar(10) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    created_at_utc datetime(6) NOT NULL,
    updated_at_utc datetime(6) NOT NULL,
    row_version bigint unsigned NOT NULL DEFAULT 1,
    PRIMARY KEY (doctor_account_id, patient_account_id),
    KEY ix_doctor_patient_links_patient (patient_account_id, status_code),
    CONSTRAINT fk_doctor_patient_links_doctor FOREIGN KEY (doctor_account_id) REFERENCES doctor_profiles(account_id) ON DELETE CASCADE,
    CONSTRAINT fk_doctor_patient_links_patient FOREIGN KEY (patient_account_id) REFERENCES accounts(id) ON DELETE RESTRICT,
    CONSTRAINT fk_doctor_patient_links_creator FOREIGN KEY (created_by_account_id) REFERENCES accounts(id) ON DELETE RESTRICT,
    CONSTRAINT ck_doctor_patient_links_status CHECK (status_code IN ('active', 'archived')),
    CONSTRAINT ck_doctor_patient_links_row_version CHECK (row_version > 0)
) ENGINE=InnoDB;

CREATE TABLE medical_report_versions (
    id bigint unsigned NOT NULL AUTO_INCREMENT PRIMARY KEY,
    medical_report_id bigint unsigned NOT NULL,
    version_number int unsigned NOT NULL,
    author_doctor_account_id bigint unsigned NOT NULL,
    clinical_summary text NOT NULL,
    recommendations text NULL,
    change_reason varchar(1000) NULL,
    created_at_utc datetime(6) NOT NULL,
    UNIQUE KEY ux_medical_report_versions_number (medical_report_id, version_number),
    KEY ix_medical_report_versions_author (author_doctor_account_id, created_at_utc),
    CONSTRAINT fk_medical_report_versions_report FOREIGN KEY (medical_report_id) REFERENCES medical_reports(id) ON DELETE RESTRICT,
    CONSTRAINT fk_medical_report_versions_author FOREIGN KEY (author_doctor_account_id) REFERENCES doctor_profiles(account_id) ON DELETE RESTRICT,
    CONSTRAINT ck_medical_report_versions_number CHECK (version_number > 0),
    CONSTRAINT ck_medical_report_versions_summary CHECK (CHAR_LENGTH(TRIM(clinical_summary)) BETWEEN 20 AND 12000),
    CONSTRAINT ck_medical_report_versions_recommendations CHECK (recommendations IS NULL OR CHAR_LENGTH(recommendations) <= 8000),
    CONSTRAINT ck_medical_report_versions_reason CHECK (change_reason IS NULL OR CHAR_LENGTH(TRIM(change_reason)) BETWEEN 5 AND 1000)
) ENGINE=InnoDB;

INSERT INTO medical_report_versions
    (medical_report_id, version_number, author_doctor_account_id, clinical_summary, recommendations, change_reason, created_at_utc)
SELECT id, 1, author_doctor_account_id, clinical_summary, recommendations, NULL, COALESCE(published_at_utc, updated_at_utc)
FROM medical_reports
WHERE status_code = 'published';

ALTER TABLE appointment_documents
    ADD deleted_at_utc datetime(6) NULL,
    ADD deleted_by_account_id bigint unsigned NULL,
    ADD row_version bigint unsigned NOT NULL DEFAULT 1,
    ADD CONSTRAINT fk_appointment_documents_deleted_by FOREIGN KEY (deleted_by_account_id) REFERENCES accounts(id) ON DELETE RESTRICT,
    ADD CONSTRAINT ck_appointment_documents_deleted_state CHECK ((status_code = 'deleted' AND deleted_at_utc IS NOT NULL AND deleted_by_account_id IS NOT NULL) OR (status_code <> 'deleted' AND deleted_at_utc IS NULL AND deleted_by_account_id IS NULL)),
    ADD CONSTRAINT ck_appointment_documents_row_version CHECK (row_version > 0);

ALTER TABLE private_documents DROP CHECK ck_private_documents_size;
ALTER TABLE private_documents ADD CONSTRAINT ck_private_documents_size CHECK (size_bytes BETWEEN 1 AND 10485760);

INSERT INTO doctor_preferences (doctor_account_id,email_enabled,sms_enabled,online_enabled,max_online_daily,max_in_person_daily,updated_at_utc,row_version)
SELECT account_id,1,1,1,8,16,UTC_TIMESTAMP(6),1 FROM doctor_profiles;

INSERT INTO doctor_services (doctor_account_id,appointment_type_id,is_active,created_at_utc,updated_at_utc,row_version)
SELECT dp.account_id,at.id,1,UTC_TIMESTAMP(6),UTC_TIMESTAMP(6),1
FROM doctor_profiles dp CROSS JOIN appointment_types at WHERE at.is_active=1;
