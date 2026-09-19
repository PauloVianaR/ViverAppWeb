# Fase 19 — matriz de autorização e contratos

| Recurso | Paciente | Médico | Psicólogo | Gestor | Administrador |
| --- | --- | --- | --- | --- | --- |
| Agenda visual | Apenas próprios atendimentos | Apenas sua agenda | Apenas sua agenda | Clínica; filtro por profissional | Clínica; filtro por profissional |
| Atendimentos | Apenas próprios | Apenas atribuídos | Apenas atribuídos | Operação da clínica | Operação da clínica com MFA nas ações elevadas |
| Pacientes e prontuário | Próprios dados | Vínculo ou atendimento autorizado | Vínculo ou atendimento autorizado | Leitura/escrita conforme configuração | Leitura/gestão conforme política e MFA |
| Cadastro público | Ativação após verificação | Aprovação após verificação | Aprovação após verificação | Aprovação após verificação | Não permitido |
| Perfil profissional | Não se aplica | CRM obrigatório | CRP obrigatório | Consulta/gestão autorizada | Gestão e aprovação |
| Tipos de atendimento e preço | Consulta | Consulta/oferta | Consulta/oferta | Edição se configuração permitir | Edição |
| Checkout/pagamento | Apenas atendimento próprio cobrado | Sem ação financeira | Sem ação financeira | Presencial se cobrado | Presencial se cobrado |

O endpoint `GET /api/v1/calendar` usa a identidade autenticada do servidor. `professionalAccountId` é aceito apenas para Gestor/Administrador; Médico e Psicólogo não podem selecionar outro profissional, e o Paciente não pode fornecer esse filtro. A projeção anual retorna datas, contagens e estados, sem nomes ou conteúdo clínico. As rotas antigas de Histórico/Consultas redirecionam para `/atendimentos`.

Os contratos de catálogo e atendimento contêm `requiresPayment`. O atendimento preserva esse indicador como snapshot: tipos gratuitos são criados com preço zero e estado `confirmed`; tentativas de checkout ou confirmação financeira são rejeitadas e não criam movimento de caixa. O banco mantém a restrição de consistência entre pagamento e atendimento cobrado.
