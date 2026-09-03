# Fase 7 — Agenda e agendamento do paciente

## Resultado

A jornada principal do paciente foi reconstruída para web sobre dados reais do banco `viverappweb`. O paciente autenticado pode filtrar profissionais, consultar horários, criar um agendamento, ver sua agenda futura, reagendar e cancelar. A interface não decide permissões nem disponibilidade: todas as regras são refeitas pela API no instante da operação.

O cálculo usa o fuso configurado na clínica, converte os períodos para UTC somente na fronteira da persistência e trata intervalos como semiabertos (`início` incluído e `fim` excluído). Horários inexistentes ou ambíguos por mudança de fuso são recusados.

## Entregas

- migration `0007__patient_scheduling.sql`, aplicada no MySQL 8.0.41 antes do scaffold DB-First;
- vínculo unívoco entre o agendamento original e seu substituto;
- histórico de status, datas, ator e motivo em `appointment_status_history`;
- índices de período por médico e paciente;
- políticas operacionais configuráveis em `application_settings`;
- busca de médicos ativos e aprovados, com especialidades e disponibilidade cadastrada;
- cálculo de horários com agenda médica, agenda da clínica, feriados, modalidade, antecedência e conflitos;
- criação e reagendamento idempotentes;
- bloqueio transacional de paciente e médico para impedir dupla reserva;
- agenda futura e detalhes limitados ao próprio paciente;
- cancelamento e reagendamento com concorrência otimista;
- página responsiva em `/paciente/agendar` e `/paciente/agenda`;
- testes de contrato, timezone, limites de intervalo, feriado, conflito, concorrência real e replay idempotente.

## Limites da fase

Esta fase não confirma pagamentos, envia lembretes nem entrega a agenda operacional de médicos e gestores. O agendamento nasce como `pending`; PagBank, notificações e demais transições pertencem às fases específicas do roteiro.

Veja também [API e regras de negócio](API-E-REGRAS.md), [concorrência e idempotência](CONCORRENCIA-E-IDEMPOTENCIA.md) e [verificações](VERIFICACOES.md).
