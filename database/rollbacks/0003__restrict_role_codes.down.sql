-- Remove apenas a restrição de catálogo criada pela migration 0003.

ALTER TABLE `roles` DROP CHECK `ck_roles_code`;
