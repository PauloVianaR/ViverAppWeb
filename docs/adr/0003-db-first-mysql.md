# ADR 0003 — MySQL 8.0.41 e EF DB-First

- **Estado:** aceita
- **Data:** 2026-09-02

## Contexto

O proprietário definiu o uso de EF Core em abordagem DB-First. O banco legado serve apenas de referência e não pode ser alterado. O novo sistema precisa de um schema próprio, reproduzível e independente dos models legados.

## Decisão

- Usar exclusivamente MySQL 8.0.41.
- Tratar `viverappmobile` como banco legado somente leitura.
- Criar e alterar apenas o novo banco `viverappweb`.
- Evoluir `viverappweb` por scripts SQL imutáveis, versionados, revisados e executados em ordem, com uma tabela de histórico.
- Após aplicar todos os scripts pendentes, gerar novamente `DbContext` e entidades por `dotnet ef dbcontext scaffold` a partir de `viverappweb`.
- Nunca editar manualmente arquivos gerados; extensões devem ficar em arquivos parciais ou camadas próprias.
- Criar DTOs, contratos e models novos exclusivamente a partir do schema e dos casos de uso do web, sem reaproveitar classes do legado.
- Proibir `EnsureCreated`, migrations Code-First, `dotnet ef migrations add` e `dotnet ef database update`.

Antes de qualquer comando de escrita, a rotina deverá confirmar servidor, versão e nome exato do banco-alvo, abortando se não for `viverappweb`.

## Confirmação do proprietário

O proprietário confirmou que `viverappmobile` é o banco legado e `viverappweb` é o banco novo. Nenhuma tentativa deve renomear, criar ou corrigir o banco legado.

## Consequências

- O SQL é a fonte de verdade do schema e o scaffold é um artefato derivado.
- Todo ambiente precisa executar todos os scripts pendentes antes do scaffold e da aplicação.
- A revisão de DDL e o teste de restauração tornam-se portas obrigatórias de entrega.
