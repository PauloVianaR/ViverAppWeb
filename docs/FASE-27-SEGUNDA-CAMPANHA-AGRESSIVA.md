# Fase 27 — Segunda campanha ofensiva aprofundada

**Estado em 02/10/2026:** execução parcial autorizada pelo proprietário nesta conversa. Os resultados estão em [FASE-27-RELATORIO-PENTEST-RODADA-2.md](FASE-27-RELATORIO-PENTEST-RODADA-2.md). Este plano, por si só, não amplia a autorização para produção, integrações reais ou correções da aplicação. A primeira execução e seu achado F27-01 permanecem em FASE-27-RELATORIO-PENTEST.md; a matriz não deve ser reinterpretada como aprovada.

**Objetivo:** substituir a amostragem rasa da primeira rodada por verificação sistemática de rotas, papéis, objetos, estados e concorrência. “Mais agressiva” significa maior **profundidade e cobertura**, com DAST autenticado e pressão controlada, não indisponibilizar o host nem atacar produção.

## Diferenças em relação à primeira rodada

| Lacuna da rodada 1 | Exigência da rodada 2 |
| --- | --- |
| Homologação praticamente vazia | Massa sintética reproduzível para agenda, caixa, prontuário, documentos, Premium, vídeo e pagamentos simulados. |
| Poucas rotas e somente duas operações de escrita testadas | Inventário de 100% das rotas/métodos Web, API e hubs existentes; matriz por papel e por propriedade do objeto, com testes positivos e negativos. |
| Marcadores de entrada em uma busca | Fuzzing limitado e específico por tipo de parâmetro, em campos de todas as famílias de recursos aplicáveis, correlacionado com logs e invariantes do banco. |
| Sem jornadas financeiras, mídia ou concorrência | Fluxos completos e tentativas de quebra de estado, replay e corridas sincronizadas, exclusivamente com fakes e dados sintéticos. |
| Carga de aproximadamente 10 req/s por 20 s | Patamares graduais até 30 req/s e 16 clientes para operações leves, se a telemetria e os portões permanecerem saudáveis; operações caras têm limites menores. |
| Navegador integrado indisponível | Inspeção visual/DevTools continua obrigatória somente nele. Testes HTTP não substituem XSS DOM, UX de erro, cache e comportamento real do cliente. |

## Portão de preparação — obrigatório antes de qualquer requisição ativa

1. Registrar em manifesto local ignorado pelo Git: commit, data/janela, operador e contato de emergência, IP/portas em allowlist, PID dos processos iniciados para o teste, versões das ferramentas, limites por cenário e condições de parada. Abrir nova execução apenas com autorização do proprietário; a solicitação de **planejamento** não é essa autorização.
2. Confirmar leitura de MySQL **8.0.41**, nome **viverappweb_homolog**, migrations correspondentes ao commit de teste e ausência de conexão com viverappweb/viverappmobile. Confirmar que Web/API e fakes escutam apenas em 127.0.0.1. Não criar banco descartável, não executar migrations e não restaurar/resetar banco nesta fase.
3. Fazer inventário inicial e backup verificado por integridade/checksum; preparar plano de recuperação **seletiva** sem ensaio destrutivo. Todas as contas, registros e objetos de teste devem receber marcador único de campanha e inventário de IDs. Preservar eventos append-only.
4. Desativar integrações reais: PagBank, Google, SMTP, SMSBarato, Cloudflare/R2, TURN e demais serviços externos. Quando a função exigir retorno de provedor, usar fake local preso ao loopback e credenciais sintéticas. Não permitir cobrança, estorno, mensagem ou upload real. Se um fake não puder reproduzir uma função sem alterar o produto, marcar o caso bloqueado.
5. Inventariar Docker antes de qualquer uso. Não tocar nos contêineres preexistentes; preferir ferramentas locais. Conferir captura de CPU, RAM, disco, conexões MySQL, pool HTTP, filas, tempos de resposta, health checks e logs antes de elevar carga.
6. Tentar habilitar o navegador integrado do Codex para a inspeção visual. Se o helper continuar falhando, seguir apenas nos testes HTTP/código que não dependem de interface e marcar explicitamente os cenários visuais como bloqueados. Não usar Chrome/Edge externo como substituto.

## Massa sintética mínima e oráculos

- Identidades: Pacientes A/B com dados distintos; Médicos A/B; Psicólogos A/B; Gestor; Administrador com MFA; profissional pendente de aprovação; conta bloqueada; convidado sem conta. Se possível, ampliar para seis pacientes fictícios para testar filtros e isolamento sem volume artificial. Nunca criar segundo Administrador de produto.
- Cadastros e objetos: serviços das quatro categorias, cobrados/gratuitos, ativos/inativos e vinculados a profissionais diferentes; disponibilidade recorrente/variável e exceções; cerca de 40 agendamentos espalhados por estados, datas e modalidades; pagamentos **somente fake** com sucesso, pendência, cancelamento e estorno; movimentos de caixa e fechamento; comprovantes, documentos e prontuários pequenos sem dado clínico real; convites de vídeo com expiração e revogação.
- Para cada fluxo, fotografar o estado inicial e final por consulta somente à homologação: contagem, dono, status, valor, versão, caixa do dia e registros de auditoria. Um status HTTP isolado não confirma segurança; verificar que operação negada não alterou estado, não revelou campo indevido e não deixou cobrança/entrada duplicada.
- Usar pacientes/contatos com nomes e domínios inequivocamente sintéticos. Após a campanha, remover apenas vínculos e massa criada por ela quando isso não violar auditoria/FKs; bloquear contas residuais e revogar sessões. Nenhuma limpeza ampla.

## Ondas de ataque

### 1. Reconhecimento completo e análise estática dirigida

- Exportar OpenAPI, rotas Razor/Blazor, endpoints mínimos, controllers, hubs SignalR, callbacks e jobs; cruzar com CodeGraph e políticas de autorização. Para cada método registrar papel permitido, propriedade do objeto, entrada controlável, efeito persistente, rate limit, antiforgery e auditoria.
- Inspecionar manualmente fronteiras sensíveis: AuthController, MedicalRecordService, agendamento, pagamento/PagBank, caixa, documentos privados, convidado de vídeo e hubs. Executar SAST/SCA/secret scan locais com revisão de falso positivo, sem abrir ou registrar valores secretos. Conferir dependências diretas e transitivas contra avisos atuais.
- Produzir uma tabela rota × método × papel × objeto antes dos testes dinâmicos. Toda rota descoberta entra na matriz; a rota bloqueada/sem massa recebe justificativa, nunca aprovação tácita.

### 2. Identidade e autorização stateful

- Reexecutar anônimo, Paciente A/B, Médico A/B, Psicólogo A/B, Gestor, Administrador antes/depois do MFA, pendente, bloqueado e sessão revogada. Para cada família de recurso, trocar identificadores A/B, papel e propriedade escondida na UI; cobrir GET e escrita. Verificar corpo, status, banco e auditoria.
- Exercitar enumeração, confirmação e recuperação por e-mail/SMS fake; expiração, uso único, replay e concorrência de códigos; troca de senha, sessões simultâneas, revogação, fixation, inatividade e step-up de Admin. Testar aprovação profissional e tentativa de autoatribuição de papel por campos extras, duplicados e alterações de DTO.
- Para Google OAuth, usar IdP fake local somente se a configuração atual permitir reproduzir state, nonce, callback e vínculo sem serviço externo nem mudança na aplicação. Caso contrário manter bloqueado, sem simular proteção por inspeção estática apenas.
- Testar CSRF em **cada classe de escrita** com token ausente, inválido, da sessão anterior e de outro usuário; CORS com origem não autorizada e preflight com credenciais; WebSocket cross-site quando o hub estiver ativo. Observar se a ação persistiu indevidamente.

### 3. Entradas, cliente e arquivos

- Executar DAST autenticado de baixo impacto por contexto de papel, com escopo de hosts estritamente local. Importar rotas da API e usar descoberta/crawler **sem interface gráfica externa**; operações mutáveis exigem massa sintética e lista explícita de IDs. Não aceitar alerta de ferramenta como achado sem reprodução manual e estado persistido.
- Construir corpus pequeno por tipo de campo: texto, ID, data/fuso, número/valor, enum, cabeçalho, URL, JSON, arquivo e paginação. Em cada família aplicável testar SQL injection, XSS refletido/armazenado/DOM, mass assignment, validação de tipo, overflow, Unicode, CRLF e template/command injection apenas se houver interpretador correspondente. Não extrair dados, executar comandos ou usar payload de exfiltração. LDAP/NoSQL/GraphQL/XML são não aplicáveis se o inventário confirmar que não existem.
- Inspecionar no navegador integrado XSS com marcador benigno, CSP, clickjacking, cookies, cache, redirecionamento, erros e fluxo real em desktop/tablet/celular. Reabrir a classificação de prompt injection somente se um recurso do **produto** consumir LLM; o testador usar IA não cria essa superfície.
- Para upload/download usar PDFs e imagens pequenos sintéticos: extensão/MIME/conteúdo divergentes, nome e caminho hostis, tamanho/quantidade limite, URL expirada, vínculo errado, usuário revogado, PDF e comprovante Premium. Testar SSRF somente contra receptor fake em loopback explicitamente permitido; nunca consultar metadata, rede interna ou terceiros. Conferir que documento privado não aparece no bucket público/cache compartilhado.

### 4. Regras clínicas, agenda e finanças

- Agenda: dupla reserva simultânea, vaga já consumida, troca de profissional/serviço incompatível, dia indisponível, preço vindo do cliente, desconto Premium forjado, agendamento gratuito, estados impossíveis, reagendamento repetido, chegada/falta/finalização fora de ordem e cancelamento após pagamento. Usar duas a oito operações sincronizadas por caso e reconciliar quantidade de registros/histórico.
- Prontuário: acesso cruzado, escrita por papel/permissão, rascunho/finalização/retificação, anexos e PDF por escopo; comparar autor/auditoria e conteúdo entregue. F27-01 deve continuar como achado aberto, não ser contornado por bypass de auditoria. A matriz Psicólogo A/B × Paciente A/B só pode ser aprovada após correção em outra fase e reteste.
- Pagamento: fake PagBank com checkout duplicado, referência trocada, assinatura inválida, evento fora de ordem/repetido, replay, confirmação tardia, estorno duplo/parcial e divergência de valor. Invariantes: uma obrigação ativa, lançamentos de caixa corretos no dia efetivo, nenhum movimento em caixa fechado, nenhuma confirmação por resposta controlada pelo cliente.
- Caixa e administração: escrita como papel errado, fechamento/reabertura, filtros que revelem dados de outro escopo, totais/relatórios, CSV/PDF e injeção de fórmula em exportação se houver CSV. Validar idempotência e trilha de auditoria, não apenas layout.

### 5. Vídeo, SignalR, jobs e notificações

- Reunião com host, paciente autenticado e convidados sintéticos até quatro participantes; quinto participante deve falhar. Tentar link expirado/revogado/reutilizado, sala de outro atendimento, host ausente, reconexão, troca de dispositivos e eventos SignalR invocados por papel incorreto. Captura visual e mídia real só no navegador integrado; testes de protocolo podem ser locais sem inspeção visual.
- Jobs, notificações e e-mail/SMS via fakes: repetição após falha antes/depois do ACK, concorrência entre dois workers isolados, destinatário alterado, supressão/preferência, dados sensíveis no corpo/log e fila sem crescimento ilimitado. Não iniciar os workers externos legados nem contêineres de outros trabalhos.

### 6. Pressão controlada de camada 7

- Primeiro obter baseline de 60 s por recurso em repouso. Em endpoints leves e sintéticos, subir em degraus: 1 cliente/5 req/s, 4/10, 8/20 e **no máximo 16 clientes/30 req/s**. Cada degrau dura até 60 s; somente se estável, manter o último por até 120 s. Não combinar cenários nem passar de aproximadamente seis minutos por alvo.
- Em PDF, upload, vídeo, busca cara, login/código ou jobs, teto separado de **2 req/s, 4 clientes e 60 s**, sem geração volumosa de arquivo. Usar gerador com backpressure, taxa global e cancelamento imediato; nunca slowloris, DDoS, amplificação, exaustão deliberada de disco ou tentativa de derrubar a máquina.
- Medir p50/p95/p99, throughput, 429/5xx, CPU, memória livre, conexões MySQL, fila e saúde da API durante o ensaio e por cinco minutos depois. Rodar simultaneamente uma sessão legítima de baixa taxa para observar se o limitador prejudica o usuário autenticado.
- Parada imediata se health/ready falhar, qualquer tráfego escapar da allowlist, aparecer integração real, CPU ficar acima de 85% por 30 s, memória livre cair abaixo de 20%, disco livre abaixo de 20%, conexões MySQL acima de 70% do limite, 5xx acima de 1% por 15 s, ou fila crescer sem drenar. Estes são tetos de segurança, não metas a atingir. Interromper antes se o operador notar degradação.

## Portões, evidências e saída

- Rastreabilidade quantitativa: 100% das rotas e métodos descobertos classificados; cada rota sensível com teste por papel permitido/proibido, objeto próprio/alheio, token presente/ausente quando mutável e efeito no banco. Para classe não aplicável, demonstrar ausência do parser/protocolo; para bloqueada, indicar dependência e procedimento de retomada.
- Um achado só é confirmado com reprodução mínima, resposta observada, identidade/pré-condição, evidência de banco/log sanitizada, impacto e hipótese de causa. Classificar vulnerabilidade explorável separadamente de defeito funcional (como F27-01). Priorizar dados clínicos, autorização, pagamento e indisponibilidade.
- Guardar tráfego bruto, IDs, logs e métricas somente em .local/, sem segredos ou dados sensíveis no Git. Atualizar a matriz existente com segunda execução identificada, sem apagar a primeira. Produzir FASE-27-RELATORIO-PENTEST-RODADA-2.md com achados, falsos positivos, lacunas e critérios objetivos de reteste.
- Não corrigir código, schema, configurações de produção nem controles durante a Fase 27. Propor reparos para fases/branches posteriores. Achado crítico/alto aberto ou cenário sensível bloqueado impede recomendação de lançamento; uma revisão independente humana permanece necessária.
- Quando o Windows Server/IIS/origem real estiver pronto, TLS/HSTS, proxy/request smuggling, Host Header, Cloudflare/origin bypass e permissões do host exigirão **novo manifesto e autorização específica**. Nenhum resultado da homologação local cobre essa futura infraestrutura.

## Referências verificadas para este plano

- [OWASP Web Security Testing Guide v4.2](https://wstg.owasp.org/v4.2/) — versão estável publicada; o projeto desenvolve a 5.0.
- [OWASP ASVS 5.0.0](https://owasp.org/projects/asvs/) — requisitos verificáveis de segurança.
- [OWASP Top 10:2025](https://top10.owasp.org/2025/en/) e [OWASP API Security Top 10:2023](https://api-security.owasp.org/editions/2023/en/0x00-header/) — taxonomia de riscos, não substituto da matriz de testes.
