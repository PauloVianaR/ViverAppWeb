-- Horarios reutilizados podem ter varios registros historicos validos.
-- Restaurar os indices antigos impediria essa coexistencia; nunca apagar historico.
SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Rollback automatico recusado: preservar historico e avaliar a compatibilidade dos indices.';
