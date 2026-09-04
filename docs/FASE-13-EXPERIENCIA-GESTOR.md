# Fase 13 — Experiência completa do Gestor

## Objetivo

Entregar todas as capacidades do perfil Gestor observadas no MAUI, separando claramente operação administrativa da clínica e autoria clínica reservada ao Médico.

**Branch prevista:** `codex/fase-13-experiencia-gestor`.

## Evidências obrigatórias

Imagens autorizadas: `Gerente.png`, `Inicio.png`, `Agenda.png`, `Histórico.png` e `Perfil.png`, em `ImagensPlayStore/Gerente`.

Fontes permitidas principais:

- `ManagerMainPage`, `ManagerHomeView`, `ManagerAgendaView`, `ManagerPatientListView`, `ManagerHistoricView`, `ManagerProfileView` e `ManagerSchedulePage`;
- popups de detalhe, cancelamento, reagendamento, confirmação de pagamento, paciente, análise Premium, laudo, anexos e senha;
- view models, serviços e controllers correspondentes.

## Navegação e identidade

- cor contextual laranja e rótulo “Gestor”;
- Início, Agenda, Pacientes, Histórico e Perfil;
- todos os Gestores pertencem implicitamente à clínica única;
- URL, filtro ou ID nunca amplia as policies do papel.

## Início operacional

- indicadores de atendimentos de hoje, médicos do dia, confirmados/pagos, pagamento pendente, online e presenciais;
- agenda do dia em cards;
- abrir detalhes;
- ligar para o Paciente por gesto explícito;
- confirmar pagamento presencial conforme policy;
- atalhos para Agenda, Pacientes, Histórico e Perfil;
- estados vazio, loading, erro, sessão expirada e retry seguro.

## Agenda da clínica

- totais online, presenciais, reagendados e geral no intervalo;
- pesquisa e filtros por modalidade, tipo, médico, pagamento, estado, data e horário;
- limpar filtros, ordenação e paginação;
- cards/tabela adaptativa com paciente, médico, serviço, estado, pagamento, reagendamento, data, duração, modalidade, local, preço e observações autorizadas;
- ligar para paciente;
- abrir detalhes;
- cancelar com motivo;
- reagendar escolhendo nova data/horário;
- confirmar pagamento presencial;
- Gestor pode operar estados administrativos permitidos, mas não criar/editar/retificar laudo médico.

## Confirmação de pagamento presencial

- selecionar forma de pagamento permitida;
- registrar data e hora;
- registrar últimos quatro dígitos somente para cartão aplicável;
- registrar número de autorização quando existir;
- recalcular valor e elegibilidade no servidor;
- exigir confirmação explícita e idempotência;
- impedir duplicidade ou alteração de pagamento já reconciliado;
- auditar autor, instante, método, referência e estado anterior/posterior sem armazenar dados completos de cartão.

## Pacientes

- indicadores de total, Premium, ativos e bloqueados;
- busca, filtros e paginação;
- adicionar paciente em nome da clínica;
- editar campos operacionais autorizados;
- detectar CPF/e-mail/telefone já existentes e impedir takeover;
- nunca escolher senha conhecida pelo Gestor para uma conta existente; novo paciente recebe onboarding seguro quando necessário;
- ligar para o Paciente;
- agendar para o Paciente;
- abrir análise Premium quando a policy específica permitir;
- não acessar conteúdo clínico reservado.

## Agendamento para paciente

1. selecionar paciente autorizado;
2. escolher Consulta, Exame ou Cirurgia;
3. selecionar serviço e modalidade;
4. selecionar Médico elegível;
5. escolher data/slot calculado pela API;
6. registrar observações;
7. revisar preço e desconto Premium do servidor;
8. confirmar idempotentemente com auditoria.

O servidor valida horário da clínica, feriados, disponibilidade/limite do Médico, conflitos, modalidade e estado do paciente.

## Histórico

- pesquisa e filtros por modalidade, tipo, Médico, estado e período;
- paginação e limpar filtros;
- cards dos atendimentos concluídos/cancelados/faltas;
- detalhes operacionais e financeiros permitidos;
- visualizar existência, autoria, versão e data de laudo, não seu conteúdo;
- visualizar metadados de anexos, não baixar conteúdo clínico sem policy excepcional explícita;
- visualizar avaliação/feedback apenas no grau necessário à operação;
- toda tentativa de acesso indevido é negada no servidor.

## Premium

Quando autorizado, o Gestor poderá:

- identificar solicitação pendente na lista de Pacientes;
- inspecionar comprovante privado;
- registrar observações;
- aprovar ou rejeitar com motivo;
- respeitar estado concorrente e cooldown;
- gerar notificação ao Paciente;
- auditar decisão e acesso ao documento.

A policy poderá reservar a decisão final ao Administrador. Isso deve ser configurado no servidor e não inferido da interface.

## Perfil e segurança

- editar nome, e-mail, telefone e CPF conforme política;
- confirmar contato alterado;
- preferências de e-mail/SMS, sem push;
- alterar senha;
- gerenciar Google, passkeys e sessões/dispositivos;
- logout e revogação segura;
- mensagens de segurança e atividade sem expor PII desnecessária.

## Banco e API

- revisar lacunas de pagamentos presenciais, referências de autorização e capacidades do Gestor;
- criar/aplicar todas as migrations no `viverappweb` em MySQL 8.0.41 antes do scaffold;
- DTOs separados de entidades EF;
- policies específicas para agenda, paciente, Premium, financeiro e metadados clínicos;
- ledger financeiro consistente e eventos de auditoria append-only.

## Segurança e testes negativos

- Gestor não lê/escreve laudo ou anexo clínico reservado;
- Gestor não aprova a própria conta, eleva papel ou altera MFA administrativo;
- confirmação financeira duplicada/forjada é recusada;
- IDs de paciente, médico, agendamento, pagamento e documento são revalidados;
- filtros não permitem enumeração irrestrita;
- endpoints críticos aplicam antiforgery, rate limit, idempotência e concorrência;
- logs não incluem observação clínica, dados completos de cartão ou documento Premium.

## Responsividade e acessibilidade

- cards de agenda no celular; tabela/card no desktop;
- filtros densos preservam estado e são operáveis por teclado;
- confirmação de pagamento possui labels e máscara sem esconder informação do leitor de tela;
- ações destrutivas exigem confirmação e devolvem foco;
- validar todos os breakpoints, zoom de 200%, contraste e touch targets.

## Cenário ponta a ponta obrigatório

Gestor aprovado entra, acompanha indicadores, pesquisa agenda/pacientes, cria ou edita paciente com segurança, agenda atendimento, liga, cancela/reagenda, confirma pagamento presencial, acompanha histórico/metadados clínicos, analisa Premium quando autorizado e gerencia o próprio perfil.

## Critério de saída

- todas as imagens, páginas, popups e comandos do Gestor estão rastreados;
- nenhuma capacidade operacional catalogada permanece ausente;
- a separação entre operação e conteúdo médico está testada;
- migrations/scaffold/build/testes/segurança aprovados;
- a experiência administrativa completa da Fase 14 não foi antecipada.
