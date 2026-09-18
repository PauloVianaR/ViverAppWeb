# Fase 18 — Implementação e validação

## Resultado

O prontuário eletrônico foi implementado na branch `codex/fase-18-prontuario-eletronico`, sem implementar a Agenda/Psicólogo da Fase 19 nem iniciar e-mail, SMS ou jobs da Fase 20. Médico, Gestor e Administrador acessam uma visão centrada no Paciente. Médico vinculado e Gestor podem registrar conteúdo clínico; a autoria do Gestor é controlada pela configuração administrativa `manager.medical_records_write_enabled`, ligada por padrão.

## Dados e integridade

A migration `0031__electronic_health_record.sql` cria o prontuário, rascunhos, encontros, versões, documentos e histórico de acesso. A migration complementar `0032__optional_medical_record_content.sql` remove a antiga exigência mínima de conteúdo: somente o vínculo com o atendimento é obrigatório, enquanto todos os campos clínicos e sinais vitais são opcionais. A migration `0034__clinical_authorship_and_operational_permissions.sql` generaliza a autoria clínica para contas autorizadas e adiciona as permissões operacionais de agendamento médico, gestão Premium pelo Gestor e escrita clínica pelo Gestor. Todas foram aplicadas no banco local `viverappweb` em MySQL 8.0.41 antes do scaffold DB-First. Campos informados permanecem tipados. Versões finalizadas e acessos são append-only por trigger; alterações criam uma nova versão com motivo, hash e encadeamento.

## API, autorização e privacidade

As rotas `/api/v1/medical-records` possuem policies por capacidade e respostas sem cache. A API deriva o ator da sessão, valida o papel no banco, aplica vínculo Médico–Paciente e não usa entidades EF como contratos. Gestor possui acesso clínico direto e autoria condicionada à configuração administrativa; o Administrador não precisa informar uma justificativa manual, mas continua dependendo de sessão elevada recente. A finalidade administrativa é registrada automaticamente na auditoria, assim como tentativas negadas, sem guardar conteúdo clínico.

Documentos passam pela validação de extensão, MIME real, assinatura, limite e malware já usada pelo produto. O objeto fica no R2 privado quando configurado, sem CDN. O PDF é montado no servidor a partir de um snapshot limitado por papel/período, sem HTML ativo, scripts, links ou recursos remotos.

## Experiência Web

Os cards de pacientes dos três perfis abrem o prontuário. O layout apresenta resumo recolhível com a visão geral incorporada, Linha do tempo, Prontuário, Financeiro e Documentos; o Administrador também vê Auditoria. No celular, resumo, abas, filtros, formulários, timeline, tabelas e documentos refluem sem transformar o desktop existente.

Médico e Gestor autorizado selecionam um atendimento confirmado, iniciado ou finalizado, preenchem anamnese e sinais vitais, recebem autosave sinalizado e recuperam o rascunho. Salvar cria uma nova versão auditável, sem alterar automaticamente o estado do atendimento. O Administrador continua em modo de leitura do recorte autorizado.

## Evidências

- `dotnet build ViverApp.slnx --no-restore`: zero erros e avisos;
- sete projetos de teste executados em sequência: 214 testes aprovados; a execução serial evita disputa entre suítes que compartilham o MySQL local;
- `dotnet format ViverApp.slnx --no-restore --verify-no-changes`: exigido antes do encerramento;
- `ViverApp.Database verify`: MySQL 8.0.41, `viverappweb` e migrations até `0035` aprovados;
- teste clínico transacional deixa o banco inalterado e comprova versionamento, concorrência, autoria, finalidade, step-up e triggers append-only;
- homologações manuais e externas remanescentes estão exclusivamente em `.local/PENDENCIAS.md`.

## Limites legais declarados

O produto não exibe selo nem afirma certificação SBIS, NGS2, assinatura ICP-Brasil ou eliminação segura do papel. A Lei nº 13.787/2018 prevê requisitos específicos para digitalização e guarda, e a SBIS mantém processo formal de certificação. Política de retenção, textos jurídicos e eventual assinatura digital precisam de validação especializada antes da produção.

## Correção posterior ao primeiro teste manual

- corrigida a incompatibilidade entre a restrição `long` das rotas e o antigo parâmetro `ulong` do componente, que encerrava o circuito Blazor ao abrir qualquer prontuário;
- adicionada uma barreira global para conter falhas não tratadas de componentes, registrar o diagnóstico técnico somente no servidor e acionar o popup padronizado para o usuário;
- o fallback de desconexão do circuito também passou a usar uma apresentação modal responsiva com a mensagem segura em português;
- mensagens técnicas de transporte, como `TypeError`, `Failed to fetch` e falha de negociação, não são mais exibidas literalmente ao usuário.

## Ajustes posteriores do formulário clínico

- mensagens catalogadas são separadas do stack trace de JavaScript antes de alimentar aviso e popup; os detalhes técnicos permanecem no console;
- todos os campos clínicos e sinais vitais passaram a ser opcionais no banco, na API e na interface; pressão `12/8` e altura `1,70` são normalizadas para `120/80 mmHg` e `170 cm`, e zeros oriundos de campos vazios são tratados como ausência;
- o seletor médico recebe somente atendimentos confirmados, com chegada ou iniciados;
- a visão geral foi incorporada ao resumo e a informação redundante de privacidade foi removida;
- ao trocar de atendimento, alterações pendentes são salvas no atendimento anterior antes do rascunho do novo contexto ser carregado, permitindo retornar sem perda de conteúdo.

## Ajustes integrados de ciclo, catálogo e visualização

- Médico, Gestor e Administrador podem finalizar um atendimento confirmado, com chegada ou iniciado mediante confirmação explícita; o laudo médico é opcional. Somente o Administrador pode reabrir um atendimento finalizado, sempre com motivo auditado;
- o Médico pode iniciar um atendimento confirmado sem registro prévio de chegada e desfazer o início, restaurando o estado anterior;
- o cancelamento de pagamento é oferecido e aceito somente enquanto o atendimento está confirmado ou com chegada;
- o prontuário passou a ser acessível diretamente dos detalhes de atendimentos a partir de confirmado; a finalidade clínica deixou de ser exigida do Gestor;
- a categoria `Procedimento` foi incluída no schema, contratos, filtros, catálogo e jornadas de agendamento;
- o Gestor recebeu gestão temporária de tipos/preços e horários médicos, controlada separadamente pelas configurações administrativas `manager.appointment_types_enabled` e `manager.doctor_schedules_enabled`;
- a preferência “Cards” ou “Lista compacta” é persistida por conta em `account_ui_preferences` e aplicada às agendas, históricos e resumos de atendimentos após recarregar ou entrar novamente;
- a autenticação administrativa recente passou para 30 minutos e a política sensível para 10 tentativas em 5 minutos;
- a migration `0033__appointment_lifecycle_preferences_and_manager_controls.sql` foi aplicada integralmente no MySQL local 8.0.41 e o scaffold DB-First foi regenerado.

## Ajustes de prontuário, Premium e permissões operacionais

- o seletor de Anamnese e evolução passou a oferecer somente atendimentos realmente elegíveis: confirmados, iniciados e finalizados, além do estado intermediário de chegada;
- laudos médicos podem ser salvos e versionados em qualquer atendimento que não esteja pendente ou cancelado; cada salvamento gera uma versão auditável e não depende da finalização do atendimento;
- o agendamento de pacientes pelo Médico passou a depender de `doctor.patient_scheduling_enabled`, desligada por padrão e aplicada tanto à interface quanto à API;
- a escrita de prontuário pelo Gestor passou a depender de `manager.medical_records_write_enabled`, ligada por padrão e validada no servidor;
- a gestão Premium pelo Gestor passou a depender de `premium.manager_can_manage`, ligada por padrão; ativar exige comprovante privado, desfazer revoga o benefício e torna o comprovante indisponível;
- os cards de pacientes exibem idade quando há nascimento, estrela dourada para Premium e filtro dedicado nos perfis que possuem a lista;
- valores Premium mostram preço original riscado e valor final com o desconto vigente nos cards de atendimento;
- jornadas de agendamento do Paciente, Médico e Gestor permitem pesquisar o serviço por texto após selecionar o tipo de atendimento;
- a grade de horários do Gestor foi alinhada ao contrato DB-First baseado em `TimeSpan`, eliminando a conversão incorreta para `00:00`;
- a especificidade do CSS da Lista compacta foi corrigida para impedir que a grade de cards permanecesse sobreposta no Histórico do Médico;
- a migration `0034__clinical_authorship_and_operational_permissions.sql` foi aplicada integralmente no MySQL local 8.0.41 e o scaffold DB-First foi regenerado.

## Ajustes financeiros e de agendamento na mesma branch

- a migration `0035__cash_reopening_and_cumulative_visibility.sql` adiciona reaberturas append-only, remove a unicidade que impedia novo fechamento auditado e cria as permissões `cash.manager_can_reopen` (desligada) e `cash.manager_can_view_cumulative_totals` (ligada);
- caixa fechado bloqueia pagamentos, cancelamentos de pagamento e lançamentos manuais para qualquer papel; dias passados são sempre apresentados e impressos como fechados;
- Administrador pode reabrir somente o caixa de hoje; Gestor depende de configuração. Totalizadores gerais acumulados aparecem em um expander recolhido;
- a distribuição administrativa passa a conter explicitamente Consultas, Exames, Cirurgias e Procedimentos, inclusive com zero no período;
- o Administrador deixou de preencher finalidade clínica manual; a sessão elevada e a auditoria permanecem;
- o agendamento de Gestor e Médico ganhou uma entrada única com seletor de paciente, preservando a pré-seleção originada no card;
- profissionais são filtrados pelos serviços ativos vinculados antes de aparecerem na seleção, corrigindo combinações impossíveis como as da Dra. Helena com serviços antigos desativados;
- preços Premium exibem valor original riscado e valor final tanto na escolha do serviço quanto na revisão;
- Gestor e Administrador receberam filtros por texto e tipo no catálogo de atendimentos.
