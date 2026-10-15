-- Não executar automaticamente: uma reversão pode invalidar recebimentos autorizados.
SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Preserve authorized deferred online collections; automatic rollback is not supported';
