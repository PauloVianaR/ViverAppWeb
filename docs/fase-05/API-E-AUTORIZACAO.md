# API e autorização

Todas as rotas usam o prefixo `/api/v1`, cookie seguro, antiforgery nas mutações, rate limiting e a política padrão de sessão autenticada com MFA concluído quando aplicável.

| Área | Rotas | Leitura | Alteração |
|---|---|---|---|
| Clínica | `/clinic` | gestor, administrador | gestor, administrador |
| Horários da clínica | `/clinic/weekly-hours` | gestor, administrador | gestor, administrador |
| Feriados | `/clinic/holidays` | gestor, administrador | gestor, administrador |
| Especialidades | `/catalog/specialties` | qualquer conta autenticada | gestor, administrador |
| Tipos de atendimento | `/catalog/appointment-types` | qualquer conta autenticada | gestor, administrador |
| Usuários | `/users` | gestor, administrador; acesso individual pelo próprio usuário | próprio usuário; bloqueio/reativação de paciente somente pelo administrador |
| Profissionais | `/professionals` | gestor e administrador; médico acessa o próprio perfil | administrador cria e revisa; gestor altera médicos; cada profissional altera o próprio perfil permitido |
| Disponibilidade médica | `/professionals/{accountId}/weekly-hours` | próprio médico, gestor, administrador | próprio médico, gestor, administrador |

As listagens aceitam paginação limitada a 100 itens, filtros com tamanho máximo e ordenação determinística. Updates e deletes exigem `rowVersion`; uma versão antiga recebe HTTP 409.

As respostas usam DTOs explícitos. Hashes, security stamp, tentativas de login, payloads da outbox, credenciais e entidades EF nunca integram os contratos públicos.
