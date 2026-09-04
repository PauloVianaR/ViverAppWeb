# Fase 9 — PagBank Checkout

**Estado:** concluída e integrada à `main` no commit `701dae4`.

Esta fase implementa a cobrança de consultas pelo Checkout PagBank sem confiar em valores ou estados enviados pelo navegador.

## Entregas

- cliente HTTP tipado, com token somente no backend, timeout e repetição apenas em operações seguras ou protegidas pela mesma chave de idempotência;
- checkout associado a um único agendamento e criado com preço e moeda recuperados do MySQL;
- página responsiva de pagamento e retorno que sempre consulta a API do ViverApp;
- autenticação SHA-256 do corpo bruto do webhook, deduplicação persistente e rejeição de divergência de valor;
- máquina de estados que impede regressão de `paid` e `refunded`, inclusive para eventos atrasados;
- confirmação automática da consulta somente após estado `PAID` confiável;
- reconciliação periódica no processo da API, sem worker separado;
- inativação de checkout de consulta cancelada e reembolso total auditável, desabilitado por padrão;
- migrations `0009` e `0010` aplicadas em `viverappweb` e scaffold DB-First regenerado.

## Limites de segurança

- nenhum checkout, pagamento ou reembolso real foi executado nesta fase;
- a integração permanece desabilitada na configuração versionada;
- produção exige `Enabled=true`, `Environment=Production` e `ProductionEnabled=true` fora do repositório;
- reembolsos exigem ainda `RefundsEnabled=true`, papel de gestão, MFA satisfeito, agendamento cancelado e pagamento pago;
- os tokens Sandbox e Produção nunca são enviados ao Blazor, persistidos no banco, registrados em logs ou incluídos em documentação.

Consulte [integração e estados](INTEGRACAO-E-ESTADOS.md), [segurança e idempotência](SEGURANCA-E-IDEMPOTENCIA.md), [runbook controlado](RUNBOOK-PRODUCAO.md) e [verificações](VERIFICACOES.md).

