-- Reversão automática deliberadamente recusada: serviços múltiplos, parcelas e descontos
-- são registros financeiros/operacionais que não cabem no schema anterior. Preservar dados,
-- usar versão binária compatível e plano de incidente aprovado; nunca apagar o ledger.
SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Rollback 0050 exige plano de preservacao de dados aprovado';
