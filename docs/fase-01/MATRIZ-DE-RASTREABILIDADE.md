# Matriz de rastreabilidade legado → novo sistema

Esta matriz registra intenção funcional. “Redesenhar” significa preservar o resultado útil sem copiar implementação, contratos ou schema.

| ID | Origem observada | Comportamento | Destino | Fase | Decisão |
|---|---|---|---|---:|---|
| MAP-001 | Login/Register MAUI + `AuthController` | login por e-mail/telefone | Identidade | 4/10 | Redesenhar API e entregar experiência Web |
| MAP-002 | `AuthController` | senha AES/ECB reversível | Identidade | 4 | Substituir por hash e migrador temporário |
| MAP-003 | `AuthController` | confirmação por e-mail/SMS | Identidade + Comunicação | 4/10/16 | Manter ambos; redesenhar com token único, hash e uso único |
| MAP-004 | `AuthController` | refresh JWT persistido | Sessões | 4/10 | Substituir por cookie/BFF, sessões revogáveis e gestão Web |
| MAP-005 | Requisito do proprietário | login Google | Identidade | 4/10 | Implementar e concluir onboarding/roteamento por papel |
| MAP-006 | `UserController` | usuários e aprovação | Perfis + Administração | 4/5/10/14/19 | Separar conta, perfil, papel e workflow |
| MAP-007 | `DoctorPropsController` | CRM/título/especialidade/limites | Profissionais | 5/10/12/14 | Redesenhar cadastro, perfil e administração |
| MAP-008 | `ClinicController` | clínica e endereço | Clínica | 5/14 | Redesenhar como cadastro singleton da clínica única |
| MAP-009 | `AppointmentController` | serviço, tipo, duração, preço, online | Catálogo | 5/11/12/14 | Renomear, normalizar e entregar nas jornadas |
| MAP-010 | `SpecialtysDoctorController` | serviço/especialidade por médico | Profissionais + Catálogo | 5/12/14 | Redesenhar relação e gestão Web |
| MAP-011 | `AvailabilityClinicController` | agenda recorrente da clínica | Disponibilidade | 5/7/11/12/14 | Redesenhar com timezone/constraints e UI por perfil |
| MAP-012 | `AvailabilityDoctorController` | agenda recorrente/semanal do médico | Disponibilidade | 5/7/11/12 | Redesenhar com exceções explícitas e UI Web |
| MAP-013 | `HolidayController` | dias bloqueados/permitidos | Disponibilidade | 5/7/11/14 | Redesenhar calendário e recorrência |
| MAP-014 | `ConfigController` | flags e números por ID | Configuração | 5/14 | Substituir IDs mágicos por chaves tipadas e UI administrativa |
| MAP-015 | `Scheduler` no MAUI | geração de slots | Agendamentos | 7/11/12/13 | Mover integralmente para servidor e jornadas Web |
| MAP-016 | `ScheduleController` | criar e consultar agenda | Agendamentos | 7/11–14 | Redesenhar com ownership, concorrência e UI por papel |
| MAP-017 | VMs de agenda | cancelar/reagendar | Agendamentos | 7/8/11–14 | Máquina de estados, política servidor e ações Web |
| MAP-018 | VMs médico/gestor | agendar por paciente | Agendamentos | 8/12/13 | Manter com permissão, auditoria e UI dedicada |
| MAP-019 | popups/agenda | concluir e registrar relatório | Encontro clínico | 8/11–14 | Separar agendamento de registro clínico e aplicar sigilo por papel |
| MAP-020 | agenda/histórico | feedback e avaliação | Qualidade | 7/8/11–14 | Manter com elegibilidade, unicidade e consumo autorizado |
| MAP-021 | `PaymentController` | pagamento presencial/histórico | Financeiro | 9/11/13/14/18 | Redesenhar como registros auditáveis |
| MAP-022 | `PagbankController` | checkout a partir de payload do cliente | PagBank | 9 | Calcular pedido no servidor |
| MAP-023 | `PagbankController` | webhook sem autenticidade/status rigoroso | PagBank | 9 | Substituir por inbox assinada/idempotente |
| MAP-024 | `PremiumUserController` | solicitação premium com documento | Benefícios + Documentos | 11/13–15/18 | Redesenhar funcionalmente e depois migrar/reconciliar documentos |
| MAP-025 | `ScheduleAttachmentsController` | anexos no B2 | Documentos | 11/12/14/15 | Recriar as jornadas e migrar o armazenamento para R2 privado |
| MAP-026 | `B2StorageService` | nomes públicos e credenciais em código | Documentos | 15 | Descartar implementação e rotacionar segredo |
| MAP-027 | `EmailWorker` | fila SMTP por polling | Comunicação | 16 | Hosted service + outbox/delivery |
| MAP-028 | `SmsWorker` | fila SMS | Comunicação | 16 | Manter via SMSBarato com outbox, claim atômico e idempotência |
| MAP-029 | `NotificationWorker` | Firebase push | Comunicação | — | Remover; Firebase/push não fazem parte do novo produto |
| MAP-030 | `NotificationController` | caixa/notificações por CRUD | Comunicação | 14/16 | Recriar a caixa e depois endurecer entrega/operação |
| MAP-031 | `VideoHub` | sinalização WebRTC anônima | Vídeo | 11/12/17 | Reimplementar autenticado na API e endurecer para produção |
| MAP-032 | `RoomController` | contagem local de sala | Vídeo | 11/12/17 | Entregar presença autorizada e depois distribuí-la |
| MAP-033 | `OnlinePage` MAUI | câmera/microfone/reconexão | UI de teleconsulta | 11/12/17 | Recriar para browser e endurecer a infraestrutura |
| MAP-034 | gráficos do admin | receita, volume, satisfação e distribuições | Analytics | 14/19 | Calcular/agregar no servidor e endurecer a operação |
| MAP-035 | `ErrorController`/tabela `error` | persistência de exceções | Observabilidade | 3 | Substituir por logs/traces/alertas redigidos |
| MAP-036 | header `X-App-Version` | bloqueio por versão no middleware | Compatibilidade | 3/21 | Substituir por versionamento/depreciação explícitos |
| MAP-037 | `AppMode`/configs | manutenção e desligamento | Operação | 3/14/19 | Feature flag/maintenance mode auditável |
| MAP-038 | OpenCEP | preenchimento de endereço | Integração de endereço | 5 | Proxy backend resiliente e opcional |
| MAP-039 | Maps/telefone/WhatsApp | ações externas | UX | 6/7/8/11–13 | Manter como links seguros e acessíveis nas jornadas finais |
| MAP-040 | deep link de sucesso | retorno visual PagBank | PagBank + UI | 9/11 | Retorno consulta estado real; não confirma pagamento |

## Cobertura por módulo

| Módulo novo | Itens da matriz |
|---|---|
| Identidade e Acesso | MAP-001 a MAP-006 |
| Clínica, Catálogo e Profissionais | MAP-007 a MAP-014, MAP-038 |
| Agendamentos e Encontro Clínico | MAP-015 a MAP-020, MAP-039 |
| Pagamentos e Benefícios | MAP-021 a MAP-024, MAP-040 |
| Documentos | MAP-024 a MAP-026 |
| Comunicação | MAP-003, MAP-027 a MAP-030 |
| Vídeo | MAP-031 a MAP-033 |
| Administração/Observabilidade | MAP-034 a MAP-037 |

Nenhum item autoriza reuso de `ViverApp.Shared`: os contratos novos serão escritos somente após a estrutura de `viverappweb` ser aprovada e scaffoldada.
