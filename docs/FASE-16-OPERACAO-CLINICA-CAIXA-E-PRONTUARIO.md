# Fase 16 — Operação clínica, caixa e prontuário eletrônico

## Estado e objetivo

**Estado:** planejada; nenhuma implementação desta fase foi iniciada.

**Branch prevista:** `codex/fase-16-operacao-caixa-prontuario`, criada a partir da `main` somente depois da integração formal da Fase 15.

Entregar uma operação diária integrada para Gestor, Médico e Administrador: cadastro operacional completo do Paciente, identificação inequívoca dos atendimentos, chegada e fila, notificações em tempo real, caixa auditável, cancelamento de pagamentos e prontuário eletrônico seguro.

Esta é uma única fase. Os blocos abaixo são incrementos internos da mesma branch e não autorizam começar a Fase 17.

## Regras invariáveis

- alterar somente `ViverAppWeb`;
- usar exclusivamente o schema novo `viverappweb`; `viverappmobile` continua somente leitura;
- seguir DB-First: criar e aplicar integralmente a migration SQL no MySQL local 8.0.41, validar o schema e só então regenerar o EF;
- nunca editar manualmente o `DbContext` ou entidades geradas;
- uma conta continua possuindo um único papel e a aplicação continua atendendo uma única clínica;
- valores, estados, números de atendimento, permissões e totais são autoridade do servidor;
- movimentações financeiras, eventos clínicos, versões e auditorias relevantes são append-only; correções usam eventos compensatórios ou novas versões;
- nenhum documento clínico ou dado sensível será público no R2/CDN, enviado a logs ou incluído em notificações desnecessariamente;
- todas as alterações sensíveis exigem autorização revalidada, antiforgery, idempotência quando aplicável, concorrência otimista e auditoria;
- a referência visual anexada é inspiração, não especificação a copiar.

## Decisões de produto desta fase

### Dois números, duas finalidades

Para não confundir ordem de cadastro com ordem real de chegada, serão usados dois identificadores humanos:

1. **Número do atendimento:** sequencial global, imutável, único e iniciado em `100`. É criado com o atendimento e serve para busca, suporte, comprovantes e comunicação.
2. **Senha de chegada:** sequencial diário, iniciada em `100` a cada data operacional, atribuída atomicamente quando o Gestor registra que o Paciente chegou. Ela representa a ordem real de chegada.

IDs técnicos continuam internos. Números cancelados não são reciclados e a API nunca aceitará o número humano sem revalidar o escopo e o papel.

### Cancelar pagamento não apaga histórico

“Cancelar pagamento” criará uma reversão vinculada ao lançamento original e um movimento negativo no caixa. O registro original nunca será apagado ou alterado para esconder o que ocorreu.

- pagamento presencial/manual: reversão local atômica, com motivo obrigatório;
- PagBank: solicitação de cancelamento/estorno pelo provedor e estado intermediário `reversal_pending`; a reversão financeira só se torna efetiva após confirmação/reconciliação;
- um novo pagamento será permitido depois que o anterior estiver efetivamente revertido;
- cada nova tentativa cria um novo pagamento; o histórico mantém original, reversão e substituto;
- nunca haverá mais de um pagamento ativo/quitado não revertido para o mesmo atendimento.

### Prontuário disponível aos três papéis, com autoria separada

O pedido atual revisa a decisão anterior que limitava Gestor e Administrador a metadados clínicos. A Fase 16 disponibilizará a área de prontuário aos três perfis, mas sem transformar acesso administrativo em autoria médica:

- **Médico:** leitura e autoria clínica para pacientes dentro de seu vínculo; cria, finaliza e retifica anamnese/evolução;
- **Gestor:** dados cadastrais, agenda, linha do tempo e financeiro; leitura de conteúdo clínico somente por ação explícita, com finalidade/motivo e auditoria; não cria, assina nem retifica registro médico;
- **Administrador:** mesma superfície integrada, com step-up MFA para conteúdo clínico sensível e exportação; não assume autoria médica;
- toda quebra de contexto (“acessar conteúdo clínico”) registra ator, paciente, finalidade, instante, sessão e resultado;
- PDFs seguem o mesmo recorte de permissão da tela e nunca ampliam o acesso do solicitante.

Durante a implementação, as documentações das Fases 8, 13 e 14 e a matriz de paridade deverão ser atualizadas para registrar formalmente essa decisão superveniente.

## Experiência inspirada pelo anexo

O anexo sugere uma visão centrada no paciente com resumo lateral, contadores, abas, filtros temporais, linha do tempo e geração de PDF. A Web adotará esses conceitos em um layout atual, responsivo e acessível, sem copiar aparência, fotografia, dados ou textos do exemplo.

Em desktop, o resumo do Paciente poderá permanecer lateral enquanto o conteúdo usa abas. Em tablet e celular, o resumo vira cabeçalho recolhível e as abas passam a navegação horizontal acessível ou seletor. A interface deve funcionar por teclado, leitor de tela, touch e zoom de 200%.

## Matriz de capacidades por papel

| Capacidade | Gestor | Médico | Administrador |
|---|---:|---:|---:|
| Ver CPF, e-mail, telefone e endereço do Paciente | Sim | Dentro do vínculo | Sim |
| Editar dados cadastrais do Paciente | Sim | Não nesta fase | Sim |
| Consultar caixa e imprimir | Sim | Não | Sim |
| Cancelar/reverter pagamento | Sim | Não | Sim, com step-up |
| Registrar chegada e emitir senha | Sim | Não | Sim |
| Receber notificação de chegada | Não | Sim, quando atribuído | Não |
| Configurar popup/som das chegadas | Não | Preferência de sessão sem ampliar política | Sim, política da clínica |
| Ver prontuário integrado | Sim, acesso clínico justificado | Sim, dentro do vínculo | Sim, com step-up e justificativa |
| Criar/finalizar/retificar conteúdo clínico | Não | Sim | Não |
| Gerar PDF | Recorte autorizado | Recorte clínico autorizado | Recorte autorizado com step-up |

## Pacientes do Gestor e do Administrador

### Lista e card

Cada card deve exibir, com máscaras adequadas:

- nome e nome preferido;
- CPF no formato `XXX.XXX.XXX-XX`;
- e-mail;
- telefone formatado para o país/DDD;
- endereço resumido: logradouro, número, complemento quando houver, bairro, cidade/UF e CEP;
- status da conta, Premium e ações já existentes;
- estado explícito para campo ausente, sem inventar valor.

No celular, CPF e contatos permanecem legíveis sem rolagem horizontal. E-mail/endereço longos quebram linha e possuem rótulo; ícones nunca serão o único meio de identificação.

### Edição

- ação “Editar dados” abre página ou painel com nome, CPF, e-mail, telefone e endereço completo;
- CPF deve ser validado por dígitos verificadores e permanecer único;
- e-mail é normalizado e validado; telefone é persistido em E.164 e exibido com máscara;
- CEP usa máscara `XXXXX-XXX`, busca automática já adotada no cadastro e permite correção manual;
- alteração de e-mail/telefone respeita confirmação do novo contato e não desativa silenciosamente o canal anterior antes do fluxo seguro;
- detectar conflitos sem revelar a conta proprietária do CPF/e-mail/telefone;
- exigir `row_version`; conflitos devolvem os dados atuais para comparação;
- registrar auditoria com campos alterados e valores redigidos, nunca CPF/endereço completos no log;
- não permitir troca de papel, senha ou estado da conta por mass assignment.

## Atendimento, chegada e estados visuais

### Máquina de estados

A implementação deve revisar a máquina existente e incluir uma transição explícita de chegada. Fluxo principal:

`pending → confirmed → arrived → in_progress → completed`

Estados terminais e alternativos continuam possíveis conforme as regras existentes: `cancelled`, `no_show` e `rescheduled`. Pagamento é um eixo separado e não será inferido apenas do status do atendimento.

Regras:

- somente Gestor/Administrador autorizado marca chegada;
- chegada exige atendimento presencial, confirmado, na janela configurada e ainda não terminal;
- repetição com a mesma chave é idempotente; duas chegadas concorrentes produzem uma única senha;
- `arrived_at_utc`, data operacional local, autor e senha ficam persistidos;
- a ordem é calculada pelo instante confirmado no servidor, nunca pelo relógio do navegador;
- o Médico só pode iniciar/concluir atendimento dentro de suas próprias atribuições.

### Cards e carinhas

Os cards usarão uma expressão visual, cor, ícone e texto — nunca apenas emoji — para manter acessibilidade:

- pendente: expressão triste e texto “Pendente”;
- confirmado: expressão positiva e texto “Confirmado”;
- chegou: expressão de presença/destaque, texto “Paciente chegou” e senha diária;
- em atendimento, concluído, cancelado e falta: ícones e textos próprios coerentes.

A mesma linguagem deve aparecer onde o estado for exibido para Gestor, Médico e Administrador. Tabelas e PDFs usam texto, não dependem das carinhas.

## Notificação imediata ao Médico

Ao registrar a chegada:

1. a mesma transação persiste chegada, senha, histórico de status e evento de outbox;
2. um consumidor cria uma notificação durável exclusivamente para o Médico atribuído;
3. SignalR entrega o evento em tempo real às sessões autorizadas;
4. o shell do Médico atualiza o sininho, a bolinha/contador não lido, exibe popup e toca o aviso se habilitado;
5. se o Médico estiver desconectado, o item permanece no sininho e aparece ao reconectar.

### Comportamento do sininho

- contador acessível e rótulo para leitor de tela;
- lista de não lidas e lidas, ordenada por data;
- ação para abrir o atendimento revalida ownership;
- marcar uma/todas como lidas;
- deduplicação entre reconexões e múltiplas abas;
- popup contém apenas o mínimo operacional: número/senha, horário e indicação de chegada;
- nenhuma anamnese, diagnóstico ou observação clínica no evento, popup ou áudio.

### Popup e áudio

- toast visível, focável quando acionável, sem roubar foco durante digitação clínica;
- som curto, não alarmante e acompanhado por alternativa visual;
- respeitar `prefers-reduced-motion` e tecnologias assistivas;
- navegadores podem bloquear autoplay: depois da primeira interação autenticada, o shell inicializa o áudio; se continuar bloqueado, mostra instrução não intrusiva;
- falha de SignalR ou áudio não perde a notificação persistida.

### Configuração administrativa

O Administrador configura, para a clínica:

- notificações de chegada habilitadas;
- popup habilitado;
- aviso sonoro habilitado;
- volume padrão e som aprovado dentre assets locais versionados;
- janela antecipada/tardia em que a chegada pode ser registrada;
- retenção das notificações e política de marcação como lida.

O Médico poderá silenciar o áudio apenas na sessão/navegador atual por acessibilidade ou contexto, sem alterar a política global. A notificação durável do sininho permanece registrada.

## Caixa do Gestor e do Administrador

### Conceito

O caixa é um livro-razão diário da clínica na data operacional configurada. Ele agrega pagamentos presenciais e online, cancelamentos/estornos e ajustes autorizados. O histórico pode ser consultado por data, sem permitir reescrever dias anteriores.

### Tela

- nova navegação “Caixa” para Gestor e Administrador;
- seletor de data com atalho Hoje, dia anterior/seguinte e limite de intervalo;
- cabeçalho com estado do dia, saldo inicial quando usado, entradas, saídas/reversões e total líquido;
- filtros por forma de pagamento, tipo de movimento, atendimento, número humano, Paciente e responsável;
- movimentos em tabela no desktop e cards no celular;
- cada item mostra horário, atendimento, descrição, forma de pagamento, entrada/saída, valor, responsável e vínculo com original/reversão;
- totais por dinheiro, Pix, débito, crédito, PagBank online e outras formas explicitamente cadastradas;
- valores em `decimal`, moeda BRL e arredondamento centralizado no servidor;
- paginação da consulta sem paginar incorretamente os totais, que são calculados sobre todo o filtro do dia.

### Movimentos e fechamento

Tipos mínimos:

- pagamento recebido;
- cancelamento/estorno de pagamento;
- suprimento/entrada manual autorizada;
- sangria/saída manual autorizada;
- ajuste corretivo compensatório, nunca edição ou exclusão.

Suprimento, sangria e ajuste exigem valor, motivo, confirmação e auditoria. Fechamento diário registra resumo e responsável, mas não impede correções futuras: correções posteriores ficam identificadas como pós-fechamento e exigem step-up do Administrador.

### Impressão

No rodapé dos resultados haverá:

- **Imprimir:** cabeçalho da clínica, data, filtros, todas as movimentações visíveis do dia e totais por forma de pagamento;
- **Imprimir Totais:** cabeçalho, data e somente consolidação por forma de pagamento, entradas, reversões/saídas e líquido.

As duas opções usam visual específico de impressão, sem menus/botões, com número de páginas, data/hora de emissão e responsável. A impressão usa o diálogo do navegador e permite “Salvar como PDF”; nenhum PDF temporário público será criado. CPF, endereço e dados clínicos não aparecem no caixa impresso. O resultado impresso deve ser reproduzível a partir do mesmo filtro e protegido contra formula injection caso no futuro seja exportado.

## Cancelamento e novo pagamento

Na agenda e no detalhe do atendimento pago, Gestor e Administrador verão “Cancelar pagamento” quando elegível.

Fluxo:

1. mostrar valor, método, data e referência redigida;
2. solicitar motivo obrigatório e confirmação explícita;
3. no Administrador, exigir sessão MFA elevada recente; no Gestor, aplicar policy financeira específica;
4. gerar chave idempotente e conferir `row_version`;
5. criar pedido de reversão e movimento compensatório conforme o tipo do pagamento;
6. atualizar a tela e o caixa somente com estado confirmado;
7. habilitar “Registrar novo pagamento” quando não existir pagamento ativo/quitado não revertido.

Cancelamento do pagamento não cancela automaticamente o atendimento. Cancelamento do atendimento também não simula estorno: a interface explicará quando existe pagamento que precisa de ação financeira separada.

## Prontuário eletrônico

### Estrutura da tela

- resumo do Paciente: foto opcional privada, nome, nome preferido, idade, contatos essenciais e alertas autorizados;
- indicadores: atendimentos, concluídos, faltas, cancelados, documentos e último atendimento;
- abas **Visão geral**, **Linha do tempo**, **Prontuário**, **Financeiro** e **Documentos**;
- filtros por período, Médico, tipo de evento e estado;
- ordem crescente/decrescente;
- busca limitada a campos permitidos, sem indexar texto clínico em serviço externo;
- ação **Gerar PDF** respeitando papel e filtro;
- estados loading, vazio, erro, concorrência, acesso negado e sessão elevada expirada.

### Visão geral e informações

- identificação e contato;
- alergias e alertas clínicos críticos, quando registrados por Médico;
- condições/problemas ativos;
- medicamentos informados;
- última consulta e próximos atendimentos;
- resumo financeiro sem dados completos de meio de pagamento;
- autoria e data da última atualização.

### Linha do tempo

Uma linha cronológica une, sem misturar permissões:

- criação, confirmação, chegada, início, conclusão, reagendamento, cancelamento e falta;
- pagamentos, reversões e novo pagamento;
- anamnese/evolução finalizada e respectivas retificações;
- laudos e documentos clínicos por metadados;
- documentos administrativos e PDFs gerados;
- autor, instante e tipo de evento.

Eventos sem permissão aparecem, quando útil, apenas como “registro restrito”, nunca com conteúdo sensível.

### Registro clínico

O Médico poderá registrar de forma estruturada:

- motivo da consulta/queixa principal;
- história da doença atual;
- antecedentes pessoais e familiares;
- alergias, medicamentos e hábitos relevantes;
- sinais vitais e exame físico;
- hipóteses/diagnósticos e códigos padronizados quando aprovados;
- conduta, orientações, solicitações e plano de acompanhamento;
- evolução clínica e observações;
- anexos privados relacionados ao atendimento.

Campos estruturados terão opção “não informado/não se aplica” quando legítima. Texto livre é codificado na saída, limitado e nunca renderizado como HTML arbitrário.

### Autoria, fechamento e retificação

- rascunho é privado ao Médico autor e possui autosave controlado;
- finalizar exige confirmação, associa atendimento/Médico/CRM/instante e torna a versão imutável;
- correção posterior cria retificação encadeada com motivo; não sobrescreve o original;
- hashes/versões permitem detectar alteração indevida;
- assinatura digital e requisitos de validade probatória serão implementados somente com método juridicamente validado; até lá, a interface não alegará certificação ICP-Brasil ou conformidade NGS2 inexistente;
- relógio do servidor, UTC e fuso da clínica são usados em todos os registros.

### PDF

- gerado no servidor a partir de uma versão fechada ou recorte temporal consistente;
- cabeçalho da clínica, Paciente, Médico/CRM quando aplicável, período, data de emissão e paginação;
- marca de “rascunho” quando não finalizado;
- rodapé com identificador verificável e aviso de confidencialidade;
- sem scripts, links inseguros ou conteúdo remoto;
- download privado, curto, auditado e autorizado no momento da requisição;
- impressão/geração não inclui seções que o papel não pode visualizar.

### Privacidade e conformidade

Dados de saúde são dados pessoais sensíveis. O prontuário deve preservar finalidade, necessidade, confidencialidade, integridade, autenticidade, rastreabilidade e direitos do titular. A política de retenção final e os textos jurídicos precisam de validação profissional antes de produção.

Referências normativas para a implementação:

- [LGPD — Lei nº 13.709/2018, texto compilado](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709compilado.htm);
- [Lei nº 13.787/2018 — prontuário de paciente](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13787.htm);
- normas vigentes do CFM/SBIS deverão ser novamente verificadas na data da implementação, sem afirmar certificação antes de auditoria apropriada.

## Banco DB-First planejado

A análise física final ocorre no início da implementação. A migration seguinte à `0021` deverá contemplar, conforme confirmação pelo schema atual:

- número humano único do atendimento e backfill determinístico a partir de `100`;
- chegada do Paciente, senha diária e sequência concorrente por data operacional;
- livro-razão do caixa, fechamento diário e vínculo imutável entre movimentos;
- múltiplas tentativas históricas de pagamento e reversões, mantendo no máximo uma obrigação ativa/quitada não revertida;
- notificações duráveis do Médico e preferências administrativas tipadas;
- prontuário, encontros clínicos, anamneses/evoluções, versões/retificações, alertas e anexos;
- índices para número do atendimento, data/senha, caixa por dia/método, notificações não lidas e linha do tempo;
- constraints de moeda/valor/estado, FKs restritivas, timestamps UTC e `row_version`;
- triggers/permissões quando necessárias para impedir update/delete de ledger, versões finalizadas e auditoria.

Evitar uma tabela EAV ou JSON genérico como única fonte clínica. JSON poderá guardar snapshot versionado complementar, mas os campos essenciais e pesquisáveis serão tipados.

Depois de aplicar a migration inteira em `viverappweb` no MySQL 8.0.41, executar o scaffold reproduzível e revisar o diff gerado antes de escrever repositórios/serviços.

## API e contratos planejados

Áreas mínimas, com versionamento e DTOs explícitos:

- pacientes: resumo completo autorizado, detalhe e atualização concorrente;
- atendimentos: busca por número, registrar chegada, obter senha e transições;
- notificações do Médico: listar, contar, marcar como lida e hub autenticado;
- configurações: política de chegada, popup e áudio;
- caixa: resumo diário, movimentos paginados, fechamento e impressão;
- pagamentos: solicitar/consultar reversão e registrar novo pagamento;
- prontuário: resumo, timeline, encontros, rascunhos, finalização, retificação e PDF.

Queries devem projetar apenas o necessário, limitar intervalo/página, usar cancelamento e evitar N+1. Relatórios e totais são produzidos no servidor a partir do ledger, nunca somados pelo browser.

## Segurança obrigatória

- policies separadas por capacidade, não apenas por papel genérico;
- ownership do Médico validado no banco em toda consulta clínica;
- step-up MFA do Administrador para conteúdo clínico, reversão e exportação;
- justificativa e auditoria para acesso clínico por Gestor/Administrador;
- rate limiting específico para busca por número/CPF, PDFs, reversões, caixa e SignalR;
- antiforgery em mutações do BFF e origem estrita no hub;
- nenhuma enumeração de CPF, atendimento, prontuário ou notificação;
- campos clínicos e financeiros redigidos de logs, tracing, erros e outbox;
- R2 privado com autorização atual e nomes imprevisíveis;
- cache `no-store` em prontuário, caixa e PDFs;
- Content-Disposition seguro, MIME fixo e proteção contra arquivos ativos;
- testes de IDOR cruzados entre Médicos e entre os três papéis;
- auditoria não poderá ser enfraquecida nem apagada por limpeza de testes.

## Plano de execução dentro da fase

1. revalidar CodeGraph, schema atual, contratos e matriz de autorização;
2. fechar máquina de estados e contratos de números, chegada, caixa, reversão, notificações e prontuário;
3. criar/revisar/aplicar migration SQL no `viverappweb` e regenerar EF DB-First;
4. implementar domínio e persistência com testes de invariantes/concorrência;
5. implementar APIs e políticas com testes negativos;
6. implementar cards completos e edição de Paciente para Gestor/Admin;
7. implementar número do atendimento, chegada, senha diária e carinhas acessíveis;
8. implementar notificação durável + SignalR + sininho/popup/som e configuração Admin;
9. implementar caixa, reversão e novo pagamento;
10. implementar prontuário por papel, versionamento e PDF privado;
11. integrar navegação, busca e responsividade nos três perfis;
12. atualizar documentação/matriz afetada e registrar evidências;
13. executar build, suíte integral, testes MySQL, segurança e validação visual exclusivamente no monitor 3;
14. parar na Fase 16 e aguardar autorização de merge.

## Testes mínimos

### Banco e concorrência

- migration aplicada exatamente uma vez e scaffold coerente;
- números de atendimento únicos a partir de `100`, inclusive sob concorrência;
- senhas diárias únicas a partir de `100`, reiniciadas somente por data operacional;
- dupla chegada gera um registro/senha;
- ledger e versão clínica finalizada recusam update/delete;
- reversão duplicada e duplo pagamento concorrente são recusados.

### Autorização e privacidade

- Gestor/Admin editam apenas campos permitidos do Paciente;
- Médico A não acessa prontuário/notificação do Médico B;
- Gestor/Admin não assinam nem retificam registro médico;
- acesso sensível sem justificativa/step-up é recusado;
- PDF não vaza seções, cache ou URL privada;
- SignalR não aceita inscrição em atendimento alheio;
- caixas, filtros e busca não permitem IDOR ou enumeração.

### Financeiro

- cada forma de pagamento totaliza corretamente;
- entrada, reversão, suprimento, sangria e líquido fecham matematicamente;
- impressão completa contém todos os movimentos e totais;
- impressão de totais omite movimentos individuais;
- cancelamento gera compensação e permite novo pagamento somente no momento correto;
- falha/retry do PagBank não cria reversão ou cobrança duplicada.

### Interface e acessibilidade

- cards e máscaras em celular/tablet/desktop;
- status possui texto acessível além da carinha/cor;
- sininho anuncia contador e popup sem roubar foco;
- áudio habilitado, desabilitado e bloqueado pelo navegador;
- impressão A4 sem cortes;
- prontuário navegável por teclado e leitor de tela;
- zoom 200%, alto contraste e redução de movimento;
- validação visual somente no monitor 3.

## Cenário ponta a ponta obrigatório

1. Gestor localiza um Paciente, confere CPF/e-mail/telefone/endereço e corrige um dado com concorrência/auditoria.
2. Gestor localiza o atendimento pelo número humano, confirma e registra a chegada.
3. A API atribui senha diária a partir de `100`; o Médico recebe sininho, popup e som conforme configuração.
4. Médico abre o atendimento, consulta a linha do tempo, registra/finaliza anamnese/evolução e gera PDF autorizado.
5. Gestor registra pagamento; o caixa do dia reflete a entrada e os totais.
6. Gestor cancela o pagamento com motivo; o caixa recebe a reversão e o atendimento volta a aceitar novo pagamento.
7. Um segundo pagamento é registrado sem apagar o histórico anterior.
8. Gestor e Administrador consultam o prontuário em seus recortes, com justificativa/step-up quando necessário.
9. Administrador imprime o caixa completo e somente os totais, altera a política de popup/som e a nova política é aplicada sem perder notificações duráveis.

## Critérios de saída

- todos os seis grupos solicitados estão implementados, integrados e testados;
- cards de Paciente do Gestor/Admin exibem e editam CPF, e-mail, telefone e endereço;
- caixa diário/histórico e ambas as impressões fecham com o ledger;
- cancelamento de pagamento é compensatório, auditável e permite novo pagamento seguro;
- atendimento e senha diária possuem números inequívocos iniciados em `100`;
- chegada possui estado/carinha acessível e notifica o Médico por sininho, popup e áudio configuráveis;
- prontuário integrado existe para Gestor, Médico e Admin com autoria, versões, PDF e controles por papel;
- migration aplicada no MySQL local 8.0.41, EF regenerado e suíte integral verde;
- documentos e matriz das decisões anteriores estão coerentes com o novo acesso ao prontuário;
- nenhuma pendência real foi ocultada e a Fase 17 não foi iniciada.
