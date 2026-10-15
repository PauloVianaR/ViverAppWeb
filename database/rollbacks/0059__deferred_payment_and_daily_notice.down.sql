-- Não executar automaticamente: preserva obrigações financeiras e avisos duráveis.
-- Reversão exige plano explícito de conciliação, backup e aprovação do proprietário.
SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Reconcile deferred obligations before a reviewed rollback; automatic rollback is not supported';
