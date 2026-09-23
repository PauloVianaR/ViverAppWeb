# Fase 21 — Layout Web com navegação lateral e disponibilidade profissional variável

## Estado e limite da fase

**Estado:** implementada na branch `codex/fase-21-layout-disponibilidade-profissional`, aguardando revisão e autorização do proprietário para integrar à `main`.

Esta fase será executada individualmente, em branch própria, somente depois da integração e autorização de encerramento da Fase 20. Ela não altera o comportamento mobile aprovado.

## Entrega e verificação

- A Fase 20 foi integrada à `main` antes da criação desta branch. O novo shell Web tem menu lateral expandido/recolhido por conta e configuração administrativa reversível `web.desktop_sidebar_enabled`, padrão `true`; abaixo do breakpoint permanece a navegação inferior existente.
- Médicos e psicólogos podem manter disponibilidade semanal ou cadastrar datas variáveis. Gestor e Administrador selecionam o profissional; permissões e impacto em atendimentos futuros são validados na API. O calendário sinaliza datas cadastradas, feriados, dias com atendimento e conflitos. A geração de horários de agendamento usa somente o modo ativo e mantém feriados e exceções.
- A migration `0039__desktop_sidebar_and_variable_availability.sql` foi executada no banco local `viverappweb` MySQL 8.0.41, verificada pelo runner e seguida de scaffold DB-First. Nenhuma escrita foi feita no banco legado.
- Compilação de API e Web sem avisos ou erros. As sete suítes totalizaram 230 testes aprovados, incluindo cálculo de slots em modo variável, contratos de autorização, persistência e reversibilidade do flag com preferência de recolhimento isolada por conta.
- No navegador integrado, a navegação expandida e recolhida foi conferida em desktop, com persistência após recarga; o calendário e a prévia de mudança de modo foram conferidos no perfil de Gestor. Em 390 px, o calendário, editor e menu inferior existente foram conferidos sem alteração do shell mobile.
- A inspeção visual autenticada individual de Administrador, Médico e Psicólogo, o zoom nativo de 200% e a ativação/desativação real do flag administrativo permanecem registrados em `.local/PENDENCIAS.md` para homologação complementar, sem criar usuários ou alterar disponibilidade operacional somente para o teste.

## Objetivos

1. aproveitar melhor a largura de notebook e desktop com uma navegação lateral clara, recolhível e reversível por configuração administrativa;
2. manter integralmente o shell e a navegação inferior do mobile;
3. permitir disponibilidade profissional recorrente ou variável por datas, com horários sugeridos a partir do funcionamento da clínica;
4. aplicar a mesma regra a Médicos e Psicólogos, respeitando o escopo de cada papel.

## Parte A — Layout Web e navegação lateral

### Comportamento esperado

- Em larguras de desktop, cada perfil terá uma barra lateral com ícone e nome de todas as áreas permitidas.
- A barra poderá ser recolhida para mostrar somente ícones, sem perder `title`, texto acessível, indicação da rota atual ou navegação por teclado.
- O conteúdo utilizará melhor a largura disponível, com limites de leitura apenas onde forem necessários; dashboards, agenda, tabelas e prontuário poderão ocupar uma área maior.
- O estado expandido/recolhido será lembrado por usuário e não será compartilhado entre contas no mesmo dispositivo.
- Abaixo do breakpoint definido pelo design system, o aplicativo continuará usando exatamente a experiência mobile atual.
- Zoom de 200%, teclado, leitor de tela, contraste, foco e redução de movimento continuarão atendendo WCAG 2.2 AA.

### Reversibilidade obrigatória

- Criar a configuração administrativa booleana `web.desktop_sidebar_enabled`, padrão `true`.
- Quando `false`, o sistema renderizará o shell Web anterior, sem depender de rollback de código ou banco.
- A configuração será lida no servidor e não poderá ser manipulada pelo navegador para ampliar permissões.
- Os dois shells usarão a mesma fonte de rotas autorizadas para evitar divergência de acesso.

### Estrutura proposta

- extrair um catálogo tipado de itens de navegação por papel;
- criar componentes separados para `DesktopSidebar`, `LegacyDesktopNavigation` e o shell mobile existente;
- persistir somente a preferência visual de recolhimento na preferência de UI da conta;
- usar tokens do design system para largura expandida, largura recolhida, espaçamento e transições;
- manter rotas, autorização e deep links atuais.

## Parte B — Disponibilidade recorrente e variável

### Modos

Cada Médico ou Psicólogo terá um modo explícito:

- **Recorrente:** grade semanal vigente, com dias da semana, intervalos, modalidade e período de validade opcional.
- **Variável:** calendário para selecionar datas específicas de um mês ou ano e informar uma ou mais faixas de horário em cada data.

O modo escolhido será a fonte principal de disponibilidade. Exceções e bloqueios continuarão tendo precedência sobre os dois modos.

### Calendário variável

- permitir seleção individual e múltipla de dias;
- ao selecionar uma data, preencher inicialmente os intervalos com o horário de funcionamento da clínica naquele dia da semana;
- permitir editar, dividir ou remover intervalos antes de salvar;
- impedir faixas invertidas, sobrepostas, fora do funcionamento presencial da clínica ou em feriado/bloqueio incompatível;
- permitir modalidade presencial, online ou ambas conforme as configurações do profissional e da clínica;
- suportar navegação mensal e anual sem carregar o ano inteiro de forma desnecessária;
- mostrar conflitos com atendimentos existentes antes de remover disponibilidade já utilizada.

### Precedência para cálculo de horários

1. profissional ativo e vinculado ao tipo de atendimento;
2. modo de disponibilidade do profissional;
3. datas/faixas recorrentes ou variáveis ativas;
4. funcionamento da clínica e feriados;
5. exceções específicas do profissional;
6. modalidade, limites diários, antecedência e horizonte de agendamento;
7. conflitos com atendimentos existentes.

Todas as datas serão interpretadas no fuso da clínica e persistidas de modo inequívoco. O cálculo final continuará exclusivamente no servidor.

### Autorização

| Operação | Administrador | Gestor | Médico/Psicólogo |
|---|---:|---:|---:|
| Consultar disponibilidade de qualquer profissional | Sim | Sim | Não |
| Alterar modo/grade de qualquer profissional | Sim | Sim, se a configuração vigente permitir | Não |
| Consultar e alterar a própria disponibilidade | Sim | Sim | Sim |
| Alterar disponibilidade de outro profissional | Sim | Sim | Não |

O profissional autenticado nunca poderá trocar o identificador da rota para alterar a agenda de outra conta.

## Banco e DB-First

Antes de codificar as telas, a fase deverá desenhar e aplicar migrations SQL no `viverappweb`, MySQL 8.0.41. Uma estrutura provável inclui:

- modo de disponibilidade no perfil/preferência profissional;
- datas variáveis de disponibilidade;
- intervalos por data, com modalidade, estado e concorrência otimista;
- índices por profissional/data/modalidade;
- constraints que impeçam intervalos inválidos;
- auditoria de mudança de modo e faixas.

O schema real será decidido na fase após inspeção do banco. Todas as migrations serão aplicadas antes do scaffold DB-First, e os arquivos gerados não serão editados manualmente.

## API e concorrência

- endpoints paginados/recortados por período para calendário variável;
- operações em lote limitadas para selecionar vários dias sem payload irrestrito;
- `row_version` e resposta 409 em edição concorrente;
- validação transacional ao trocar de recorrente para variável e vice-versa;
- prévia de impacto sobre horários futuros antes de uma mudança destrutiva;
- auditoria com ator, profissional afetado, período e resumo, sem dados clínicos;
- cache curto apenas para consultas que sejam invalidadas por mudanças de agenda.

## Experiência de uso

- Administrador e Gestor escolherão o profissional antes de editar a disponibilidade.
- Médico/Psicólogo entrarão diretamente na própria agenda.
- A tela explicará claramente qual modo está ativo e o efeito de trocar de modo.
- O calendário indicará dias disponíveis, indisponíveis, feriados, conflitos e dias com atendimento.
- Ações em lote terão confirmação e resumo antes de salvar.
- Estados vazio, carregando, erro e conflito serão responsivos e acessíveis.

## Sequência de implementação

1. inventariar shells, rotas e preferências visuais atuais com CodeGraph;
2. definir breakpoints e medir telas largas sem alterar o mobile;
3. modelar a configuração reversível e preferência por conta;
4. implementar o shell lateral atrás da configuração e validar todos os papéis;
5. desenhar migrations da disponibilidade variável e aplicar no MySQL local;
6. regenerar o scaffold DB-First;
7. implementar regras de domínio e cálculo de horários;
8. implementar APIs e matriz de autorização;
9. implementar calendário e edição recorrente/variável;
10. executar regressões de agendamento, conflitos e responsividade;
11. validar visualmente no navegador integrado do Codex em mobile, tablet, notebook e desktop;
12. registrar evidências e pendências locais antes de solicitar integração.

## Testes obrigatórios

- shell novo e antigo sob a configuração administrativa;
- persistência do estado recolhido por conta;
- ausência de alteração visual/funcional no mobile;
- autorização cruzada entre Administrador, Gestor, Médico e Psicólogo;
- recorrência semanal, datas variáveis, feriados, exceções e mudança de modo;
- faixas sobrepostas, fora da clínica e com atendimento existente;
- concorrência de duas edições e tentativas de mass assignment;
- cálculo de slots para todos os tipos/modalidades vinculados;
- acessibilidade e responsividade nos breakpoints do projeto.

## Critérios de saída

- navegação lateral utilizável nos quatro perfis e reversível pela configuração do Administrador;
- shell mobile sem regressões;
- disponibilidade recorrente e variável funcionando para Médico e Psicólogo;
- Administrador/Gestor controlando qualquer profissional e profissional restrito à própria conta;
- migrations aplicadas e verificadas em `viverappweb`/MySQL 8.0.41, scaffold regenerado e suíte verde;
- validação visual concluída no navegador integrado do Codex;
- nenhuma implementação da fase seguinte iniciada.

## Ajustes de homologação de 22/09/2026

- O editor de datas variáveis agora impede selecionar datas passadas ou além dos dois anos permitidos pela API; falhas de validação exibem a causa em português, sem stack JavaScript, também no aviso global. A semana de **14 a 18/06/2027**, 08h–18h, foi salva pelo navegador integrado para a Psicóloga Validação. O mesmo intervalo em junho de **2026** já é passado e fica indisponível na interface.
- Agenda diária, semanal, mensal e anual não inclui atendimentos concluídos ou cancelados. O calendário de disponibilidade também não os marca como dias ocupados/conflitos. Os testes no MySQL comprovam a retirada de `BookedDates` e cobrem Paciente, Psicólogo, Gestor e Administrador nas quatro visualizações da Agenda.
- Calendário e edição por data aparecem somente no modo variável selecionado; grade semanal aparece somente no modo recorrente, nos acessos de Gestor, Administrador e profissional.
- A navegação lateral passa a receber a rota atual de fato, destacando o item/ícone ativo na cor do perfil e sem marcar rotas secundárias.
- Tipos de atendimento sem qualquer agendamento recebem a ação **Excluir**, com confirmação, proteção por `row_version`, remoção dos vínculos profissionais, bloqueio server-side caso passem a ter agendamentos e auditoria. O fluxo de desativação dos tipos usados permanece.
- As funções de Clínica do Gestor e do Administrador foram agrupadas em seções recolhidas por padrão. Cards e detalhes financeiros de atendimentos gratuitos mostram **Sem cobrança**.
- Verificações: build sem avisos; 233 testes aprovados nos sete projetos; MySQL 8.0.41, `viverappweb` e migrations conferidos. No navegador integrado foram conferidos o Gestor em desktop, tablet e celular, o menu lateral aberto/recolhido, as seções recolhidas e o salvamento da semana variável da psicóloga de teste. O próprio perfil da Psicóloga também foi conferido, com grade semanal somente no recorrente e calendário somente no variável. A inspeção visual individual de Administrador e Médico continua registrada em `.local/PENDENCIAS.md`.

## Ajustes complementares de 23/09/2026

- Agendamento e reagendamento dos quatro acessos usam um calendário mensal que só habilita datas com horário realmente disponível para o paciente, serviço, modalidade e profissional escolhidos. A API retorna apenas as datas do mês; a lista de horários é consultada somente ao selecionar um dia, evitando respostas grandes no circuito Blazor. A validação final do agendamento permanece no servidor.
- No editor variável, datas anteriores a hoje ficam esmaecidas, hoje recebe destaque e a seleção de datas/intervalos permanece ao navegar entre meses; ela só é limpa após salvar, remover ou trocar de modo. A seleção entre setembro e outubro de 2026 foi conferida no navegador integrado sem gravação adicional.
- As listas de Atendimentos dos quatro acessos exibem a legenda de cores e estados dos cards. Gestor, Administrador e Médico/Psicólogo oferecem a ação explícita **Marcar como não compareceu** quando um atendimento confirmado/com chegada já atingiu seu horário; a confirmação antecede a alteração. Atendimentos `no_show`, assim como concluídos e cancelados, deixam de aparecer nas visualizações da Agenda.
- Verificações: build sem avisos; 234 testes aprovados nos sete projetos em execução sequencial; MySQL local 8.0.41, `viverappweb` e migrations verificados. A execução paralela inicial teve duas colisões de fixture no banco compartilhado; ambos os testes passaram isoladamente e toda a suíte passou sem concorrência entre projetos. No navegador integrado, o Gestor foi conferido em desktop, tablet e celular: em junho de 2027 somente 14–18/06 ficaram selecionáveis para a Psicóloga Validação, e 14/06 abriu os horários sem erro nem desconexão. A ação de não comparecimento e seu diálogo foram conferidos em atendimento confirmado, sem alterar dados reais.
