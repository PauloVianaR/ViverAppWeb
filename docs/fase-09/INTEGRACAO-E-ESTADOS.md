# Integração e estados

Documentação oficial consultada em **3 de setembro de 2026**:

- [Criar Checkout](https://developer.pagbank.com.br/reference/criar-checkout);
- [Consultar Checkout](https://developer.pagbank.com.br/reference/consultar-checkout);
- [Objeto Checkout](https://developer.pagbank.com.br/reference/objeto-checkout);
- [Webhooks do Checkout](https://developer.pagbank.com.br/reference/webhooks-checkout);
- [Confirmar autenticidade da notificação](https://developer.pagbank.com.br/reference/confirmar-autenticidade-da-notificacao);
- [Chaves públicas e de idempotência](https://developer.pagbank.com.br/docs/chaves-publicas-e-de-idempotencia);
- [Inativar Checkout](https://developer.pagbank.com.br/reference/inativar-checkout);
- [Cancelar pagamento](https://developer.pagbank.com.br/reference/cancelar-pagamento).

## Fluxo

1. O paciente pede o checkout informando apenas o identificador do agendamento e uma chave idempotente.
2. A API bloqueia e valida o agendamento, sua titularidade, seu estado, sua data e o valor salvo no banco.
3. A API converte `decimal(13,2)` para centavos e cria o Checkout com `reference_id=appointment-{id}`.
4. O navegador recebe somente o link HTTPS `PAY`; o token PagBank não atravessa a fronteira do backend.
5. O retorno exibe o estado local e pode solicitar reconciliação ao backend. Parâmetros do redirecionamento não confirmam pagamento.
6. Webhook autenticado ou consulta de reconciliação aplica a transição financeira e registra um evento.
7. Apenas `PAID` confirmado promove a consulta de `pending` para `confirmed`.

## Mapeamento de estados

| PagBank | ViverApp |
|---|---|
| `ACTIVE`, `WAITING` | `pending` |
| `AUTHORIZED`, `IN_ANALYSIS` | `authorized` |
| `PAID` | `paid` |
| `DECLINED` | `failed` |
| `INACTIVE`, `EXPIRED`, `CANCELED` | `canceled` |
| `CANCELED` com total integral devolvido | `refunded` |

`paid` não regride para estados de espera, falha ou cancelamento. `refunded` é terminal. Um pagamento `PAID` pode superar uma tentativa anterior falha do mesmo checkout. Eventos mais antigos são ignorados, salvo uma evolução comprovável para `paid` ou `refunded`.

## Persistência DB-First

`payments` conserva o estado agregado. `payment_events` guarda cada decisão aplicada ou ignorada. `payment_webhook_receipts` guarda somente hashes, identificadores técnicos e resultado de processamento — nunca o payload bruto. `appointment_status_history.actor_account_id` passou a aceitar `NULL` para representar corretamente uma confirmação automática, sem atribuí-la falsamente ao paciente.

