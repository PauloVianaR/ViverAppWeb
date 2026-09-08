# Fase 14 — Implementação e validação

## Resultado

A experiência do Administrador foi implementada na branch `codex/fase-14-experiencia-administrador`, sem antecipar a Fase 15. A área exige papel Administrador, MFA e sessão curta; ações de alto impacto também exigem autenticação recente. A matriz das Fases 10 a 14 registra as 33 páginas e os 13 popups como cobertos ou substituídos por decisão já aprovada.

## Entregas funcionais

- início administrativo com identidade vermelha, indicadores, agenda do dia, atalhos e fila de Médico/Gestor pendente;
- clínica única com edição auditável dos dados, serviços, horários semanais, feriados recorrentes ou pontuais e configurações tipadas do sistema;
- consultas planejadas e histórico com filtros por texto, período, estado, Médico, tipo, modalidade e pagamento, além de ordenação, paginação e cards responsivos;
- detalhe operacional com cancelamento, reagendamento e confirmação de pagamento presencial, sem revelar conteúdo clínico reservado;
- analytics calculado no servidor para períodos predefinidos ou intervalo limitado, com KPIs e paridade explícita dos nove gráficos MAUI: Evolução da Receita, Receita vs Consultas, Pagamentos por Tipo, Distribuição por Tipo de Pagamento, Tendência pagamentos Online vs Presencial, Online vs Presencial, Distribuição de Serviços, Distribuição de Tipos de Atendimento e Performance dos Médicos;
- gráficos Web responsivos em SVG, com legenda e tabela de valores acessível, além do estado dos agendamentos como resumo adicional;
- caixa durável de notificações com filtros, severidade, leitura individual/em massa, dispensa e vínculo autorizado com a entidade relacionada;
- administração de usuários com aprovação, rejeição, reabertura, bloqueio, permissão de atendimento online e salvaguardas para Administradores;
- análise e cancelamento de Premium, configuração de plano, concorrência otimista e acesso reautenticado ao comprovante privado;
- navegação exclusiva do Administrador em seis áreas: Início, Clínica, Consultas, Analytics, Alertas e Usuários;
- sincronização dos eventos auditáveis relevantes com as notificações administrativas duráveis.

## Banco DB-First

As migrations `0019__administrator_experience.sql` e `0020__administrator_configuration_concurrency.sql` foram executadas integralmente em `viverappweb`, no MySQL local 8.0.41, antes da regeneração do scaffold DB-First.

O schema passou a representar notificações administrativas duráveis, leitura e dispensa, concorrência das configurações e planos Premium e recorrência anual explícita de feriados. As entidades e o contexto gerados não foram editados fora do processo de scaffold.

## Segurança e integridade

- policy administrativa exige MFA e limita a sessão a duas horas, sem persistência prolongada;
- filtro de step-up exige sessão iniciada há no máximo cinco minutos para configuração, aprovação, bloqueio, Premium e operações financeiras;
- rate limits específicos, antiforgery, DTOs explícitos, validação no servidor, concorrência otimista e auditoria append-only;
- bloqueio contra autoexclusão insegura e contra perda do último Administrador recuperável;
- analytics limitado a 366 dias e agregado no servidor, sem observações, laudos, anexos ou identificadores desnecessários;
- a sincronização de eventos operacionais usa somente os tipos aceitos pelo schema de notificações, incluindo cancelamento, reagendamento, pagamento e decisões Premium;
- pagamentos presenciais e estados de agenda permanecem sob autoridade do servidor e usam idempotência;
- maintenance mode não bloqueia health checks, autenticação nem a própria recuperação administrativa;
- a rotação da chave do autenticador agora revoga a sessão anterior e emite nova sessão restrita até a confirmação do MFA, evitando que a mudança do security stamp interrompa o cadastro;
- um escopo local opcional de Data Protection permite validações isoladas sem apagar, substituir ou tentar reutilizar chaves e certificados antigos de outro contexto Windows.

## Evidências automatizadas

- `dotnet build ViverApp.slnx --no-restore`: aprovado com zero erros e zero avisos;
- `dotnet test ViverApp.slnx --no-build --no-restore`: 179 testes aprovados;
- `ViverApp.Database verify`: MySQL 8.0.41, banco `viverappweb`, schema e histórico de migrations aprovados;
- `dotnet format ViverApp.slnx --no-restore --verify-no-changes`: aprovado;
- contratos específicos cobrem endpoints administrativos, schema, concorrência, navegação, rotas responsivas e fronteiras entre papéis;
- a matriz [FASE-14-MATRIZ-PARIDADE.md](FASE-14-MATRIZ-PARIDADE.md) não contém item ausente ou parcial.

## Validação visual

A janela de validação permaneceu no monitor 3. Foram revisados o painel inicial e a agenda em desktop, a configuração da clínica em tablet e a navegação/áreas administrativas em celular. Também foram percorridas diretamente Clínica, Consultas, Analytics, Alertas e Usuários com a sessão elevada.

A inspeção encontrou dois defeitos de apresentação: os atalhos do painel não formavam cards e o sexto destino da navegação móvel quebrava a barra inferior. Ambos foram corrigidos. Após a recriação da aba para atualizar o Web, o controle de viewport do navegador integrado deixou de reaplicar a largura solicitada; a última correção móvel foi então confirmada pelo seletor isolado do shell administrativo e pelo contrato que exige exatamente seis destinos. O viewport temporário foi restaurado ao final.

Em 08/09/2026, a paridade analítica também foi revalidada no monitor 3 com a conta administrativa e o MFA configurados pelo proprietário. A página exibiu os nove gráficos do MAUI, com dados reais do banco local, legendas, percentuais e tabelas acessíveis de valores. Foram inspecionados os estados desktop e responsivo; a revisão levou a duas correções adicionais: cards analíticos passaram a trocar de uma para duas colunas conforme o espaço disponível, sem comprimir os gráficos, e séries de área com apenas um mês passaram a exibir um marcador visível sem uma série zerada encobrir outra.

O certificado HTTPS e o anel de chaves existentes foram preservados. A validação utilizou um escopo local isolado de Data Protection e não limpou nem recriou certificados.

## Homologações remanescentes

O TOTP administrativo foi configurado e testado pelo proprietário. As dependências externas ou manuais ainda reais continuam registradas em `.local/PENDENCIAS.md`, incluindo documentos Premium não sensíveis e entrega por e-mail/SMS.
