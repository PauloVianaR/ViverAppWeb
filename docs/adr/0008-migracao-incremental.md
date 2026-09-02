# ADR 0008 — Reescrita e migração incrementais

- **Estado:** aceita
- **Data:** 2026-09-02

## Contexto

O legado contém comportamento útil, regras no cliente e vulnerabilidades que não devem ser copiadas. Uma troca integral sem checkpoints elevaria o risco de perda de dados e regressões.

## Decisão

Executar exatamente uma fase por branch, com critérios de entrada e saída, sem antecipar fases. O legado será usado somente para descoberta e comparação; a implementação nova seguirá os requisitos aprovados e o schema `viverappweb`.

Migrações de dados terão ensaio, contagem, checksum/reconciliação, relatório de exceções e rollback. Integrações serão ativadas primeiro em sandbox ou modo controlado. A desativação de API, workers, storage ou hub legados só ocorrerá depois de equivalência comprovada e autorização explícita.

## Consequências

- O produto ganha checkpoints pequenos e auditáveis.
- Compatibilidade temporária pode exigir adaptadores e execução paralela controlada.
- Uma fase não pode aproveitar a branch para implementar itens futuros, mesmo que pareçam simples.
