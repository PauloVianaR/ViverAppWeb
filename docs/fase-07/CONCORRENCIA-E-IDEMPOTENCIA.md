# Concorrência e idempotência

## Proteção contra dupla reserva

Criação e reagendamento abrem transação `SERIALIZABLE`. Dentro dela, a API:

1. bloqueia a conta do paciente com `FOR UPDATE`;
2. valida ou reserva a chave idempotente;
3. bloqueia o perfil do médico com `FOR UPDATE`;
4. recalcula o horário contra o estado atual;
5. grava agendamento, histórico e auditoria;
6. confirma tudo numa única transação.

A ordem é sempre paciente e depois médico. Requisições do mesmo paciente são serializadas; pacientes diferentes disputando o mesmo médico são serializados no perfil profissional. Assim, o cálculo não depende apenas dos índices de início exato e também rejeita sobreposições com durações diferentes.

Qualquer fluxo futuro de agenda administrativa deve usar a mesma ordem de locks e o mesmo cálculo centralizado.

## Idempotency-Key

Criação e reagendamento exigem um valor ASCII sem espaços, entre 16 e 100 caracteres. O registro é isolado por operação e paciente, guarda SHA-256 do comando canônico e expira em 24 horas.

- mesma chave e mesmo conteúdo: a resposta anterior é devolvida e o header `Idempotent-Replayed: true` é enviado;
- mesma chave e conteúdo diferente: `409 Conflict`;
- chave expirada ou registro incompleto: `409 Conflict`;
- primeira execução válida: gravação e resposta idempotente são confirmadas juntas.

O cliente web gera uma chave nova para cada confirmação feita pelo usuário. Reenvio de rede da mesma tentativa pode reutilizar a chave sem criar duplicidade.
