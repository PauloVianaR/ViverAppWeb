# Segurança e idempotência

## Autenticidade do webhook

A API calcula `SHA-256(token + "-" + corpo_bruto)` exatamente como recebido, sem reformatar JSON, e compara o resultado em tempo constante com `x-authenticity-token`. Assinatura ausente, malformada ou divergente recebe `401` antes de qualquer escrita financeira.

Depois da autenticação:

- o SHA-256 do payload tem unicidade por provedor e bloqueia replay;
- o `reference_id`, o checkout ou a cobrança devem localizar um pagamento existente;
- o total em centavos, quando presente, deve ser idêntico ao valor persistido;
- a transição passa pela máquina de estados e pelo controle de ordem temporal;
- recibo, evento, pagamento e confirmação da consulta são salvos transacionalmente.

O corpo do webhook não é armazenado nem enviado a logs para reduzir exposição de dados pessoais e financeiros.

## Idempotência em camadas

- cliente → ViverApp: `Idempotency-Key`, escopo por paciente, hash do pedido e resposta persistida por 48 horas;
- ViverApp → PagBank: chave alfanumérica estável derivada do registro de pagamento;
- webhook: hash único do corpo bruto autenticado;
- evento financeiro: impressão digital única por pagamento;
- banco: um pagamento e um checkout por agendamento, protegidos por índices únicos e transação serializável.

## Fronteiras de autorização

- paciente só cria e consulta pagamento do próprio agendamento;
- webhook é anônimo apenas na autenticação da aplicação e exige a assinatura do PagBank;
- reembolso usa política de gestão, MFA da sessão, rate limiting, antiforgery, idempotência, concorrência otimista e auditoria;
- URLs da API PagBank são aceitas somente nos hosts oficiais do ambiente e sempre via HTTPS;
- a URL `PAY` é validada como HTTPS antes de ser persistida e novamente antes do redirecionamento do navegador.

