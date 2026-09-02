-- Torna a trilha de auditoria append-only no próprio MySQL.

CREATE TRIGGER `trg_audit_events_block_update`
BEFORE UPDATE ON `audit_events`
FOR EACH ROW
SIGNAL SQLSTATE '45000'
    SET MESSAGE_TEXT = 'audit_events is append-only';

CREATE TRIGGER `trg_audit_events_block_delete`
BEFORE DELETE ON `audit_events`
FOR EACH ROW
SIGNAL SQLSTATE '45000'
    SET MESSAGE_TEXT = 'audit_events is append-only';
