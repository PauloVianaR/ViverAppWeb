# Fase 31 — Operação, chat e agenda diária

Branch: `codex/fase-31-operacao-chat-agenda`. Esta fase foi solicitada diretamente pelo proprietário após a integração da Fase 30 à `main`. Não envolve implantação, merge ou antecipação das fases 28/29.

## Escopo entregue

1. As duas modalidades de impressão do caixa exibem o total líquido geral, quando o papel tem permissão para consultá-lo, abaixo do total líquido do dia. O cálculo geral usa movimentos até o dia operacional atual, sem aplicar filtros da impressão do dia.
2. Início e conclusão de atendimento gravam um alerta durável para cada Gestor ativo na mesma transação da mudança de estado. Após commit, a API transmite o evento por SignalR. O cabeçalho usa sininho com ponto vermelho para alertas não lidos; os profissionais conservam o feed de chegadas.
3. Chat interno entre Administrador, Gestores, Médicos e Psicólogos ativos. Paciente não acessa nem aparece. Contatos excluem o próprio usuário, aparecem online primeiro e em ordem alfabética, com busca textual. Mensagens e leitura persistem no MySQL; SignalR distribui mensagens, leitura e presença. O balão de conversa pode ser minimizado e há indicadores de não lidas no balão, ícone de chat e sininho. Escrita usa antiforgery, validação de destinatário/papel/tamanho, nonce de idempotência e rate limiting de operação autenticada. Não registrar conteúdo das mensagens em logs.
4. A visualização Dia da equipe mostra seleção de profissional à esquerda (própria agenda para Médico/Psicólogo), mini calendário com dias sem disponibilidade em vermelho e a lista de horários. O plano cruza horários da clínica, disponibilidade recorrente/variável, exceções e feriados, considerando presencial e online; usa `appointments.slot_interval_minutes`, nome real da configuração de intervalos neste projeto. O atendimento aparece no seu início com emoji de status e a próxima linha livre só surge após seu término mais um intervalo. Consultas antigas concluídas/canceladas/faltas continuam ausentes da agenda. Outras visualizações permanecem no formato anterior.
5. `/contato` lê nome, telefone, e-mail e endereço da configuração institucional pública da clínica. Os canais de privacidade e segurança continuam separados conforme as políticas vigentes.
6. CPF do cadastro público recebe máscara progressiva `000.000.000-00`; o código de e-mail é limpo ao passar à etapa de autenticador MFA.
7. Observação interna opcional de até 2000 caracteres para inclusão/edição de paciente por Gestor/Admin e leitura no card. Não há exposição aos perfis de paciente/profissional.
8. Os filtros da tela Atendimentos começam recolhidos também no desktop, nos quatro componentes de perfil (Médico cobre Psicólogo).

## Banco e validação

- Migration `0053__staff_chat_manager_alerts_patient_notes.sql` aplicada integralmente no MySQL local 8.0.41, banco `viverappweb`; rollback de emergência versionado, **não executado**. Scaffold DB-First regenerado a partir do banco local. Nenhuma alteração no banco legado ou em outro projeto.
- Builds Debug da API, Web e solução: sem erros. Testes específicos de chat/alertas, autorização anônima, agenda do Psicólogo, impressão e observação: aprovados no banco local com rollback de dados sintéticos dos novos testes.
- `GET /api/v1/public/clinic` retornou 200; `GET /api/v1/staff/contacts` sem sessão retornou 401; `GET /contato` retornou 200.
- Suíte completa: todos os testes de Web, Security, Persistence, PatientScheduling e ClinicAdministration passaram. Permanecem duas falhas antigas já registradas em `.local/PENDENCIAS.md`: teste de opções de cadastro apontando para porta 1 e teste de prontuário dependente de `viver_migrator@localhost` ausente. A execução paralela da solução também expôs duas falhas intermitentes de integração clínica; ambas passaram isoladamente, e o projeto clínico inteiro passou com 53 testes ao excluir somente o caso do definer ausente. Não declarar a suíte integral verde.
- Inspeção visual autenticada desktop/tablet/celular não concluída: o navegador integrado falhou ao iniciar com `windows sandbox failed: helper_unknown_error: apply deny-read ACLs`, repetido após reset do kernel. O aplicativo foi aberto em aba do Codex para conferência humana, sem alegar validação visual.
