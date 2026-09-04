# API e autorização clínica

Base: `/api/v1/clinical`, sempre com autenticação, papel clínico autorizado, cookie seguro, antiforgery em escritas, rate limiting e respostas `ProblemDetails` sem detalhes internos.

| Operação | Médico | Gestor | Administrador |
|---|---|---|---|
| contexto e profissionais | somente o próprio | todos os médicos ativos | todos os médicos ativos |
| agenda, histórico e detalhe | consultas atribuídas | todas da clínica única | todas da clínica única |
| pacientes | vinculados às próprias consultas | vinculados à operação da clínica | vinculados à operação da clínica |
| conteúdo do relatório | somente consulta atribuída | nunca | nunca |
| salvar rascunho/concluir | somente consulta atribuída | nunca | nunca |
| registrar falta | consulta atribuída | qualquer consulta permitida | qualquer consulta permitida |
| disponibilidade | própria | médicos da clínica | médicos da clínica |

Endpoints principais:

- `GET /context`;
- `GET /appointments` e `GET /appointments/{id}`;
- `GET /patients`;
- `PUT /appointments/{id}/medical-report`;
- `POST /appointments/{id}/complete`;
- `POST /appointments/{id}/no-show`;
- `GET /api/v1/patient/appointments/{id}/medical-report` para o próprio paciente após publicação.

O filtro de ownership faz parte da consulta SQL, evitando o padrão inseguro de carregar primeiro e autorizar depois. Um médico não recebe confirmação sobre a existência de consultas atribuídas a outro profissional. Escritas usam `row_version`; disputas retornam `409` e exigem recarga.
