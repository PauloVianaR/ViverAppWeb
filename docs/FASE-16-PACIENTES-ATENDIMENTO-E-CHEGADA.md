# Fase 16 — Pacientes, atendimento e chegada

## Estado e objetivo

**Estado:** planejada; nenhuma implementação desta fase foi iniciada.

**Branch prevista:** `codex/fase-16-pacientes-atendimento-chegada`, criada a partir da `main` somente depois da integração formal da Fase 15.

Completar os dados operacionais do Paciente para Gestor e Administrador, atribuir identificadores humanos inequívocos aos atendimentos, registrar a chegada presencial com senha de ordem e avisar o Médico em tempo real por sininho, popup e som configuráveis.

Esta fase não implementa caixa, reversão financeira nem prontuário. Essas entregas pertencem exclusivamente às Fases 17 e 18.

## Regras invariáveis

- alterar somente `ViverAppWeb`;
- usar exclusivamente `viverappweb`; `viverappmobile` continua somente leitura;
- seguir DB-First: aplicar integralmente a migration SQL no MySQL local 8.0.41, validar o schema e só então regenerar o EF;
- nunca editar manualmente o `DbContext` ou entidades geradas;
- uma conta continua com exatamente um papel e o sistema continua atendendo uma única clínica;
- identidade, estados, números e permissões são autoridade do servidor;
- nenhuma PII ou dado clínico será incluído desnecessariamente em log, auditoria, popup ou som;
- mutações exigem antiforgery, autorização revalidada, concorrência otimista, idempotência quando aplicável e auditoria;
- nenhuma funcionalidade das Fases 17 ou 18 será antecipada.

## Escopo funcional

### Pacientes do Gestor e do Administrador

Cada card da lista deve exibir, com máscaras apropriadas:

- nome e nome preferido;
- CPF no formato `XXX.XXX.XXX-XX`;
- e-mail;
- telefone formatado com país/DDD;
- endereço resumido com logradouro, número, complemento, bairro, cidade/UF e CEP;
- status da conta, Premium e ações já existentes;
- estado explícito para campo ausente, sem inventar valor.

No celular, todos os dados permanecem legíveis sem rolagem horizontal. E-mail e endereço longos quebram linha; rótulos permanecem visíveis e ícones nunca serão o único meio de identificação.

### Edição dos dados

Gestor e Administrador terão a ação “Editar dados”, com:

- nome, nome preferido, CPF, e-mail e telefone;
- CEP mascarado como `XXXXX-XXX` e busca automática já adotada no cadastro;
- logradouro, número, complemento, bairro, cidade e UF;
- validação de dígitos verificadores e unicidade do CPF;
- normalização/validação de e-mail e persistência do telefone em E.164;
- confirmação segura do novo contato quando e-mail ou telefone forem alterados;
- detecção de conflito sem revelar a conta proprietária do dado;
- `row_version` e tratamento compreensível de concorrência;
- auditoria dos campos alterados com valores sensíveis redigidos;
- DTO explícito que não aceite papel, senha, MFA ou estado da conta por mass assignment.

Gestor e Administrador possuem a mesma capacidade operacional, mas ações administrativas elevadas continuam exigindo as policies e o step-up já definidos para o Administrador.

## Números do atendimento e da fila

Serão usados dois números, porque cadastro e chegada representam ordens diferentes.

### Número do atendimento

- sequencial global, imutável, único e iniciado em `100`;
- criado junto com o atendimento;
- visível em cards, detalhes, busca e comunicação operacional;
- usado para localização rápida, sem substituir o ID técnico;
- nunca reciclado após cancelamento;
- registros existentes recebem backfill determinístico, preservando sua ordem de criação;
- buscas pelo número continuam revalidando papel e escopo para impedir enumeração/IDOR.

### Senha de chegada

- sequencial diário e iniciada em `100` para cada data operacional;
- atribuída atomicamente apenas quando a chegada é confirmada;
- representa a ordem real de chegada, não a ordem de agendamento;
- única dentro da data operacional da clínica;
- persistida com `arrived_at_utc`, data local da clínica e autor;
- duas requisições concorrentes para a mesma chegada produzem uma única senha;
- senhas atribuídas não são reutilizadas no mesmo dia.

O fuso será obtido da configuração da clínica. Enquanto a decisão final estiver pendente, o código não deve espalhar `America/Sao_Paulo` como constante de domínio.

## Estados do atendimento

### Máquina de estados

A máquina atual deve ser revisada para incluir explicitamente a chegada:

`pending → confirmed → arrived → in_progress → completed`

Estados terminais/alternativos continuam sujeitos às regras existentes: `cancelled`, `no_show` e `rescheduled`. O estado do pagamento continua um eixo separado.

Regras mínimas:

- somente Gestor ou Administrador autorizado registra chegada;
- chegada exige atendimento presencial, confirmado, não terminal e dentro da janela configurada;
- repetir a mesma operação é idempotente;
- transição, autor, instante, motivo quando aplicável e versão anterior/posterior ficam auditados;
- Médico só inicia/conclui atendimento atribuído ao próprio escopo;
- chegada não confirma pagamento, não cria caixa e não conclui atendimento.

### Carinhas e acessibilidade

Os cards usarão expressão visual, cor, ícone e texto — nunca somente emoji:

- pendente: carinha triste e texto “Pendente”;
- confirmado: carinha positiva e texto “Confirmado”;
- chegou: expressão de presença/destaque, texto “Paciente chegou” e senha diária;
- em atendimento, concluído, cancelado e falta: ícones/textos próprios e coerentes.

A mesma linguagem será aplicada às agendas de Gestor, Médico e Administrador. Tabelas, leitores de tela e futuras impressões usarão rótulos textuais.

## Notificação de chegada ao Médico

### Fluxo durável

Ao registrar a chegada:

1. a transação grava chegada, senha, histórico de status e evento de outbox;
2. um consumidor cria uma notificação durável apenas para o Médico atribuído;
3. SignalR entrega o evento às sessões autorizadas;
4. o shell do Médico atualiza o sininho, indicador/contador não lido, popup e áudio;
5. se o Médico estiver desconectado, a notificação aparece ao reconectar.

A fase pode usar o processamento interno já existente para essa entrega específica. O endurecimento geral de e-mail/SMS e jobs permanece na Fase 19.

### Sininho

- presente no shell do Médico em todos os tamanhos de tela;
- bolinha e contador de não lidas com nome acessível;
- lista de lidas/não lidas ordenada por instante;
- ação para abrir o atendimento com ownership revalidado;
- marcar uma ou todas como lidas;
- deduplicar reconexões e múltiplas abas;
- paginação/limite e retenção configurada;
- popup contém apenas número/senha, horário e indicação de chegada;
- nenhuma anamnese, diagnóstico, documento ou observação clínica no evento.

### Popup e áudio

- toast visível e anunciável sem roubar foco durante digitação;
- som curto, não alarmante e sempre acompanhado por alternativa visual;
- respeito a `prefers-reduced-motion` e tecnologias assistivas;
- inicialização de áudio depois da primeira interação autenticada, devido às políticas de autoplay;
- orientação não intrusiva se o navegador bloquear o som;
- falha de áudio ou SignalR nunca perde o registro durável;
- nada de nome completo, CPF ou conteúdo clínico no arquivo/locução sonora.

### Configuração administrativa

O Administrador configura para a clínica:

- notificações de chegada habilitadas;
- popup habilitado;
- som habilitado;
- volume padrão e som dentre assets locais aprovados;
- janela antecipada/tardia para registrar chegada;
- retenção e política de leitura das notificações.

O Médico pode silenciar o som somente na sessão/navegador atual, sem alterar a política global. O sininho durável permanece ativo.

## Banco DB-First planejado

A análise física final ocorre no começo da implementação. A migration seguinte à `0021` deve contemplar, conforme o schema real:

- número humano único e sequencial do atendimento, com backfill a partir de `100`;
- chegada, data operacional, senha diária e autor;
- mecanismo de sequência concorrente por data;
- histórico da nova transição;
- notificações duráveis do Médico e estado de leitura;
- configurações tipadas de chegada, popup e som;
- índices para número do atendimento, data/senha e notificações não lidas;
- constraints, FKs restritivas, timestamps UTC e `row_version`.

Não criar migration de caixa, reversão ou prontuário nesta fase. Depois de aplicar integralmente a migration em `viverappweb` no MySQL 8.0.41, executar o scaffold reproduzível e revisar o diff gerado.

## API e contratos planejados

- listar/detalhar Pacientes com os novos campos autorizados;
- atualizar os dados operacionais com contrato mínimo e concorrência;
- buscar atendimento por número humano sem enumeração;
- registrar chegada e devolver a senha diária;
- obter estado/linha operacional do atendimento;
- listar/contar notificações do Médico;
- marcar uma/todas como lidas;
- hub SignalR autenticado e restrito ao Médico destinatário;
- consultar/alterar a configuração administrativa de chegada e avisos.

Queries devem projetar somente o necessário, limitar página/período, aceitar cancelamento e evitar N+1.

## Segurança obrigatória

- policies separadas para editar Paciente, registrar chegada, ler notificações e alterar configuração;
- ownership do Médico validado no banco e novamente ao abrir a entidade relacionada;
- antiforgery nas mutações e origem estrita no hub;
- rate limiting para busca por número/CPF, chegada, leitura e SignalR;
- proteção contra IDOR, enumeração, mass assignment e replay;
- PII redigida de logs, tracing, erros, auditoria e outbox;
- notificação sem conteúdo clínico sensível;
- cache `no-store` nas respostas pessoais/operacionais;
- eventos de auditoria continuam append-only;
- testes não podem apagar auditoria para realizar limpeza.

## Plano de execução

1. consultar CodeGraph, schema, páginas, serviços e matriz de autorização atuais;
2. fechar contratos de números, estados, chegada, notificação e configuração;
3. criar, revisar e aplicar a migration no `viverappweb` MySQL 8.0.41;
4. regenerar EF DB-First e revisar somente os artefatos gerados;
5. implementar domínio/persistência e testes de concorrência;
6. implementar APIs, policies e testes negativos;
7. completar cards e edição de Paciente no Gestor/Admin;
8. implementar número, chegada, senha e carinhas acessíveis;
9. implementar notificação durável, SignalR, sininho, popup e som;
10. implementar configuração administrativa e navegação responsiva;
11. atualizar documentação/matriz afetada e registrar evidências;
12. executar build, suíte integral, testes MySQL e segurança;
13. validar exclusivamente no monitor 3 em celular, tablet e desktop;
14. parar sem iniciar a Fase 17.

## Testes mínimos

### Banco e concorrência

- migration aplicada uma vez e scaffold coerente;
- números de atendimento únicos e a partir de `100` sob concorrência;
- backfill dos registros existentes sem colisão;
- senhas diárias únicas e iniciadas em `100` em cada data;
- dupla chegada produz um único registro, estado, senha e notificação;
- transições inválidas ou tardias são recusadas.

### Autorização e privacidade

- Gestor/Admin editam somente os campos permitidos;
- Médico não obtém edição administrativa por manipulação de rota;
- Médico A não recebe/lê notificação do Médico B;
- busca por CPF/número não enumera pacientes ou atendimentos alheios;
- SignalR recusa inscrição indevida;
- popup, áudio, logs e auditoria não vazam PII/conteúdo clínico.

### Interface

- cards e máscaras funcionam em celular/tablet/desktop;
- carinha/cor possuem texto equivalente;
- sininho anuncia contador e popup sem roubar foco;
- som funciona habilitado, desabilitado e bloqueado pelo navegador;
- alteração administrativa reflete sem perder notificações;
- teclado, leitor de tela, touch, redução de movimento e zoom 200%;
- validação visual somente no monitor 3.

## Cenário ponta a ponta obrigatório

1. Gestor localiza um Paciente, confere CPF/e-mail/telefone/endereço e corrige um dado com concorrência/auditoria.
2. Gestor encontra o atendimento pelo número humano, confirma e registra a chegada.
3. A API atribui uma senha diária a partir de `100` uma única vez.
4. Médico atribuído recebe sininho, bolinha, popup e som conforme configuração.
5. Médico abre o atendimento pela notificação; outro Médico tem o acesso negado.
6. Administrador desabilita o áudio e mantém o sininho; nova chegada respeita a configuração.
7. As agendas dos três perfis exibem carinhas e textos coerentes para todos os estados.

## Critérios de saída

- cards do Gestor/Admin exibem e editam CPF, e-mail, telefone e endereço;
- número permanente do atendimento e senha diária de chegada são distintos e iniciados em `100`;
- máquina de estados contém chegada idempotente e auditada;
- cards exibem carinhas com equivalentes acessíveis;
- Médico recebe notificação durável por sininho, popup e áudio configurável pelo Admin;
- migration foi aplicada no MySQL local 8.0.41 e o EF foi regenerado por DB-First;
- build, suíte integral, segurança e validação visual estão aprovados;
- Fases 17 e 18 não foram antecipadas;
- nenhuma pendência real foi ocultada.
