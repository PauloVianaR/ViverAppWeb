-- Não executado. Preservar a configuração e sua trilha administrativa.
-- Desativar pela tela de Configurações em vez de remover uma regra em uso.
SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Disable elapsed same-day booking through administrative settings; automatic rollback is not supported';
