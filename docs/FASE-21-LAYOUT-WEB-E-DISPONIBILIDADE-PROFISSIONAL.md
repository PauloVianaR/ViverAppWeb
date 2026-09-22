# Fase 21 — Layout Web com navegação lateral e disponibilidade profissional variável

## Estado e limite da fase

**Estado:** planejada; nenhuma implementação iniciada.

Esta fase será executada individualmente, em branch própria, somente depois da integração e autorização de encerramento da Fase 20. Ela não altera o comportamento mobile aprovado.

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
