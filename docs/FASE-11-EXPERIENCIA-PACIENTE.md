# Fase 11 — Experiência completa do Paciente

**Acompanhamento:** [Implementação, rastreabilidade e validações pendentes](FASE-11-IMPLEMENTACAO-E-VALIDACAO.md). A implementação e a validação local foram concluídas; homologações externas continuam registradas localmente.

## Objetivo

Entregar todas as capacidades do perfil Paciente observadas no MAUI, redesenhadas para Web responsiva e integradas às fundações das Fases 7, 9 e 10.

**Branch prevista:** `codex/fase-11-experiencia-paciente`.

## Evidências obrigatórias

Imagens autorizadas: `Paciente.png`, `Inicio.png`, `agendar.png`, `agenda.png`, `Pagamento.png`, `perfil.png` e `chamada online.png`, em `ImagensPlayStore/Paciente`.

Fontes permitidas principais:

- `PatientMainPage`, `PatientTabbedPage`, `PatientHomeView`, `PatientScheduleView`, `PatientAgendaView`, `PatientPaymentView`, `PatientWaitPaymentPage` e `PatientProfileView`;
- `OnlinePage` e `PaymentSuccessfulPage`;
- popups de detalhes, cancelamento, reagendamento, avaliação, laudo, anexos e troca de senha;
- view models/serviços/controllers correspondentes em `ViverAppMobileNew` e `ViverAppApi`.

## Navegação e identidade

- cor contextual azul, com contraste e rótulo “Paciente”;
- Início, Agendar, Agenda, Pagamentos e Perfil/Premium;
- atalhos e menu preservam as mesmas capacidades em celular, tablet e desktop;
- a API deriva o paciente da sessão; nenhum `patientId` arbitrário amplia acesso.

## Início

- saudação, nome e badge Premium;
- próximo atendimento em card completo ou estado vazio com CTA para agendar;
- médico, especialidade/serviço, data, duração, modalidade, local, estado clínico, pagamento e reagendamento;
- ações conforme elegibilidade: pagar, cancelar com motivo, reagendar, entrar online, abrir rota e ligar para a clínica;
- atalhos para agenda e novo agendamento;
- catálogo/vitrine de exames e cirurgias disponíveis;
- área promocional configurável, incluindo CTA externo equivalente quando aprovado, sempre com URL allowlisted e indicação de destino;
- apresentação de benefícios e acesso ao Premium;
- carrossel acessível, controlável e sem autoplay obrigatório.

## Agendar

1. escolher Consulta, Exame ou Cirurgia;
2. pesquisar/selecionar serviço com descrição, preço e duração;
3. exibir desconto Premium calculado pelo servidor;
4. escolher presencial ou online somente quando permitido;
5. selecionar médico elegível, exibindo especialidade, avaliação, experiência e disponibilidade online;
6. selecionar data e slot calculado pela API;
7. informar observações;
8. revisar resumo completo;
9. confirmar com chave idempotente.

O servidor recalculará duração, intervalo, disponibilidade da clínica/médico, feriados, limites diários, conflitos, horizonte, modalidade, preço e desconto. Slot expirado ou conflito deverá permitir nova escolha sem perder os demais dados válidos.

## Agenda futura e histórico

- abas Próximos e Histórico;
- pesquisa e filtros por período, estado, tipo e modalidade;
- paginação e limpar filtros;
- cards responsivos como visualização canônica;
- detalhe com médico, serviço/tipo, estado, pagamento, data, duração, modalidade, endereço/telefone, preço, observação e indicação de reagendamento;
- pagar atendimento pendente;
- cancelar com confirmação e motivo;
- reagendar escolhendo nova data e horário válidos;
- abrir rota presencial ou teleconsulta;
- ligar para a clínica por gesto explícito;
- no histórico, abrir laudo publicado, baixar anexos autorizados e avaliar atendimento;
- avaliação somente após conclusão, uma vez por política, com uma a cinco estrelas e comentário opcional.

## Pagamentos

- separar “Pagar” de “Histórico”;
- listar atendimentos pendentes elegíveis;
- permitir pagamento pelo aplicativo ou na clínica quando a regra permitir;
- impedir pagamento na clínica para atendimento online quando incompatível;
- usar exclusivamente o PagBank Checkout da Fase 9 para cobrança online;
- nunca aceitar preço, desconto, status ou método como autoridade do browser;
- tela de espera com expiração, atualização moderada e consulta ao estado real;
- retorno de sucesso/erro não confirma pagamento sozinho;
- histórico paginado com período, valor mínimo/máximo, método, local e estado;
- detalhe/recibo sem token, payload ou segredo do gateway;
- Google Pay legado não será portado como integração separada.

## Perfil e segurança

- editar nome, contatos, CPF, nascimento e endereço completo conforme campos permitidos;
- CEP assistido com fallback manual;
- contato alterado precisa ser confirmado antes de substituir o principal;
- preferências de e-mail e SMS, sem Firebase/push;
- alterar senha confirmando a atual quando aplicável;
- vincular/desvincular Google com reautenticação;
- gerenciar passkeys e sessões/dispositivos;
- encerrar outras sessões;
- mensagens de segurança sem revelar dados internos.

## Premium

- explicar prioridade, ausência de anúncios internos e desconto configurado;
- enviar comprovante de plano de saúde;
- validar extensão, tamanho, MIME real e conteúdo antes de disponibilizar;
- apresentar progresso e estados aguardando, aprovado, rejeitado e cancelado;
- mostrar motivo/data de rejeição autorizados;
- respeitar cooldown observado de três dias para nova solicitação, até decisão diferente do proprietário;
- cancelar benefício com confirmação e auditoria;
- nunca tornar documento público;
- nesta fase, usar adaptador privado seguro de desenvolvimento; Cloudflare R2 será implantado na Fase 15.

## Laudo, anexos e avaliação

- Paciente vê somente o próprio laudo publicado/retificado;
- versões anteriores permanecem preservadas conforme política clínica, sem edição pelo Paciente;
- downloads revalidam sessão, ownership e vínculo no momento do acesso;
- nomes/chaves de arquivo não serão previsíveis;
- avaliação não altera prontuário nem estado financeiro.

## Videochamada

- entrar somente na sala do próprio atendimento, na janela autorizada;
- vídeo local/remoto, qualidade e estado de conexão;
- ligar/desligar microfone e câmera;
- sair, reconectar e tratar encerramento;
- orientar permissão negada, dispositivo ausente ou rede ruim;
- nenhuma gravação por padrão;
- infraestrutura distribuída/TURN de produção será endurecida na Fase 21.

## Banco e API

- revisar lacunas de Premium, documentos, avaliação e preferências no schema `viverappweb`;
- criar/aplicar todas as migrations no MySQL 8.0.41 antes do scaffold DB-First;
- endpoints paginados e DTOs explícitos para home, agenda/histórico, pagamentos, perfil, Premium e documentos;
- constraints para avaliação única, workflow Premium, documento privado, idempotência e ownership;
- nenhuma escrita ou geração de model a partir de `viverappmobile`.

## Segurança e acessibilidade

- testar IDOR/BOLA trocando agendamento, pagamento, laudo, documento e perfil;
- rate limits próprios para busca de slots, checkout, upload e vídeo;
- proteção de upload contra path traversal, polyglot, MIME falso, malware e abuso de tamanho;
- cards e filtros integralmente navegáveis por teclado;
- estados clínico e financeiro não dependem só de cor;
- dialogs devolvem foco e ações destrutivas exigem confirmação;
- validar 320, 390/412, 768, 1024, 1366 e 1920 px e zoom de 200%.

## Cenário ponta a ponta obrigatório

Paciente cadastra-se, confirma contato, entra, completa perfil, encontra serviço/médico/slot, agenda, paga em sandbox, acompanha o estado, entra em atendimento online ou abre rota presencial, recebe laudo/anexo, avalia e solicita/cancela Premium conforme o workflow.

## Critério de saída

- todos os arquivos/telas/fontes listados possuem item rastreável e teste;
- nenhuma ação do Paciente catalogada permanece parcial ou ausente;
- jornada funciona em celular, tablet e desktop;
- preço, slot, transições e autorização vêm da API;
- migrations/scaffold/build/testes/segurança aprovados;
- fases de Médico, Gestor e Administrador não foram antecipadas.
