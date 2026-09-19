# Fase 19 — Agenda visual, Psicólogo e atendimentos sem cobrança

## Estado e objetivo

**Estado:** implementada na branch da Fase 19; falta a inspeção visual autenticada exclusivamente no monitor físico 3.

**Branch prevista:** `codex/fase-19-agenda-psicologo-atendimentos-gratuitos`.

**Evidências técnicas (19/09/2026):** migrations `0036` e `0037` aplicadas integralmente em `viverappweb`, MySQL 8.0.41; scaffold DB-First regenerado; build sem avisos; suíte integral aprovada em execução sequencial. Há testes transacionais novos para Psicólogo/agendamento sem cobrança, recorte de calendário e rejeição do checkout gratuito. A validação visual física permanece registrada em `.local/PENDENCIAS.md`, pois a automação desta sessão não identifica nem seleciona o monitor 3.

Reorganizar a operação diária em torno de uma agenda visual responsiva, transformar o histórico atual em uma consulta única de atendimentos, acrescentar o papel Psicólogo com a mesma experiência clínica do Médico e permitir tipos de atendimento que não exigem cobrança.

Esta fase será executada integralmente em uma branch própria, sem iniciar os workers de e-mail/SMS/jobs da Fase 20.

## Resultado esperado

Ao concluir a fase:

- cada perfil terá uma Agenda visual em Dia, Semana, Mês e Ano, adequada às permissões do usuário;
- a tela antiga de Histórico terá sido substituída por **Atendimentos**, preservando seus filtros, cartões, lista compacta e ações;
- Médico e Psicólogo compartilharão as mesmas jornadas e componentes clínicos, diferenciados pelo papel e pelo registro profissional CRM/CRP;
- Gestor e Administrador poderão criar tipos de atendimento cobrados ou sem cobrança;
- um atendimento sem cobrança nascerá confirmado, não oferecerá ações financeiras e não criará movimentação artificial no caixa;
- dados e histórico existentes serão preservados por migration SQL aplicada no MySQL local 8.0.41 e novo scaffold DB-First.

## Limites da fase

- não implementar e-mail, SMS, jobs duráveis, videochamada distribuída ou infraestrutura de produção;
- não alterar qualquer projeto legado;
- não copiar visualmente o Google Agenda nem depender de APIs do Google Calendar: a referência é apenas o padrão de interação;
- não criar uma segunda clínica ou associação multiclínica;
- não permitir que Psicólogo visualize agenda ou prontuário de outro profissional sem uma permissão administrativa que já exista para Gestor/Administrador;
- não considerar `robots.txt`, SEO, cookies ou documentos jurídicos, reservados à Fase 25;
- não efetuar cobrança real durante os testes.

## 1. Descoberta e consolidação antes da migration

Antes de alterar o schema:

1. mapear todas as rotas, menus, endpoints e componentes atuais de Agenda e Histórico nos quatro perfis existentes;
2. inventariar cada dependência cujo nome ou FK esteja acoplado a `doctor`, `DoctorProfile` ou `doctor_account_id`;
3. identificar agenda, disponibilidade, especialidades, serviços, vínculos com pacientes, notificações, videochamada, prontuário, relatórios e analytics que precisam aceitar Médico e Psicólogo;
4. confirmar todos os estados de atendimento e pagamento afetados pela criação automática como confirmado;
5. registrar a migration seguinte à última aplicada e validar que o alvo é exclusivamente `viverappweb` em MySQL 8.0.41;
6. atualizar a matriz de autorização antes de expor qualquer rota de Psicólogo.

## 2. Modelo profissional compartilhado e DB-First

### 2.1 Papel Psicólogo

- adicionar o papel canônico `psychologist`, mantendo a regra de exatamente um papel por conta;
- permitir cadastro Google ou clássico, confirmação de contato e aprovação administrativa nas mesmas condições do Médico;
- impedir autoatribuição de Administrador e troca de papel por payload;
- incluir Psicólogo nos fluxos administrativos de aprovação, bloqueio, pesquisa e auditoria;
- usar o rótulo **Psicólogo** ou **Psicóloga** de forma neutra na navegação quando o gênero não estiver disponível;
- armazenar e mostrar **CRP**, UF/região e número no lugar de CRM.

### 2.2 Generalização do profissional clínico

O banco atual possui relacionamentos centrados em Médico. A migration deve substituí-los por um modelo profissional compartilhado, sem duplicar a mesma estrutura para Psicólogo:

- criar ou adaptar um perfil profissional com conta, título, biografia, experiência, duração padrão e tipo de conselho (`CRM` ou `CRP`);
- preservar IDs, horários, vínculos, serviços, especialidades e atendimentos médicos existentes;
- generalizar FKs de agenda, disponibilidade, oferta de serviços, vínculo com pacientes e atendimento para `professional_account_id` ou nomenclatura equivalente;
- manter aliases/contratos de transição somente quando necessários para compatibilidade interna durante a fase;
- gerar entidades exclusivamente pelo scaffold DB-First após aplicar a migration;
- revisar índices para consultas por profissional e intervalo de tempo;
- não editar manualmente entidades ou contexto gerados.

Se a inspeção do schema demonstrar que uma migração incremental com perfil compartilhado não é segura, o plano deve ser atualizado antes da execução do DDL; não será aceita uma FK de Psicólogo apontando semanticamente para uma tabela exclusiva de Médico.

### 2.3 Tipos cobrados e sem cobrança

Adicionar ao schema:

- `appointment_types.requires_payment`, booleano obrigatório e `true` por padrão;
- um snapshot equivalente em `appointments`, para que mudar o catálogo no futuro não altere o comportamento financeiro de atendimentos já criados;
- regras de consistência: atendimento sem cobrança terá valor efetivo zero, moeda preservada e nenhuma cobrança atual;
- índices/constraints necessários para impedir pagamento ou checkout vinculado a atendimento sem cobrança.

Todos os tipos e atendimentos existentes serão migrados como `requires_payment = true`.

## 3. Navegação: Histórico passa a Atendimentos

Em todos os perfis que hoje possuem Agenda/Histórico:

- remover o conceito visual de tela **Histórico**;
- criar a entrada **Atendimentos** usando a funcionalidade da tela de Histórico atual;
- ao entrar na tela, inicializar **De** e **Até** com a data local de hoje;
- preservar os demais filtros, paginação, indicadores, origem dos indicadores, cartões, lista compacta, ações e regras de autorização;
- manter a preferência Cards/Lista compacta por usuário;
- adotar rotas canônicas como `/paciente/atendimentos`, `/medico/atendimentos`, `/psicologo/atendimentos`, `/gestao/atendimentos` e `/administracao/atendimentos`;
- redirecionar rotas antigas de `/historico` para a rota nova, preservando links salvos e sem renderizar duas implementações;
- atualizar menus desktop, tabs mobile, breadcrumbs, títulos e links internos.

O filtro de hoje é o valor inicial, não uma limitação: o usuário continuará podendo consultar outros períodos dentro dos limites seguros já existentes.

## 4. Nova Agenda visual

### 4.1 Modos de visualização

A Agenda oferecerá:

- **Dia:** linha temporal do dia, com horários e blocos posicionados pela duração;
- **Semana:** colunas por dia e eixo de horário, com navegação para semana anterior/seguinte;
- **Mês:** grade mensal, com cada atendimento identificado por `#número + nome do paciente`;
- **Ano:** apenas o ano selecionado, exibindo os 12 meses; dias com atendimento terão marcador e cor diferenciada, sem tentar desenhar todos os cartões;
- ação **Hoje**, navegação anterior/próxima e seletor de data/período;
- estado vazio, carregamento, erro, teclado, foco, leitor de tela, zoom e redução de movimento.

Nos modos Dia, Semana e Mês, cada ocorrência mostrará ao menos número do atendimento e nome do paciente. Estado, modalidade, tipo e cor terão legenda acessível; cor nunca será o único meio de comunicar estado.

### 4.2 Escopo por perfil

- **Paciente:** visualiza somente os próprios atendimentos, sem seletor de profissional;
- **Médico:** visualiza somente a própria agenda;
- **Psicólogo:** visualiza somente a própria agenda;
- **Gestor e Administrador:** possuem seletor no topo para um profissional específico e opção autorizada de visão conjunta quando útil;
- a interface deverá usar o rótulo **Profissional** onde a lista puder conter Médico e Psicólogo;
- nenhuma seleção enviada pelo navegador substitui a autorização no servidor.

### 4.3 API e desempenho

Separar a projeção de calendário da listagem operacional de Atendimentos:

- endpoint de calendário recebe período, modo e profissional autorizado;
- Dia/Semana/Mês retornam ocorrências mínimas com ID, número, paciente, profissional, início/fim, estado, tipo e modalidade;
- Ano retorna agregados por data, contagem e estados necessários aos marcadores, sem transferir todos os detalhes clínicos;
- datas são calculadas no timezone da clínica e transportadas com semântica explícita;
- o servidor limita intervalos, ordena de forma determinística e rejeita filtros inválidos em português;
- consultas devem usar projeção, índices e `AsNoTracking`, evitando N+1;
- colisões visuais de horários simultâneos devem ser acomodadas em colunas ou agrupamento, sem sobreposição ilegível;
- tocar/clicar em uma ocorrência abre o detalhe autorizado do mesmo atendimento.

### 4.4 Responsividade

- desktop e tablet podem usar grades temporais completas;
- celular deve usar uma visualização adaptada, com cabeçalho compacto, navegação por gesto/botão e lista temporal do dia selecionado;
- o modo Ano no celular exibirá meses em grade responsiva, sem redução ilegível;
- nenhum ajuste mobile deve degradar a visão desktop já validada.

## 5. Experiência completa do Psicólogo

Psicólogo terá exatamente as capacidades do Médico, salvo a identificação profissional:

- início/dashboard;
- Agenda e Atendimentos;
- pacientes vinculados, filtros e cartões;
- prontuário eletrônico, versões, documentos e PDF conforme as mesmas regras clínicas;
- chegada, início, desfazer início, conclusão e demais ações permitidas ao Médico;
- disponibilidade, serviços/modalidades e perfil;
- notificações visuais, sonoras e em popup;
- videochamada e integrações futuras no mesmo ponto de extensão;
- preferências de visualização persistidas por conta;
- agendamento para paciente condicionado à mesma configuração que controla Médicos.

A implementação deve extrair componentes, contratos e serviços profissionais compartilhados. Não copiar páginas inteiras de Médico para uma árvore paralela que precise de manutenção duplicada. Wrappers de rota/tema podem ser específicos, mas a regra clínica deve ter uma única fonte.

### 5.1 CRM e CRP

- Médico: `CRM {UF} {número}`;
- Psicólogo: `CRP {região/UF} {número}`;
- formulários, validações, cards, seletores, documentos, prontuário e auditoria exibem o conselho correto;
- o backend deriva o tipo de conselho do papel/perfil e ignora tentativas do cliente de escolher um conselho incompatível;
- unicidade e normalização do registro profissional são aplicadas no banco e no servidor.

## 6. Tipos de atendimento sem cobrança

### 6.1 Cadastro e edição do catálogo

Nos formulários de Gestor e Administrador:

- incluir checkbox **Cobrar?**, marcada por padrão;
- marcada: preço obrigatório e validado como hoje;
- desmarcada: ocultar o campo de preço, limpar qualquer valor digitado no estado da tela e enviar intenção explícita de não cobrar;
- o servidor nunca confiará apenas no campo oculto: quando `requires_payment = false`, persistirá o valor efetivo conforme a regra do domínio;
- a listagem identificará claramente **Sem cobrança**;
- manter filtros por texto e tipo de atendimento.

As permissões administrativas já existentes para o Gestor controlar o catálogo continuam válidas.

### 6.2 Criação e ciclo do atendimento

- atendimento cobrado mantém o comportamento atual e precisa do pagamento previsto para confirmar;
- atendimento sem cobrança nasce em `confirmed` tanto quando criado pelo Paciente quanto por Médico/Psicólogo/Gestor/Administrador, respeitando quem pode agendar;
- o detalhe mostra **Sem cobrança** em vez de `R$ 0,00` como se fosse desconto;
- não mostrar checkout, confirmar pagamento, cancelar pagamento ou forma de pagamento;
- não aplicar desconto Premium nem exibir preço original riscado em atendimento sem cobrança;
- reagendamento preserva o snapshot de cobrança e o estado coerente;
- cancelamento, chegada, início e conclusão seguem as mesmas regras de um atendimento confirmado comum;
- caixa e totais financeiros não recebem movimento de valor zero;
- relatórios distinguem atendimento sem cobrança de pagamento pendente ou cortesia financeira manual.

### 6.3 Segurança e consistência

- API rejeita tentativa de criar pagamento, checkout ou reversão para atendimento sem cobrança;
- idempotência e concorrência permanecem obrigatórias;
- alteração posterior de `requires_payment` no tipo não muda atendimentos existentes;
- auditoria registra criação/edição do tipo e criação do atendimento sem incluir conteúdo clínico;
- analytics contam o atendimento, mas não o classificam como receita ou inadimplência.

## 7. Contratos e compatibilidade

- substituir nomes públicos `Doctor*` por `Professional*` onde o contrato passar a atender os dois papéis;
- manter DTOs explícitos; entidades EF não atravessam a API;
- versionar mudanças incompatíveis ou fornecer transição interna durante a fase;
- atualizar navegação, deep links e testes de rotas antigas;
- manter o número do atendimento, paciente, histórico de reagendamentos e autoria clínica intactos;
- revisar todas as traduções de papéis para incluir `psychologist` sem cair em texto técnico em inglês.

## 8. Segurança, privacidade e auditoria

- políticas específicas ou compartilhadas devem autorizar Médico e Psicólogo sem ampliar Gestor/Paciente indevidamente;
- ownership do profissional será validado em Agenda, Atendimentos, pacientes, prontuário, documentos, notificações e disponibilidade;
- Gestor/Administrador podem selecionar profissional apenas nas operações já permitidas pelo papel;
- nenhuma API anual retorna anamnese, documentos ou outros dados clínicos desnecessários;
- registrar alterações de catálogo, cadastro/aprovação de Psicólogo, mudanças de disponibilidade e transições críticas;
- preservar rate limiting por identidade e mensagens de erro em português;
- executar testes de IDOR entre Médico e Psicólogo, inclusive troca manual de IDs e filtros.

## 9. Ordem de implementação da fase

1. consultar CodeGraph, roadmap, este plano e pendências locais;
2. inventariar acoplamentos a Médico, Agenda e Histórico;
3. desenhar e revisar a migration DB-First;
4. validar MySQL 8.0.41 e banco `viverappweb`, aplicar integralmente a migration e gerar novo scaffold;
5. generalizar domínio/serviços/policies para profissional clínico;
6. implementar cadastro, aprovação, perfil e shell do Psicólogo sobre componentes compartilhados;
7. implementar `requires_payment` no catálogo e no snapshot do atendimento;
8. implementar regras de criação confirmada e bloqueios financeiros para atendimentos sem cobrança;
9. criar API de calendário e agregação anual;
10. substituir Histórico por Atendimentos e configurar o período inicial de hoje;
11. implementar Agenda Dia/Semana/Mês/Ano nos cinco perfis;
12. atualizar caixa, premium, analytics, notificações, prontuário e seletores para profissional/sem cobrança;
13. executar testes, build, format, validação de migrations e inspeção visual exclusivamente no monitor 3;
14. registrar evidências e pendências reais;
15. parar sem iniciar a Fase 20.

## 10. Testes obrigatórios

### Banco e migração

- versão MySQL exatamente 8.0.41 e schema `viverappweb` confirmados;
- todos os profissionais e atendimentos existentes preservados;
- FKs e índices profissionais íntegros;
- registros existentes marcados como cobrados;
- scaffold reproduzível e sem edição manual.

### Agenda e Atendimentos

- limites de dia, semana, mês e virada de ano no timezone da clínica;
- fevereiro e ano bissexto;
- atendimentos simultâneos e atravessando intervalos visuais;
- agregação anual correta;
- seleção de profissional permitida apenas a Gestor/Administrador;
- Médico/Psicólogo não conseguem consultar agenda alheia por URL/API;
- Paciente vê apenas seus atendimentos;
- Atendimentos abre com De/Até iguais a hoje;
- rota antiga de Histórico redireciona corretamente;
- cards/lista compacta, filtros, indicadores e ações permanecem funcionais.

### Psicólogo

- cadastro Google e clássico, aprovação e bloqueio;
- CRP obrigatório/normalizado e CRM incompatível rejeitado;
- paridade de páginas, ações e preferências com Médico;
- prontuário identifica autoria/papel corretamente;
- notificações de chegada chegam somente ao profissional vinculado;
- IDOR cruzado Médico ↔ Psicólogo rejeitado.

### Cobrança

- checkbox marcada por padrão;
- preço obrigatório somente quando cobrado;
- atendimento sem cobrança nasce confirmado;
- checkout/pagamento/reversão rejeitados para atendimento sem cobrança;
- nenhum movimento de caixa de valor zero;
- Premium não aplica desconto a item sem cobrança;
- edição do catálogo não altera o snapshot financeiro anterior;
- atendimento cobrado mantém todas as regressões atuais.

### Interface

- celular, tablet, notebook e desktop largo;
- Agenda legível em Dia/Semana/Mês/Ano;
- teclado, foco, leitor de tela, contraste e zoom de 200%;
- marcadores anuais possuem texto/legenda além de cor;
- erros catalogados aparecem no popup global sem stack trace.

## 11. Critérios de saída

- migration aplicada e scaffold DB-First reproduzido no MySQL local 8.0.41;
- nenhuma alteração no banco legado `viverappmobile`;
- papel Psicólogo aprovado, autorizado e funcional com CRP;
- paridade clínica de Médico/Psicólogo comprovada sem duplicação estrutural relevante;
- Agenda visual e Atendimentos funcionam em todos os perfis previstos;
- atendimentos sem cobrança são confirmados sem criar dívida, pagamento ou caixa;
- suíte integral verde, sem warnings;
- validação visual concluída no monitor 3 ou pendência objetiva registrada;
- documentação e matriz de autorização atualizadas;
- nenhuma funcionalidade da Fase 20 iniciada.

