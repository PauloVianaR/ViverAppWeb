# Fase 12 — Experiência completa do Médico

## Objetivo

Entregar todas as capacidades do perfil Médico observadas no MAUI, preservando sigilo clínico, autoria, disponibilidade e vínculo real com os pacientes.

**Branch prevista:** `codex/fase-12-experiencia-medico`.

## Evidências obrigatórias

Imagens autorizadas: `Médico.png`, `Início.png`, `Agenda.png`, `Histórico.png`, `Perfil 1.png` e `chamada online.png`, em `ImagensPlayStore/Médico`.

Fontes permitidas principais:

- `DoctorMainPage`, `DoctorHomeView`, `DoctorAgendaView`, `DoctorPatientListView`, `DoctorHistoricView`, `DoctorProfileView` e `DoctorSchedulePage`;
- `OnlinePage`;
- popups de detalhe, conclusão, cancelamento, reagendamento, laudo, anexos, paciente e senha;
- view models, serviços e controllers correspondentes.

## Navegação e identidade

- cor contextual verde e identificação profissional explícita;
- Início, Agenda, Pacientes, Histórico e Perfil;
- Perfil contém Dados, Especialidades/Serviços e Disponibilidade;
- Médico acessa somente atendimentos/pacientes ligados ao próprio escopo.

## Início

- nome, título, CRM, especialidade principal, experiência e avaliação;
- indicadores de atendimentos de hoje, semana, online e presenciais;
- agenda do dia em cards;
- abrir detalhes;
- entrar em chamada quando elegível;
- concluir atendimento com laudo/anexos quando a transição permitir;
- atalhos para Agenda, Histórico e Perfil.

## Agenda

- totais online, presenciais, reagendados e geral no período;
- busca e filtros por tipo, modalidade, estado, data e horário;
- limpar filtros e paginação;
- cards com paciente/idade, serviço, tipo, estado, pagamento, reagendamento, data, duração, local, preço e observações autorizadas;
- detalhe completo do atendimento;
- cancelar com motivo;
- reagendar escolhendo nova data/horário;
- entrar online;
- concluir atendimento;
- impedir conclusão de estado inelegível e resolver concorrência no servidor.

## Pacientes

- indicadores de total, Premium, ativos e bloqueados;
- busca, filtros e paginação;
- lista derivada de vínculos reais, sem consulta irrestrita;
- incluir paciente em nome da clínica com proteção contra duplicidade/tomada de conta;
- editar somente campos operacionais permitidos;
- ligar por gesto explícito;
- iniciar agendamento para o paciente;
- não exibir prontuário global ou atendimentos de outro médico.

## Agendamento para paciente

1. escolher paciente autorizado;
2. escolher Consulta, Exame ou Cirurgia;
3. selecionar serviço oferecido pelo Médico;
4. escolher modalidade compatível;
5. manter o Médico autenticado como profissional fixo;
6. selecionar data/horário respeitando clínica, feriados, disponibilidade, limites e conflitos;
7. registrar observações;
8. revisar preço/desconto Premium calculados no servidor;
9. confirmar de forma idempotente e auditada.

## Histórico, laudo, anexos e feedback

- busca e filtros por modalidade, tipo, estado e período;
- paginação e limpar filtros;
- cards de concluídos, cancelados e faltas;
- visualizar o laudo de atendimento próprio;
- retificar laudo por nova versão auditada, preservando texto, autoria e instante anteriores;
- enviar, listar, baixar e excluir anexos autorizados conforme retenção;
- visualizar nota e comentário do Paciente sem alterá-los;
- conteúdo clínico nunca entra em listagem, log ou evento de auditoria;
- Gestor/Administrador recebem somente metadados, conforme decisão de sigilo da Fase 8.

## Conclusão do atendimento

- conferir paciente, idade, serviço, data, tipo, modalidade e observações;
- exigir laudo quando a regra do atendimento determinar;
- aceitar anexos permitidos com máximo inicial de 10 MB por arquivo, sujeito à validação de produto;
- permitir remover arquivo antes da publicação;
- publicar laudo/anexos e transicionar o atendimento atomicamente;
- impedir conclusão duplicada;
- qualquer alteração posterior gera versão/retificação, nunca sobrescrita silenciosa.

## Perfil profissional

### Dados

- nome, e-mail, telefone, CPF, título, CRM/UF, especialidade principal e experiência;
- contato alterado exige nova confirmação;
- preferências de e-mail/SMS, sem push;
- mudança de senha, Google, passkeys e sessões;
- alterações de credenciamento relevantes podem exigir nova análise administrativa.

### Especialidades e serviços

- pesquisar e selecionar ofertas agrupadas em consultas, exames e cirurgias;
- mostrar contagens e estado ativo;
- impedir oferta incompatível ou inativa;
- mudanças ficam auditadas.

### Disponibilidade

- habilitar/desabilitar atendimento online quando Admin também permitir;
- escolher disponibilidade semanal padrão ou exceções por semana/data;
- definir máximos diários online e presenciais;
- configurar faixas online e presenciais por dia da semana;
- criar/editar/remover exceções por data;
- validar sobreposição, horário da clínica, feriados e consultas existentes;
- concorrência otimista e explicação de conflito.

## Videochamada

- sala vinculada ao atendimento e à janela autorizada;
- vídeo local/remoto e indicador de qualidade/conexão;
- ligar/desligar câmera e microfone;
- sair e reconectar;
- tratar permissão negada, dispositivo ausente e falha de rede;
- impedir entrada de médico não atribuído;
- nenhuma gravação por padrão;
- endurecimento distribuído/TURN permanece na Fase 22.

## Banco e API

- revisar lacunas de perfil profissional, ofertas, limites, disponibilidade, versões de laudo e anexos;
- criar/aplicar todas as migrations em `viverappweb` no MySQL 8.0.41 antes do scaffold;
- contracts explícitos e queries limitadas;
- constraints para autoria, versão de laudo, disponibilidade válida e idempotência;
- sem reutilizar entidades/DTOs do legado.

## Segurança e testes negativos

- Médico A não acessa paciente, atendimento, laudo, anexo ou sala do Médico B;
- alterar IDs, filtros ou rota não amplia escopo;
- conclusão/retificação registra autoria e não aceita mass assignment;
- uploads validam tamanho, extensão, MIME real, assinatura e malware;
- concorrência de conclusão/reagendamento mantém uma transição válida;
- dados clínicos são redigidos em logs e auditoria;
- testar bloqueio, aprovação revogada, sessão expirada e online desabilitado.

## Responsividade e acessibilidade

- cards no celular e tabela/card alternáveis no desktop;
- filtros densos em painel acessível sem esconder filtros ativos;
- editor de laudo com label, contagem/limite e navegação por teclado;
- disponibilidade semanal utilizável em touch, teclado e zoom de 200%;
- videochamada anuncia estados de câmera/microfone/conexão;
- validar todos os breakpoints da Fase 10.

## Cenário ponta a ponta obrigatório

Médico aprovado entra, completa perfil, escolhe serviços, configura disponibilidade, encontra/cria paciente autorizado, agenda atendimento, gerencia cancelamento/reagendamento, entra na chamada, conclui com laudo/anexo, retifica por nova versão e consulta o feedback.

## Critério de saída

- todas as imagens, páginas, popups e comandos médicos estão rastreados;
- agenda, pacientes, histórico, perfil, disponibilidade e vídeo funcionam integralmente;
- sigilo e autoria resistem aos testes cruzados;
- migrations/scaffold/build/testes/segurança aprovados;
- nenhuma funcionalidade de Gestor ou Administrador foi antecipada.

## Estado de implementação

Implementação concluída e validada localmente em `codex/fase-12-experiencia-medico`. O detalhamento das entregas, migrations, testes, validação visual no monitor 3 e homologações externas remanescentes está em [FASE-12-IMPLEMENTACAO-E-VALIDACAO.md](FASE-12-IMPLEMENTACAO-E-VALIDACAO.md).
