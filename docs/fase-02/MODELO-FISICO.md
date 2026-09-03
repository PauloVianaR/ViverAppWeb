# Modelo físico inicial de `viverappweb`

O schema abaixo é a fonte de verdade da persistência. As classes EF em `Infrastructure/Persistence/Generated` são derivadas dele por DB-First e não devem ser editadas manualmente.

## Identidade e acesso

| Tabela | Responsabilidade | Invariantes principais |
|---|---|---|
| `roles` | quatro papéis de referência | códigos fixos para paciente, médico, gestor e administrador |
| `accounts` | identidade central | exatamente um `role_code`; ao menos e-mail ou telefone; senha somente em hash |
| `account_addresses` | endereço opcional 1:1 | pertence a uma conta |
| `external_logins` | vínculo OIDC | apenas Google; no máximo um vínculo Google por conta |
| `auth_sessions` | sessões revogáveis | token de renovação somente em hash; expiração posterior à criação |
| `account_challenges` | login, confirmação e recuperação | canal e-mail/SMS; código e destino somente em hash; expiração e limite de tentativas |
| `account_authenticators` | segredo TOTP por conta | chave protegida por Data Protection e ativação explícita |
| `account_recovery_codes` | recuperação do MFA | código em HMAC e consumo atômico de uso único |
| `account_passkeys` | credenciais WebAuthn | credencial pública vinculada a uma única conta; máximo aplicado pela aplicação |
| `patient_profiles` | dados próprios de paciente | relação 1:1 com conta |
| `doctor_profiles` | dados profissionais | relação 1:1 com conta; registro profissional único |
| `professional_reviews` | histórico de decisões profissionais | profissional e revisor válidos; decisão fechada e justificativa obrigatória para rejeição/bloqueio |

O banco assegura que uma conta aponta para um único papel. A aplicação deverá assegurar, nas fases funcionais, que apenas contas com papel correspondente recebam perfil de paciente ou médico e que mudanças de papel sejam administrativas e auditadas.

## Clínica única e agenda

| Tabela | Responsabilidade | Invariantes principais |
|---|---|---|
| `clinic` | cadastro da única clínica | chave singleton restrita ao valor 1 |
| `specialties` | especialidades disponíveis | nome normalizado único |
| `doctor_specialties` | especialidades por médico | par médico/especialidade único |
| `appointment_types` | serviços/modalidades/preços | duração positiva; preço não negativo |
| `clinic_weekly_hours` | horário geral | dia 0–6 e término após início |
| `doctor_weekly_hours` | disponibilidade profissional | médico, dia e faixa únicos; período válido |
| `holidays` | bloqueios totais ou parciais | data/nome únicos e faixa coerente |
| `appointments` | ciclo de consultas | paciente, médico, criador, modalidade, UTC, preço e estado controlado |
| `appointment_documents` | metadados de anexos | objeto e SHA-256 únicos; não armazena o arquivo no MySQL |

Não existe `clinic_id` nas contas ou nas tabelas operacionais porque todos os registros pertencem à única clínica. A ausência é intencional e impede que um padrão multitenant seja introduzido acidentalmente.

## Financeiro e premium

| Tabela | Responsabilidade | Invariantes principais |
|---|---|---|
| `payments` | pagamento associado a consulta | precisão `decimal(13,2)`, moeda, provedor e IDs externos únicos |
| `premium_plans` | planos configuráveis | preço e desconto com limites |
| `premium_memberships` | adesão do paciente | período e estado controlados |
| `idempotency_records` | repetição segura de comandos | chave única por escopo, hash da requisição e expiração |

## Operação, mensagens e auditoria

| Tabela | Responsabilidade | Invariantes principais |
|---|---|---|
| `outbox_messages` | entrega durável | somente e-mail/SMS, tentativas, lease e dead-letter |
| `audit_events` | trilha de ações críticas | ator, alvo, correlação, instante UTC e metadados mínimos |
| `application_settings` | configuração não secreta | chave única; segredos são proibidos nessa tabela |

## Convenções

- nomes físicos em `snake_case` e classes C# derivadas pelo scaffold;
- datas operacionais persistidas em UTC com precisão de microssegundos;
- valores financeiros em `decimal`, nunca ponto flutuante;
- chaves externas com `RESTRICT` quando exclusão quebraria histórico e `CASCADE` apenas em dependentes inseparáveis;
- checks do MySQL para enums fechados e invariantes locais;
- `row_version` em contas e cadastros mutáveis, marcado como concurrency token em arquivo parcial fora do código gerado;
- segredos, tokens reutilizáveis e códigos de desafio nunca persistidos em texto claro.
