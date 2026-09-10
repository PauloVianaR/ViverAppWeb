# API e regras de negócio

## Endpoints do paciente

Todos exigem sessão autenticada, MFA satisfeita quando aplicável e papel único `patient`.

| Método | Rota | Finalidade |
|---|---|---|
| `GET` | `/api/v1/patient/booking/professionals` | Filtrar profissionais por nome, especialidade, tipo e modalidade |
| `GET` | `/api/v1/patient/booking/slots` | Obter horários livres por até 31 dias |
| `GET` | `/api/v1/patient/appointments` | Listar agenda futura, histórico ou todos os registros |
| `GET` | `/api/v1/patient/appointments/{id}` | Consultar detalhe pertencente ao paciente |
| `POST` | `/api/v1/patient/appointments` | Criar reserva com `Idempotency-Key` |
| `POST` | `/api/v1/patient/appointments/{id}/reschedule` | Criar substituto e encerrar o horário anterior |
| `POST` | `/api/v1/patient/appointments/{id}/cancel` | Cancelar com motivo e `rowVersion` |

Entidades geradas pelo EF nunca são usadas como contratos HTTP. JSON com membros desconhecidos é recusado nos comandos.

## Políticas adotadas

As decisões ainda abertas no roteiro receberam uma base conservadora e configurável:

| Chave | Padrão | Regra |
|---|---:|---|
| `appointments.booking_horizon_days` | 365 | antecedência máxima |
| `appointments.minimum_lead_minutes` | 120 | antecedência mínima para reservar |
| `appointments.cancellation_cutoff_hours` | 24 | limite para o paciente cancelar |
| `appointments.reschedule_cutoff_hours` | 24 | limite para o paciente reagendar |
| `appointments.slot_interval_minutes` | 10 | distância entre inícios oferecidos |

Esses valores não são segredos e residem em `application_settings`. Alterações futuras devem passar por uma API administrativa auditada; não devem ser editadas diretamente em produção.

## Disponibilidade

Um horário só é publicado e aceito quando:

1. o médico e o tipo de atendimento estão ativos;
2. a modalidade é compatível com o tipo;
3. o período está dentro da disponibilidade médica vigente;
4. na modalidade presencial, também está dentro do expediente da clínica;
5. não cruza feriado integral ou parcial;
6. respeita horizonte e antecedência mínima;
7. não cruza agendamento `pending` ou `confirmed` do médico;
8. não cruza outro agendamento ativo do paciente.

O cliente envia data e hora locais. A API resolve o timezone singleton da clínica, rejeita horário local ambíguo/inexistente e persiste `starts_at_utc` e `ends_at_utc`.

## Reagendamento e cancelamento

O reagendamento atualiza data e hora do mesmo atendimento, mantendo ID, número humano, pagamento e o estado principal (`pending` ou `confirmed`). Cada alteração recebe sequência própria em `appointment_reschedule_history`, com horário anterior, novo horário, autor, motivo e instante. Registros antigos no formato substituto continuam legíveis, mas apenas o sucessor aparece nas listagens.

O cancelamento exige motivo com 5 a 500 caracteres. Ambas as operações exigem a `rowVersion` atual, pertencimento ao paciente, status `pending` ou `confirmed` e antecedência configurada.
