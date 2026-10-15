-- Nao remover ou alterar ajustes contabilizados para restaurar a regra anterior.
SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Preserve posted cash adjustments; automatic rollback is not supported';
