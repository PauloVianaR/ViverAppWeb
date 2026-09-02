# Inventário do legado permitido

## 1. Visão de componentes

| Componente | Responsabilidade observada | Destino previsto |
|---|---|---|
| `ViverAppMobileNew` | UI MAUI, navegação, estado local, chamadas HTTP, cálculo de agenda, checkout e WebRTC | Redesenhar em Blazor; mover regras autoritativas para a API |
| `ViverAppApi` | CRUDs, autenticação JWT, regras de agenda/premium/pagamento e Backblaze B2 | Reescrever como ASP.NET Core API modular |
| `ViverAppEmailWorker` | Polling de filas MySQL para SMTP, SMS e Firebase push | Migrar e-mail e SMSBarato para hosted services duráveis; remover Firebase/push |
| `ViverAppVideoHub` | Hub SignalR para sinalização e contagem de sala em memória | Módulo de vídeo autenticado dentro da API |
| `ViverApp.Shared` | Entidades DB-First e DTOs compartilhados entre cliente e servidor | Não reutilizar; gerar persistência a partir de `viverappweb` e criar contratos próprios |

## 2. Perfis e navegação MAUI

| Perfil | Áreas observadas |
|---|---|
| Não autenticado | login por e-mail/telefone, cadastro, recuperação e confirmação por e-mail/SMS; ambos serão mantidos no novo produto |
| Paciente | início, novo agendamento, agenda/histórico, pagamento, perfil/premium, chamada online |
| Médico | início, agenda, pacientes, histórico, perfil/disponibilidade, agendamento para paciente, chamada online |
| Gestor | início, agenda, pacientes, histórico, perfil, agendamento e confirmação de pagamento |
| Administrador | início/aprovações, clínica, serviços, agendas, analytics, notificações, usuários e premium |

O MAUI possui 46 arquivos XAML, incluindo 33 páginas principais e 13 popups. A navegação é tabulada e estreita, adequada ao telefone; tabelas, filtros e operações administrativas precisarão de padrões específicos para desktop e tablet.

## 3. Superfície HTTP existente

A API possui 111 endpoints sob `api/v1`, distribuídos em 17 controllers.

| Controller | Capacidades observadas | Tratamento no novo sistema |
|---|---|---|
| `Auth` | modo do app, descoberta de tipo, cadastro, confirmações, login, refresh, senha e tokens | Redesenhar integralmente com Identity, Google, hash, MFA e políticas |
| `User` | listar usuários/médicos, detalhes, criar, editar e excluir | Separar identidade, perfil, papéis e administração |
| `Clinic` | CRUD da clínica | Módulo Clínica singleton |
| `Appointment` | catálogo de serviços/tipos de atendimento | Renomear conceitualmente para oferta/serviço clínico |
| `AvailabilityClinic` | horários recorrentes da clínica | Agenda/Disponibilidade |
| `AvailabilityDoctor` | horários recorrentes e semanais de médicos | Agenda/Disponibilidade |
| `Holiday` | feriados e permissão de agendar | Agenda/Disponibilidade |
| `Config` | flags operacionais, durações, intervalo, desconto e versão | Configuração tipada e auditada; não usar IDs mágicos |
| `DoctorProps` | CRM, título, especialidade principal, rating e limites | Perfil profissional e regras de credenciamento |
| `SpecialtysDoctor` | vínculo entre médico e serviço/especialidade | Catálogo profissional normalizado |
| `Schedule` | agenda futura/histórica, contagem, criar, editar, lote e excluir | Módulo Agendamentos com máquina de estados e concorrência |
| `ScheduleAttachments` | upload/download/exclusão de anexos | Documentos privados com autorização e antivírus |
| `Payment` | histórico, métricas, checagem e CRUD | Ledger/registro financeiro append-oriented |
| `PagBank` | criação de checkout e webhook | Integração PagBank isolada, assinada e idempotente |
| `PremiumUser` | solicitação, arquivo, análise e cancelamento | Módulo Benefícios/Premium com workflow |
| `Notification` | CRUD de notificações | Caixa de entrada e preferências; escrita via outbox |
| `Error` | registro estático de erros | Substituir por observabilidade estruturada e auditoria |

Problemas transversais observados: entidades EF expostas diretamente, listagens irrestritas, contratos inconsistentes, filtros por IDs fornecidos pelo cliente, mensagens internas devolvidas em erros e mistura de regra, persistência e integração dentro dos controllers.

## 4. Estrutura de dados legada inferida

O contexto legado contém 22 conjuntos:

| Grupo | Tabelas/entidades observadas | Papel |
|---|---|---|
| Identidade | `user`, `user_token` | conta, perfil, papel único, status, senha reversível e refresh token |
| Clínica/catálogo | `clinic`, `appointment`, `appointment_type`, `config`, `holiday` | clínica, serviços, tipos e parâmetros globais |
| Profissionais | `doctor_props`, `specialtys_doctor` | propriedades e ofertas associadas ao médico |
| Disponibilidade | `availability_clinic`, `availability_doctor`, `availability_doctor_week` | regras recorrentes e exceções semanais |
| Atendimento | `schedule`, `schedule_attachments` | agendamento, relatório, avaliação e anexos |
| Financeiro | `payment`, `payment_type` | confirmação e histórico de pagamento |
| Premium | `premium_user` | solicitação, status e documento de plano de saúde |
| Comunicação | `notification`, `email_queue`, `email_confirmation`, `sms_queue` | mensagens, filas e códigos de confirmação |
| Diagnóstico | `error` | erros persistidos sem modelo adequado de observabilidade |

Relações centrais inferidas:

- um usuário pode atuar como paciente ou profissional conforme um inteiro `usertype`;
- `doctor_props` é opcional e um-para-um com usuário médico;
- um médico possui disponibilidades recorrentes/semanais e vínculos de especialidade/serviço;
- um agendamento liga paciente, médico, clínica e serviço;
- um agendamento possui pagamentos e anexos;
- uma solicitação premium pertence a um paciente e guarda metadados de documento;
- notificações usam `targetid`, mas o mapeamento legado não demonstra chave estrangeira;
- filas de e-mail e SMS têm status próprio e polling periódico.

Esse inventário **não** define o schema novo. `viverappweb` será desenhado na Fase 2 e seus nomes, chaves, constraints e contratos não serão derivados automaticamente das classes de `ViverApp.Shared`.

## 5. Regras configuráveis observadas

O legado usa IDs/enum para:

- aplicativo disponível e modo mestre;
- permissão de chamadas online;
- permissão e prazo de cancelamento;
- horizonte máximo de agendamento;
- notificação por e-mail/push no legado; e-mail e SMS serão mantidos, sem push;
- ambiente de produção;
- duração padrão de consulta, exame e cirurgia;
- intervalo entre atendimentos;
- percentual de desconto premium;
- versão mínima do cliente.

No novo sistema, configurações terão chave semântica, tipo, validação, valor padrão, histórico e autorização de alteração.

## 6. Regras de agenda encontradas

- duração vem do serviço ou do padrão por tipo: consulta, exame ou cirurgia;
- um intervalo configurável é inserido entre slots;
- a disponibilidade presencial é a interseção entre médico e clínica;
- consulta online depende da oferta e da habilitação do médico;
- há disponibilidade recorrente por dia da semana e alternativa semanal por data;
- feriados podem bloquear agendamento;
- slots ocupados são removidos do cálculo;
- o legado verifica conflito apenas por médico e instante exato ao gravar;
- paciente cria agendamento como pendente; médico/gestor criam como confirmado;
- todos nascem com pagamento pendente no fluxo observado;
- desconto premium é calculado no cliente;
- cancelamento, reagendamento, conclusão, relatório, anexos, feedback e nota alteram o ciclo do atendimento.

O cálculo de disponibilidade e preço deve sair do navegador e ser repetido de forma transacional pela API.

## 7. Processamento assíncrono

| Worker | Comportamento atual | Riscos principais |
|---|---|---|
| E-mail | busca pendentes, paraleliza, até cinco tentativas e backoff fixo | claim não atômico, possível duplicidade, atraso dentro do worker e SMTP acoplado |
| SMS | busca lotes de cinco, tenta claim por `UPDATE`, envia via provedor | ausência de retry durável uniforme, payload sensível e estados limitados |
| Push | busca notificações, envia Firebase e marca enviada | arquivo de credencial local, claim não atômico, itens ignorados podem permanecer pendentes |

Destino: transactional outbox, inbox, claim atômico no MySQL 8.0.41, idempotência, backoff com jitter, dead-letter, métricas e console administrativo auditado.

## 8. Vídeo

O VideoHub fornece `JoinRoom`, `LeaveRoom`, `SendSignal` e contagem de conexões. Salas vivem em um dicionário estático, não há autenticação/autorização visível e a política de origem produtiva aceita qualquer origem com credenciais. O certificado de debug também está acoplado a caminho e senha fixos.

Destino: SignalR dentro da API, sala ligada a agendamento autorizado, grant efêmero, schema/limite de mensagens, presença distribuída e STUN/TURN com credenciais temporárias.

## 9. Integrações externas

| Integração | Uso observado | Decisão inicial |
|---|---|---|
| PagBank Checkout | criar checkout, redirecionar e receber webhook | Reimplementar conforme documentação oficial vigente na Fase 9 |
| Backblaze B2/S3 | documentos premium e anexos | Migrar para Cloudflare R2 na Fase 10 |
| SMTP | confirmação, recuperação e mensagens | Encapsular provedor e usar outbox |
| SMS Barato | confirmação e recuperação | Manter com segredo externo, limites, retry e auditoria |
| Firebase Cloud Messaging | push Android | Remover; Firebase/push não fazem parte do novo produto |
| OpenCEP | preenchimento de endereço | Chamada pelo backend com validação/cache/timeout |
| Google Maps/Apple Maps/WhatsApp | links externos no cliente | Gerar links seguros e consentidos na UI |
| SignalR/WebRTCme | videochamada | SignalR API + WebRTC nativo do browser e TURN |
| Syncfusion/LiveCharts | calendário/gráficos MAUI | Não copiar dependência; avaliar componentes web na fase visual |

## 10. Configuração encontrada, sem valores

- API: JWT, três connection strings, URLs/tokens PagBank e logging;
- worker legado: database, SMTP, OneSignal/Firebase, SMS, polling e concorrência; migrar SMTP/e-mail e SMSBarato, sem push;
- VideoHub: logging e hosts;
- MAUI: URLs de API/hub/PagBank, versão, localhost e devtools.

Somente `LocalConnection` e PagBank foram transferidos para User Secrets da nova API na Fase 0. Os demais provedores terão segredos novos, rotacionados e definidos somente quando suas fases forem autorizadas.

## 11. Divergência do nome do database legado

A inspeção segura do nome — sem revelar host, usuário ou senha — mostrou que `LocalConnection` seleciona `viverappmobile`, nome confirmado pelo proprietário. Portanto:

- ambos devem ser considerados nomes distintos;
- nenhum deles pode receber escrita;
- a Fase 2 deve abortar antes de DDL se o alvo não for exatamente `viverappweb`;
- nenhuma inferência de que um nome é erro de digitação pode ser feita pelo agente.
