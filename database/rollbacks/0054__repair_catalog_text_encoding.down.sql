-- Recuperação da migration 0054: RESTORE DE BACKUP, não reversão automática.
-- Não há snapshot dos rótulos anteriores; reconversão automática voltaria a corromper caracteres.
-- NÃO executar no deploy. Este script recusa o rollback deliberadamente.
-- Procedimento: suspender novas escritas, fazer backup do estado atual, restaurar
-- backup consistente anterior à migration em janela expressamente autorizada,
-- conciliar escritas posteriores e usar o binário compatível com esse schema.
-- Nunca apagar auditoria nem desabilitar constraints/triggers para "reverter".
SIGNAL SQLSTATE '45000'
    SET MESSAGE_TEXT = 'Rollback 0054 recusado: recuperar por backup consistente com autorização e conciliação dos dados posteriores.';
