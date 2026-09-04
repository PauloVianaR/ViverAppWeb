# Runbook controlado de produção

Este roteiro não autoriza uma cobrança real. A ativação e a transação controlada exigem ordem explícita do proprietário em uma etapa futura.

## 1. Homologar em Sandbox

- configurar, fora do Git, `PagBank:TokenSandbox`, `PagBank:ApiPublicBaseUrl` e `PagBank:WebPublicBaseUrl`;
- publicar as duas URLs em HTTPS válido e confirmar que a URL completa do webhook tem até 100 caracteres;
- definir `PagBank:Enabled=true` e manter `PagBank:Environment=Sandbox`;
- deixar `ProductionEnabled=false` e `RefundsEnabled=false`;
- testar criação repetida, retorno, `WAITING`, `IN_ANALYSIS`, `PAID`, `DECLINED`, expiração, replay, assinatura falsa e evento fora de ordem;
- comparar valor, identificadores e estado com o painel Sandbox do PagBank.

## 2. Preparar produção sem ativar

- concluir a homologação exigida pelo PagBank;
- armazenar `PagBank:TokenProduction` no cofre do ambiente, com acesso restrito e rotação documentada;
- configurar domínios públicos definitivos, TLS, proxy confiável, alertas e backup;
- revisar URLs de retorno e webhook, política de cancelamento, responsáveis por conciliação e procedimento de incidente;
- manter `PagBank:Enabled=false`, `ProductionEnabled=false` e `RefundsEnabled=false` durante a preparação.

## 3. Ativação com autorização explícita

- registrar a autorização do proprietário e uma janela de mudança;
- definir `Environment=Production`, `ProductionEnabled=true` e, somente então, `Enabled=true` no cofre/configuração de implantação;
- reiniciar uma única instância, conferir health checks e criar um checkout controlado de menor valor permitido;
- uma pessoa confere o valor antes do pagamento; outra acompanha webhook, estado local, painel PagBank e auditoria;
- desativar `Enabled` imediatamente se identificador, valor, assinatura, estado ou reconciliação divergir.

## 4. Reembolsos

`RefundsEnabled` permanece independente e falso. Só deve ser ativado após teste Sandbox e autorização operacional específica. O fluxo implementado permite apenas devolução integral de pagamento `paid` cujo agendamento já esteja `canceled`; cada solicitação exige chave idempotente e fica auditada.

## Resposta a incidente

1. definir `PagBank:Enabled=false` e reiniciar as instâncias;
2. preservar `payment_events`, `payment_webhook_receipts`, `audit_events` e logs correlacionados;
3. conferir transações diretamente no PagBank sem alterar registros manualmente;
4. rotacionar o token se houver suspeita de exposição;
5. reconciliar cada pagamento afetado antes de reativar.

