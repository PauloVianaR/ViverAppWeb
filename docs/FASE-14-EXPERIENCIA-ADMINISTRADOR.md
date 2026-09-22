# Fase 14 — Experiência completa do Administrador e fechamento da paridade

## Objetivo

Entregar todas as capacidades administrativas observadas no MAUI e encerrar a matriz de paridade das Fases 10 a 14, sem permitir que a operação administrativa rompa sigilo clínico ou controles elevados.

**Branch prevista:** `codex/fase-14-experiencia-administrador`.

## Evidências obrigatórias

Imagens autorizadas: `Admin.png`, `inicio.png`, `Clínica.png`, `Consultas.png`, `Analytics.png`, `Notificações.png` e `Usuarios.png`, em `ImagensPlayStore/Admin`.

Fontes permitidas principais:

- `AdminMainPage`, `AdminHomeView`, `AdminClinicView`, `AdminAppointmentsManagementView`, `AdminAnalyticsView`, `AdminNotificationView`, `AdminUserManagementView` e `AdminPremiumManagementPage`;
- popups de edição de serviço, cancelamento, reagendamento, confirmação de pagamento, análise Premium, laudo e anexos;
- view models, serviços, controllers e enums correspondentes;
- matriz consolidada das Fases 10 a 13.

## Navegação e identidade

- cor contextual vermelha, rótulo Administrador e aviso de sessão elevada quando aplicável;
- Início, Clínica, Consultas, Analytics, Notificações e Usuários/Premium;
- MFA obrigatório e sessão administrativa curta;
- step-up para aprovação, bloqueio, Premium, financeiro e configuração crítica;
- impedir que o último Administrador recuperável bloqueie a si mesmo ou remova o próprio acesso sem procedimento seguro.

## Início e aprovações

- indicadores de usuários ativos, consultas do dia, pacientes Premium e cadastros pendentes;
- atalhos para clínica, analytics, notificações, usuários, Premium e consultas;
- fila de Médico/Gestor pendentes;
- detalhe dos dados necessários à decisão;
- aprovar ou rejeitar com motivo;
- reabrir análise quando permitido;
- permitir/bloquear atendimento online de Médico;
- confirmar pagamento presencial;
- cancelar/reagendar consulta;
- auditar autor, motivo, antes/depois, instante e correlação.

A superfície mínima criada na Fase 10 será absorvida pela administração definitiva sem duplicar endpoints ou regras.

## Clínica única

### Informações

- razão social, nome fantasia, CNPJ, telefone e e-mail;
- CEP, logradouro, número, complemento, bairro, cidade e UF;
- busca de CEP com fallback manual;
- concorrência otimista e histórico auditável.

### Serviços

- CRUD de consultas, exames e cirurgias;
- tipo, nome, descrição, preço e duração média;
- modalidade/flag online quando aplicável;
- pesquisa e filtros;
- ativar/desativar;
- editar pelo fluxo equivalente ao popup legado;
- excluir somente sem vínculo impeditivo, oferecendo desativação quando houver histórico;
- preço/duração nunca confiados ao browser em agendamento/pagamento.

### Horários e feriados

- dias ativos/fechados da semana;
- hora inicial/final por dia;
- validação de faixa e sobreposição;
- criar/editar/remover feriado ou bloqueio;
- recorrência anual somente quando explicitamente configurada;
- impacto em agenda futura mostrado antes da confirmação.

### Sistema

- disponibilidade/maintenance mode do Web;
- permissão e janela de cancelamento;
- horizonte máximo de agendamento;
- chamadas online;
- durações padrão de consulta/exame/cirurgia;
- intervalo entre atendimentos;
- desconto Premium;
- preferências de comunicação aplicáveis;
- chaves semânticas tipadas, defaults validados e histórico;
- configurações exclusivamente móveis, Firebase/push e versão mínima do app não serão copiadas sem equivalente Web justificado.

## Consultas e agendamentos

- abas Planejados e Histórico;
- busca e filtros por paciente, Médico, serviço/tipo, estado, pagamento, modalidade e período;
- paginação, ordenação e limpar filtros;
- cards/tabela adaptativa com dados operacionais completos;
- detalhe, cancelamento com motivo e reagendamento com nova data/horário;
- confirmação de pagamento presencial com método, data/hora, últimos quatro dígitos aplicáveis e autorização;
- acompanhar checkout/link e estado reconciliado;
- visualizar metadados de laudo/anexos, não conteúdo clínico completo;
- acesso indevido ao conteúdo legado será substituído pela regra de sigilo da Fase 8.

## Analytics

- períodos de um, três, seis e doze meses, além de intervalo limitado;
- KPIs de receita, consultas, ticket médio e satisfação;
- comparação segura com período anterior;
- os nove gráficos do MAUI, sem redução funcional: Evolução da Receita (Premium versus Regular), Receita versus Consultas, Pagamentos por Tipo, Distribuição por Tipo de Pagamento, Tendência pagamentos Online versus Presencial, Online versus Presencial, Distribuição de Serviços, Distribuição de Tipos de Atendimento e Performance dos Médicos;
- estados dos agendamentos como resumo operacional adicional da Web;
- queries calculadas no servidor, limitadas, paginadas e medidas;
- gráficos com resumo textual e tabela acessível;
- nenhuma métrica expõe laudo, observação ou identificador desnecessário.

## Notificações

- contadores de pagamentos pendentes/inadimplência, não lidas, alta severidade e aprovações;
- filtros por tipo, severidade, leitura e período;
- tipos: atualização do sistema, aprovação pendente, pagamento pendente, reagendamento, cancelamento, conclusão, pagamento aprovado, Premium pendente e decisão Premium;
- abrir entidade relacionada com autorização revalidada;
- marcar uma ou todas como lidas;
- dispensar/remover da coleção quando permitido;
- texto sem dado clínico sensível;
- e-mail/SMS externos quando configurados, nunca Firebase/push;
- operação durável completa será endurecida na Fase 22.

## Usuários

- indicadores total, ativos, pendentes e Premium;
- abas/filtros para pendentes, pacientes, médicos, gestores e rejeitados;
- busca, status, ordenação e paginação;
- detalhes de Médico com CRM, especialidade, experiência, avaliação e online;
- aprovar/rejeitar Médico/Gestor com motivo;
- reabrir cadastro rejeitado;
- bloquear/desbloquear Paciente, Médico, Gestor ou Administrador conforme salvaguardas;
- nunca trocar silenciosamente o papel único;
- notificar decisões por e-mail/SMS;
- trilha de auditoria completa e step-up.

## Premium

- indicadores aprovados e aguardando;
- busca, filtros e estados;
- abrir comprovante privado após reautorização;
- registrar observações de análise;
- aprovar ou rejeitar com motivo;
- cancelar benefício;
- respeitar cooldown e concorrência;
- auditar acesso ao documento e decisão;
- storage definitivo/reconciliação seguem nas Fases 15 e 22.

## Segurança administrativa

- MFA obrigatório e recovery codes protegidos;
- step-up recente em ações de alto impacto;
- rate limit por ação e ator;
- antiforgery e confirmação explícita;
- proteção contra IDOR, mass assignment, exportação excessiva e consultas caras;
- nenhum segredo, documento ou conteúdo clínico em logs;
- auditoria append-only;
- políticas negativas entre Administrador, Gestor e Médico;
- console nunca exibe payload bruto de outbox, pagamento ou documento.

## Banco e API

- revisar lacunas de notificações, decisões, configurações e agregações;
- criar/aplicar todas as migrations em `viverappweb` no MySQL 8.0.41 antes do scaffold;
- views/queries de analytics somente quando medidas e versionadas;
- DTOs mínimos e contratos paginados;
- constraints para papel único, decisões concorrentes, configuração tipada e auditoria;
- nenhuma escrita em `viverappmobile`.

## Responsividade e acessibilidade

- sidebar desktop, navegação compacta em tablet e drawer/fluxo adequado no celular;
- tabela/card alternáveis para consultas/usuários;
- filtros densos preservam estado;
- gráficos com alternativa textual;
- dialogs e step-up operáveis por teclado;
- foco, contraste, touch targets, leitor de tela e zoom 200%;
- validar todos os breakpoints definidos na Fase 10.

## Fechamento da paridade

Ao fim da implementação, consolidar uma matriz com:

`ID → imagem/tela MAUI → fonte de comportamento → regra → papel/policy → endpoint → página/componente → testes → estado`.

Cada uma das imagens autorizadas, 33 páginas principais, 13 popups e respectivos comandos relevantes deve aparecer como `coberto` ou `substituído por decisão explícita`. Itens `ausente` ou `parcial` bloqueiam o merge.

Substituições já aprovadas:

- Firebase/push por e-mail/SMS;
- senha reversível por hash/redefinição segura;
- multiclínica por clínica singleton;
- múltiplos papéis por exatamente um papel;
- cadastro público de Administrador por provisionamento seguro;
- Google Pay separado por PagBank Checkout;
- gravação de vídeo desativada por padrão;
- acesso administrativo/gerencial ao laudo completo por metadados, preservando sigilo;
- regras do cliente por autoridade da API;
- layout estritamente móvel por Web responsiva.

Qualquer outra substituição exige autorização do proprietário e atualização da matriz.

## Cenário ponta a ponta obrigatório

Administrador com MFA entra, aprova Médico/Gestor, configura clínica/serviços/horários/feriados/sistema, acompanha agenda, cancela/reagenda, confirma pagamento presencial, analisa usuários/Premium/notificações e confere analytics; em seguida, a suíte cruza as jornadas completas dos quatro papéis e tenta elevação horizontal/vertical.

## Critério de saída

- todas as capacidades administrativas estão implementadas e testadas;
- a matriz integral das Fases 10 a 14 não contém item ausente/parcial;
- sigilo clínico, MFA, step-up e auditoria foram comprovados;
- todos os breakpoints e WCAG 2.2 AA foram revisados;
- migrations/scaffold/build/testes/segurança aprovados;
- a Fase 15 não foi antecipada.
