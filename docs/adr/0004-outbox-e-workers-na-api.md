# ADR 0004 — Outbox e workers hospedados na API

- **Estado:** aceita
- **Data:** 2026-09-02

## Contexto

Os workers legados consultam tabelas por polling e possuem garantias desiguais contra concorrência e duplicidade. Manter executáveis separados agora aumentaria a operação sem resolver durabilidade.

## Decisão

Hospedar inicialmente e-mail e rotinas agendadas como `BackgroundService` modulares dentro da API. Toda solicitação assíncrona será persistida numa outbox na mesma transação da mudança de negócio. Firebase, push e SMS não serão migrados para o novo produto.

O processamento terá claim atômico, lease com expiração, chave de idempotência, tentativas limitadas, backoff com jitter, dead-letter, reprocessamento auditado e métricas. Nenhum controle de exclusão dependerá apenas de memória local.

## Consequências

- A implantação inicial tem menos processos e preserva consistência transacional.
- Múltiplas instâncias da API poderão processar a fila com segurança.
- Um worker poderá ser extraído mais tarde sem mudar o contrato da outbox.
