# Fase 14 — Matriz final de paridade MAUI → Web

Esta matriz fecha o inventário autorizado das Fases 10 a 14. “Coberto” preserva o resultado funcional em contrato Web próprio. “Substituído” registra uma decisão já aprovada no plano da Fase 14; não significa funcionalidade ausente.

## 33 páginas principais

| ID | Tela MAUI / comportamento | Regra e papel | Endpoint / página Web | Evidência automatizada | Estado |
|---|---|---|---|---|---|
| PG-01 | `AdminAnalyticsView` — KPIs e gráficos | Administrador + MFA; agregação no servidor | `GET /api/v1/administrator/analytics`; `/administracao/analytics` | contratos Admin/Web + integração MySQL | Coberto |
| PG-02 | `AdminAppointmentsManagementView` — previstos/histórico e filtros | Administrador + MFA | `/api/v1/administrator/agenda`; `/administracao/consultas` | contratos Admin/Web | Coberto |
| PG-03 | `AdminClinicView` — clínica, serviços, horários e sistema | Administrador + MFA + step-up | `/api/v1/clinic`, `/catalog`, `/administrator/settings`; `/administracao/clinica` | contratos clínica + schema | Coberto |
| PG-04 | `AdminHomeView` — indicadores, atalhos e aprovações | Administrador + MFA | `GET /api/v1/administrator/home`; `/administracao` | contratos Admin/Web | Coberto |
| PG-05 | `AdminMainPage` — navegação vermelha | Administrador + MFA | shell com seis áreas administrativas | teste de navegação | Coberto |
| PG-06 | `AdminNotificationView` — caixa, filtros, leitura e dispensa | Administrador + MFA; coleção durável | `/api/v1/administrator/notifications`; `/administracao/notificacoes` | migration 0019 + contratos | Coberto |
| PG-07 | `AdminPremiumManagementPage` — análise e cancelamento | Administrador + MFA + step-up | `/administrator/premium`; `/administracao/usuarios` | contratos Admin | Coberto |
| PG-08 | `AdminUserManagementView` — usuários, aprovações, bloqueio e online | Administrador + MFA + step-up; papel único | `/users`, `/professionals`, `/administrator/users`; `/administracao/usuarios` | políticas e contratos | Coberto |
| PG-09 | `DoctorAgendaView` | Médico; apenas própria agenda | `/api/v1/doctor/experience/agenda`; `/medico/agenda` | suíte clínica/Web | Coberto |
| PG-10 | `DoctorHistoricView` | Médico; histórico próprio | `/medico/historico` | suíte clínica/Web | Coberto |
| PG-11 | `DoctorHomeView` | Médico | `/api/v1/doctor/experience/home`; `/medico` | suíte clínica/Web | Coberto |
| PG-12 | `DoctorMainPage` | Médico | shell médico | teste de navegação | Coberto |
| PG-13 | `DoctorPatientListView` | Médico; vínculo obrigatório | `/medico/pacientes` | suíte clínica negativa | Coberto |
| PG-14 | `DoctorProfileView` | Médico; contato/Google com reautenticação | `/medico/perfil` | suíte identidade/Web | Coberto |
| PG-15 | `DoctorSchedulePage` | Médico agenda para paciente vinculado | `/medico/pacientes/{id}/agendar` | suíte agendamento | Coberto |
| PG-16 | `LoginRegisterPage` | Google padrão; e-mail/telefone alternativos; sem cadastro Admin | `/api/v1/auth`; `/acesso` | suíte identidade/Web | Coberto |
| PG-17 | `OnlinePage` | Paciente/Médico vinculados ao atendimento; sem gravação | hub autenticado; páginas `/video` | suíte vídeo/segurança | Coberto |
| PG-18 | `PaymentSuccessfulPage` | retorno consulta o estado real | `/api/v1/payments`; `/paciente/pagamentos` | suíte PagBank/Web | Coberto |
| PG-19 | `ManagerAgendaView` | Gestor; clínica única | `/api/v1/manager/agenda`; `/gestao/agenda` | suíte gestão/Web | Coberto |
| PG-20 | `ManagerHistoricView` | Gestor; metadados sem conteúdo clínico | `/gestao/historico` | suíte gestão | Coberto |
| PG-21 | `ManagerHomeView` | Gestor | `/api/v1/manager/home`; `/gestao` | suíte gestão/Web | Coberto |
| PG-22 | `ManagerMainPage` | Gestor | shell laranja | teste de navegação | Coberto |
| PG-23 | `ManagerPatientListView` | Gestor; diretório sem Administradores | `/gestao/pacientes` | políticas negativas | Coberto |
| PG-24 | `ManagerProfileView` | Gestor | `/gestao/perfil` | suíte identidade/gestão | Coberto |
| PG-25 | `ManagerSchedulePage` | Gestor agenda em nome do paciente | `/gestao/pacientes/{id}/agendar` | suíte agendamento | Coberto |
| PG-26 | `PatientAgendaView` | Paciente; ownership | `/paciente/agenda` | suíte paciente/agendamento | Coberto |
| PG-27 | `PatientHomeView` | Paciente | `/api/v1/patient/experience/home`; `/paciente` | suíte paciente/Web | Coberto |
| PG-28 | `PatientMainPage` | Paciente | shell azul | teste de navegação | Coberto |
| PG-29 | `PatientPaymentView` | Paciente; checkout servidor | `/paciente/pagamentos` | suíte PagBank/paciente | Coberto |
| PG-30 | `PatientProfileView` | Paciente; perfil, segurança e Premium | `/paciente/perfil` | suíte paciente/identidade | Coberto |
| PG-31 | `PatientScheduleView` | Paciente; slots/preço no servidor | `/paciente/agendar` | suíte agendamento | Coberto |
| PG-32 | `PatientTabbedPage` | Paciente; navegação responsiva | shell paciente | teste de navegação | Coberto |
| PG-33 | `PatientWaitPaymentPage` | Paciente; espera/reconciliação de cobrança | `/paciente/pagamentos` com consulta de estado | suíte PagBank | Coberto |

## 13 popups

| ID | Popup MAUI / comando | Destino Web | Segurança / decisão | Evidência | Estado |
|---|---|---|---|---|---|
| PP-01 | `AppointmentAttachmentsPopup` | painel/dialog de documentos do atendimento | conteúdo somente para Paciente/Médico autorizado; Admin/Gestor veem metadados | suíte clínica negativa | Coberto |
| PP-02 | `CancelSchedulePopup` | dialogs de cancelamento por papel | motivo, cutoff, versão, auditoria e step-up Admin | suíte agendamento | Coberto |
| PP-03 | `ChangePasswordPopup` | `/seguranca` | hash adaptativo e reautenticação | suíte identidade | Coberto |
| PP-04 | `CompleteSchedulePopup` | ação de conclusão médica | somente Médico responsável | suíte clínica | Coberto |
| PP-05 | `ConfirmPaymentPopup` | detalhe Gestor/Admin | valor servidor, idempotência e ledger | suíte gestão/PagBank | Coberto |
| PP-06 | `EditAppointmentPopup` | reagendamento por papel | slot servidor, concorrência e auditoria | suíte agendamento | Coberto |
| PP-07 | `LoadingPopup` | skeleton/estado ocupado acessível | feedback sem bloquear leitor de tela | teste Web | Coberto |
| PP-08 | `MedicalReportPopup` | editor/versionamento médico | conteúdo bloqueado a Admin/Gestor | suíte clínica negativa | Coberto |
| PP-09 | `PatientEditPopup` | perfil/detalhe do paciente por papel | DTO mínimo, ownership/policy | suítes paciente/médico/gestor | Coberto |
| PP-10 | `PremiumUserRequestAnalysisPopup` | seção Premium administrativa/gerencial | comprovante privado, decisão concorrente, step-up Admin | contratos + auditoria | Coberto |
| PP-11 | `RateSchedulePopup` | avaliação do atendimento | Paciente elegível; uma avaliação | suíte paciente | Coberto |
| PP-12 | `ReschedulePopup` | dialogs de reagendamento | máquina de estados e idempotência | suíte agendamento | Coberto |
| PP-13 | `ScheduleDetailsPopup` | páginas de detalhe responsivas | policy por papel e metadados mínimos | suítes clínicas/Web | Coberto |

## Substituições transversais aprovadas

| Origem | Decisão Web | Estado |
|---|---|---|
| Firebase/push | Caixa durável + e-mail/SMS | Substituído por decisão explícita |
| Senha reversível | Hash adaptativo e redefinição | Substituído por decisão explícita |
| Multiclínica | Clínica singleton | Substituído por decisão explícita |
| Múltiplos papéis | Exatamente um papel por conta | Substituído por decisão explícita |
| Cadastro público de Administrador | Provisionamento seguro | Substituído por decisão explícita |
| Google Pay separado | PagBank Checkout | Substituído por decisão explícita |
| Gravação de vídeo | Desativada; nenhuma captura implementada | Substituído por decisão explícita |
| Laudo completo para Admin/Gestor | Somente metadados; sigilo clínico preservado | Substituído por decisão explícita |
| Regras no cliente | API como autoridade | Substituído por decisão explícita |
| Layout estritamente móvel | Web responsiva com sidebar/navegação inferior | Substituído por decisão explícita |

Não existem itens `ausente` ou `parcial` nesta matriz. Homologações que dependem de provedores, documentos reais ou navegadores externos continuam registradas em `.local/PENDENCIAS.md` e não alteram a cobertura funcional local.
