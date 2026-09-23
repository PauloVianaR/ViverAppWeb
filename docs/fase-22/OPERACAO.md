# Fase 22 — operação de notificações e jobs

## Escopo entregue

- `outbox_messages` persiste e-mail, SMS e eventos internos com idempotência, versão de template, lease e estado terminal; `notification_preferences` e `notification_suppressions` controlam opt-in e bloqueio por destino sem expor contatos no painel operacional.
- A decisão Premium produz mensagem na mesma transação. Lembretes são descobertos e executados por `scheduled_jobs`, revalidando atendimento, contato confirmado, consentimento e preferências antes de enfileirar e antes de entregar. O job diário de saúde cria alerta interno ao Administrador quando há dead letters ou pendência antiga.
- Claims usam bloqueio de linha `FOR UPDATE SKIP LOCKED` e leases expiráveis. Falhas transitórias usam backoff exponencial com jitter; falhas definitivas vão para dead letter. Reprocessamento requer Administrador com step-up e motivo auditado; mensagens de código de acesso não podem ser reprocessadas.
- O painel **Administrador → Notificações → Entrega de e-mail e SMS** exibe apenas metadados; a página **Segurança da conta** permite ao titular alterar suas preferências.
- O adaptador SMSBarato usa `POST /send` com `chave`, `dest` e `text`, normaliza o texto para ASCII e limita a 160 caracteres. SMTP reutiliza o provedor existente e usa `Message-Id` determinístico.

## Ativação controlada

1. Confirmar MySQL local 8.0.41 / `viverappweb` e migrations 0040–0042. Nenhum schema do legado é alterado.
2. Manter as credenciais em user-secrets no desenvolvimento ou cofre em produção: `Smtp:*` e `SmsBarato:*`. Nunca incluir valores em logs, documentação ou Git.
3. `Notifications:BusinessDelivery:Enabled` e `Notifications:Scheduler:Enabled` começam em `false`. Ativar primeiro a entrega de negócio somente com destinatários de teste autorizados, conferir caixa de entrada, saldo e resposta do SMSBarato; depois ativar o scheduler. Este último exige o primeiro ligado.
4. Conferir contagens da outbox, latência, dead letters, jobs e alertas administrativos. Aprovar limites de monitoramento externo antes da produção. Em falha de provedor, desligar a entrega de negócio preserva mensagens pendentes para posterior reconciliação; não apagar a fila.
5. Não desativar workers legados por suposição. Comparar IDs/eventos/volumes em execução paralela controlada, garantir que apenas um sistema envie cada classe de mensagem e obter autorização do proprietário para o corte. Os workers legados e suas pastas permanecem intactos nesta fase.

## Garantias e limites

- O processamento é **pelo menos uma vez**, não exatamente uma vez. Se o processo cair depois que SMTP/SMSBarato aceitou o envio e antes de gravar o ACK, o lease expira e uma nova tentativa pode ocorrer. O `Message-Id` de SMTP ajuda a reconhecer repetição, mas não a elimina; SMSBarato não recebe chave de idempotência neste endpoint. Antes de reprocessar manualmente uma dead letter, verificar o provedor pelo horário e referência quando houver.
- Payloads clínicos e contatos não entram em métricas, logs do worker ou listagens administrativas. A outbox ainda guarda o destino necessário ao envio e seu prazo de retenção depende da política LGPD pendente.
- Códigos de identidade continuam no worker já existente; a preferência de marketing/lembretes não interrompe a recuperação e verificação de acesso.
- Nenhum envio real de teste foi feito nesta implementação. Homologação de entrega, concorrência em múltiplas instâncias reais e corte dos workers antigos exigem ambiente/destinatários explicitamente autorizados.

## Evidências locais

- Migrations 0040, 0041 e 0042 aplicadas e verificadas em `viverappweb` no MySQL 8.0.41.
- Build sem avisos/erros; 241 testes da suíte automatizada sequencial aprovados após atualização dos contratos do schema.
- Navegador integrado por HTTP: conta sintética de Gestor abriu **Segurança da conta** e carregou preferências; inspecionados desktop, tablet e celular sem overflow horizontal. O painel administrativo com MFA fica para homologação visual autenticada.
