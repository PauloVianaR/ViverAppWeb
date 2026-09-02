-- Remove as proteções append-only introduzidas pela migration 0004.

DROP TRIGGER IF EXISTS `trg_audit_events_block_delete`;
DROP TRIGGER IF EXISTS `trg_audit_events_block_update`;
