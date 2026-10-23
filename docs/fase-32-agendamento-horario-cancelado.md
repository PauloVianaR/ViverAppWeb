# Correção de reutilização de horários — Fase 32

## Causa

A disponibilidade considera somente `pending`, `confirmed`, `arrived` e
`in_progress` como reservas ativas. Os índices únicos antigos de `appointments`
consideravam todos os estados. Assim, um horário liberado por cancelamento podia
aparecer disponível e falhar na gravação com MySQL 1062.

## Correção

A migration 0062 inclui uma expressão funcional nos dois índices únicos de
profissional/paciente e início. Ela vale 1 para reservas ativas e NULL para estados
encerrados. O MySQL mantém a exclusividade das reservas ativas e permite coexistir
com o histórico. As transações e os bloqueios de agenda da API permanecem vigentes.
Nenhum atendimento existente precisa ser removido ou alterado.

O rollback SQL automático é recusado: depois de reutilizar um horário, restaurar
os índices antigos pode ser incompatível com registros históricos válidos. O
schema permanece compatível com a API/Web anterior.

## Validação

No MySQL 8.0.41 persistente `viverappweb_homolog`, com as mesmas migrations:

```powershell
./scripts/test-appointment-start-uniqueness.ps1
```

O teste cria somente fixtures sintéticas em transações desfeitas. São 13 cenários:
reutilização após canceled/completed/no_show/rescheduled, proteção de profissional
e paciente nos quatro estados ativos e cancelamento/reagendamento sucessivos.
O primeiro cenário reproduziu MySQL 1062 antes da migration e todos passaram após.
O teste de contrato verifica os dois índices funcionais no banco fonte do scaffold.

Não executar fixtures contra produção. A conferência visual autenticada depende
de uma sessão autorizada no navegador integrado.
