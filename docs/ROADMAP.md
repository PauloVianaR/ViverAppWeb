# Roteiro de reimplementação do ViverApp

## Visão do produto

Reescrever o ecossistema atual como uma aplicação web segura, responsiva e operável, mantendo o legado apenas como fonte de requisitos. O destino é uma solution .NET 10 com Blazor, ASP.NET Core API, MySQL 8.0.41, processamento assíncrono durável, SignalR/WebRTC, PagBank e Cloudflare R2/CDN.

O roteiro evita um “big bang”: cada fase deve entregar um incremento verificável em branch própria, com build, testes, migrations aplicadas e critérios de saída satisfeitos.

## Decisões iniciais

1. **Arquitetura:** monólito modular. `ViverApp.Web` e `ViverApp.Api` são processos separados; regras de negócio serão agrupadas por módulos e não por camadas genéricas globais.
2. **Workers:** e-mail, SMS via SMSBarato e rotinas agendadas entram inicialmente na API como hosted services sobre uma fila/outbox persistida no MySQL. Firebase e push não fazem parte do novo produto. O módulo será extraível se volume, disponibilidade ou escalabilidade justificarem outro processo.
3. **Vídeo:** o hub de sinalização SignalR entra na API. A mídia WebRTC continua ponto a ponto quando adequado, com STUN/TURN e autorização de salas. Estado de presença não dependerá de dicionário estático em memória.
4. **Autenticação web:** padrão BFF/cookie seguro para o navegador. Credenciais e tokens sensíveis permanecem no servidor; não serão armazenados no storage do browser.
5. **Persistência:** Entity Framework Core DB-First para a nova aplicação, exclusivamente em MySQL 8.0.41. `viverappmobile` é uma referência estritamente somente leitura; `viverappweb` será o banco novo e a única fonte dos models EF e contratos web. O schema novo evoluirá por migrations SQL obrigatórias, executadas antes de cada novo scaffold.
6. **Armazenamento:** migrar anexos do mecanismo legado compatível com S3/B2 para Cloudflare R2, servidos por domínio próprio/CDN com URLs assinadas quando o conteúdo não for público.
7. **Segurança:** controles serão parte de cada fase, não uma revisão tardia. A Fase 3 estabelece a base transversal antes das funcionalidades sensíveis.

## Regras de passagem entre fases

Cada fase deve:

- começar em `codex/fase-NN-descricao` fora de `main`/`master`;
- executar apenas uma fase por branch e parar sem antecipar a seguinte;
- registrar critérios de aceite e decisões arquiteturais relevantes;
- consultar CodeGraph primeiro quando `.codegraph/` existir;
- compilar sem warnings e executar todos os testes afetados;
- criar, revisar e **aplicar todas** as migrations pendentes no MySQL 8.0.41;
- não incluir segredo nem mudança em qualquer projeto legado;
- atualizar este roteiro quando uma descoberta alterar dependências ou escopo.

## Fase 0 — Fundação da solution

**Objetivo:** criar a base vazia e as regras de trabalho.

**Escopo:**

- solution `ViverApp.slnx` em .NET 10;
- Blazor Web App com interatividade Server;
- ASP.NET Core Web API sem endpoints de negócio;
- SDK fixado, warnings como erros, ignores locais, documentação e `AGENTS.md`;
- `UserSecretsId` na API;
- transferência segura apenas de `LocalConnection` e da seção PagBank; exclusão das strings Azure.

**Saída:** restore e build bem-sucedidos; nenhum pacote ou feature de negócio; nenhum segredo no repositório.

## Fase 1 — Descoberta funcional, riscos e arquitetura executável

**Estado:** concluída e incorporada à `main` do repositório Web.

**Objetivo:** transformar o comportamento espalhado pelo MAUI/API/workers/hub em requisitos confiáveis antes de portar código.

**Entregas:**

- mapa de jornadas por perfil: paciente, médico, gestor e administrador;
- inventário de endpoints, tabelas, telas, regras, jobs, integrações e arquivos;
- matriz “legado → módulo/caso de uso novo”, marcando manter, corrigir ou remover;
- glossário do domínio e estados válidos de agendamento, pagamento e usuário premium;
- classificação LGPD dos dados, retenção, consentimento e bases legais a validar;
- threat model com fronteiras de confiança e riscos STRIDE/OWASP;
- ADRs para autenticação, módulos, fila/outbox, vídeo, armazenamento e implantação;
- estratégia de testes e ambientes;
- levantamento específico da experiência de login Google existente, inclusive configurações externas que não estejam visíveis no código atual.

**Saída:** backlog priorizado, diagramas e decisões aprovadas; nenhuma portabilidade cega de código legado.

**Artefatos:** [índice da Fase 1](fase-01/README.md) e [ADRs](adr/README.md).

## Fase 2 — Banco DB-First, modelo de domínio e baseline de dados

**Estado:** concluída em `codex/fase-02-banco-db-first`; integração à `main` autorizada pelo proprietário.

**Objetivo:** criar persistência segura sem perder nem corromper o banco local existente.

**Entregas:**

- inventário somente leitura e backup restaurável de referência do schema `viverappmobile`, sem qualquer alteração nesse banco;
- criação do banco novo `viverappweb` na conexão local, validando MySQL 8.0.41 e o nome do alvo antes de executar DDL;
- desenho da nova estrutura sem obrigação de reproduzir tabelas, nomes ou problemas do legado;
- módulos/entidades iniciais, convenções de nomes, UTC, precisão monetária e concorrência otimista;
- runner de migrations SQL versionadas, com histórico no próprio `viverappweb`;
- primeira migration/baseline SQL aplicada exclusivamente em `viverappweb`;
- scaffold DB-First reproduzível do `DbContext` e das entidades a partir de `viverappweb`, usando provedor compatível com MySQL 8.0.41 e versão explícita;
- models e contratos novos baseados exclusivamente no novo schema, sem reutilizar classes de `ViverApp.Shared`;
- seeds apenas para dados de referência, idempotentes e versionados;
- testes de integração contra MySQL 8.0.41 real, nunca SQLite como substituto;
- plano de reconciliação e migração de dados legados.

**Saída:** migrations SQL listadas e aplicadas em `viverappweb`, scaffold DB-First reproduzido, schema e scripts de rollback validados, `viverappmobile` comprovadamente inalterado e zero uso de Azure/SQL Server. Ensaio destrutivo de rollback não é exigido.

## Fase 3 — Fundação de segurança, privacidade e observabilidade

**Estado:** concluída e integrada à `main`.

**Objetivo:** estabelecer controles transversais antes de expor funcionalidades.

**Entregas:**

- HTTPS/HSTS, cookies `Secure`/`HttpOnly`/`SameSite`, antiforgery e CORS por allowlist;
- CSP com nonces/hashes, Permissions-Policy, proteção de framing, MIME sniffing e referrer policy;
- rate limiting por endpoint/identidade/IP, limites de payload e timeout/cancelamento;
- validação central, Problem Details, encoding contextual e proteção a XSS, SQL injection, SSRF, path traversal e mass assignment;
- honeypot em formulários públicos como sinal complementar, sem substituir rate limiting/CAPTCHA adaptativo;
- bloqueio progressivo, detecção de abuso e respostas sem enumeração de contas;
- auditoria imutável para ações críticas e logs estruturados com correlação e redação de PII/segredos;
- health/readiness checks sem revelar detalhes; métricas, tracing e alertas;
- varredura de dependências, SAST, secret scanning e baseline de testes OWASP;
- política de backup, retenção, restauração e resposta a incidentes.

**Saída:** checklist de segurança automatizado e threat model atualizado.

**Artefatos:** [índice da Fase 3](fase-03/README.md).

## Fase 4 — Identidade, login Google/e-mail/SMS e autorização

**Estado:** concluída e integrada à `main`.

**Objetivo:** substituir a autenticação legada por identidade moderna e políticas por perfil.

**Entregas:**

- ASP.NET Core Identity adaptado ao MySQL, sessões revogáveis e confirmação de contato;
- senhas com hash adaptativo, salgado e versionado (preferência inicial: Argon2id após validação da biblioteca; fallback documentado para o hasher robusto do Identity);
- remoção completa de criptografia reversível para novas credenciais;
- transição auditada sem transportar AES/ECB: contas legadas futuramente elegíveis entram sem a credencial reversível e executam redefinição forçada por contato confirmado; a chave AES não entra no runtime Web;
- login local por e-mail ou telefone, com senha armazenada somente como hash, além do login Google;
- confirmação e recuperação por código curto, de uso único e armazenado como hash, enviado por e-mail ou SMSBarato; nunca enviar senha temporária em texto claro;
- Google OpenID Connect/OAuth 2.0 com `state`, `nonce`, PKCE quando aplicável, e-mail verificado e vínculo explícito de conta para impedir account takeover;
- exatamente um papel por conta entre Paciente, Médico, Gestor e Administrador, com policies e checagem de ownership na API;
- passkeys/WebAuthn como login primário moderno e MFA obrigatório para administrador por TOTP ou recovery code de uso único, pois passkeys nativas do ASP.NET Core 10 não operam como segundo fator;
- gestão de dispositivos/sessões, rotação, revogação e eventos de segurança;
- testes para brute force, enumeração, CSRF, fixation, redirect indevido, privilege escalation e vínculo Google.

**Saída:** matriz de autorização coberta por testes e nenhuma senha reversível restante no novo sistema.

## Fase 5 — Cadastros e configuração clínica

**Estado:** concluída e integrada à `main`.

**Objetivo:** reconstruir os dados mestres usados pelas jornadas.

**Entregas:**

- concluir a configuração externa do cliente OAuth Web do Google e validar o handshake real sem versionar credenciais;
- usuários e perfis profissionais;
- clínica, especialidades, tipos de atendimento e feriados;
- disponibilidade da clínica e de médicos;
- aprovação/status de profissionais e regras administrativas;
- APIs versionadas, paginação, filtros, validação e auditoria;
- primeiras telas responsivas de manutenção conforme o design system da Fase 6.

**Saída:** CRUDs autorizados por política, sem exposição direta de entidades EF.

**Artefatos:** [índice da Fase 5](fase-05/README.md) e [matriz de acesso da API](fase-05/API-E-AUTORIZACAO.md).

## Fase 6 — Design system e shell web responsivo

**Estado:** concluída e integrada à `main`.

**Objetivo:** redesenhar a experiência MAUI para web, preservando a identidade útil sem copiar limitações mobile.

**Entregas:**

- inventário visual do XAML, marca, cores, tipografia e iconografia;
- tokens, componentes reutilizáveis e documentação de estados;
- navegação adaptativa: celular, tablet, notebook, desktop largo e zoom de 200%;
- shells específicos por perfil com menus e densidade adequados, sem confiar na UI para autorização;
- layouts para tabelas/filtros no desktop e cartões/ações no mobile;
- loading, skeleton, vazio, erro, offline/degradação e sessão expirada;
- WCAG 2.2 AA, teclado, leitor de tela, foco, contraste e redução de movimento;
- testes visuais e de acessibilidade nos breakpoints acordados.

**Saída:** biblioteca visual aprovada e páginas-base prontas para receber os fluxos.

**Artefatos:** [índice da Fase 6](fase-06/README.md), [design system](fase-06/DESIGN-SYSTEM.md) e [acessibilidade e responsividade](fase-06/ACESSIBILIDADE-E-RESPONSIVIDADE.md).

## Fase 7 — Agenda e agendamento do paciente

**Estado:** concluída e integrada à `main` em `54f43b1`.

**Objetivo:** entregar a jornada principal de descoberta e marcação.

**Entregas:**

- busca/filtro de profissionais, especialidades, modalidade e disponibilidade;
- criação de agendamento com transação, idempotência e proteção contra dupla reserva;
- timezone explícito, conflitos de agenda, feriados, limites e validações de negócio;
- reagendamento e cancelamento conforme políticas;
- agenda futura e detalhes para paciente;
- testes de concorrência e estados extremos.

**Saída:** jornada responsiva ligada à API, com horários calculados no servidor e alterações protegidas contra repetição e disputa concorrente.

**Artefatos:** [índice da Fase 7](fase-07/README.md), [API e regras](fase-07/API-E-REGRAS.md) e [concorrência e idempotência](fase-07/CONCORRENCIA-E-IDEMPOTENCIA.md).

## Fase 8 — Jornadas de médico e gestor

**Estado:** concluída e integrada à `main` no commit `09382d3`.

**Objetivo:** entregar operação clínica e gestão de pacientes.

**Entregas:**

- agenda, histórico, filtros e detalhes;
- gestão de disponibilidade e modalidades;
- lista autorizada de pacientes vinculados;
- conclusão de consulta, relatório médico e regras de visibilidade;
- controles de acesso por papel, ownership e vínculo profissional/paciente, sem tenancy ou multiclínica, e trilha de auditoria;
- adaptação de fluxos densos para desktop/tablet sem prejudicar mobile.

**Saída:** médico e gestor só acessam dados permitidos e toda alteração crítica é auditada.

**Artefatos:** [índice da Fase 8](fase-08/README.md), [API e autorização](fase-08/API-E-AUTORIZACAO.md) e [sigilo e ciclo clínico](fase-08/SIGILO-E-CICLO-CLINICO.md).

## Fase 9 — PagBank Checkout em produção

**Estado:** concluída e integrada à `main` no commit `701dae4`. A ativação externa permanece deliberadamente pendente de ambiente público e autorização explícita.

**Objetivo:** reimplementar cobrança sem confiar no navegador nem no comportamento frágil legado.

**Entregas:**

- validação da versão atual da API e documentação oficial do PagBank no início da fase;
- cliente HTTP resiliente com configuração tipada e token de produção somente no backend;
- criação de checkout a partir de valores calculados no servidor;
- idempotência ponta a ponta para criação, retorno e webhook;
- validação de autenticidade/assinatura do webhook segundo a especificação oficial vigente;
- máquina de estados de pagamento, proteção contra replay, processamento transacional e reconciliação periódica;
- páginas de retorno/sucesso/erro que consultam o estado real no servidor;
- política de reembolso/cancelamento e trilha financeira auditável;
- testes sandbox, testes de contrato e roteiro controlado para produção sem cobrança acidental.

**Saída:** cenários duplicados, atrasados, forjados e fora de ordem cobertos; ativação de produção exige autorização explícita.

**Artefatos:** [índice da Fase 9](fase-09/README.md), [integração e estados](fase-09/INTEGRACAO-E-ESTADOS.md), [segurança e idempotência](fase-09/SEGURANCA-E-IDEMPOTENCIA.md) e [runbook de produção](fase-09/RUNBOOK-PRODUCAO.md).

## Fase 10 — Acesso e fundação compartilhada

**Estado:** concluída e integrada à `main`.

**Plano detalhado:** [Acesso e fundação compartilhada](FASE-10-ACESSO-E-FUNDACAO-COMPARTILHADA.md).

## Fase 11 — Experiência completa do Paciente

**Estado:** concluída e integrada à `main` no commit de merge `2d48185`; homologações externas permanecem pendentes. Evidências e limitações em [Implementação e validação](FASE-11-IMPLEMENTACAO-E-VALIDACAO.md).

**Plano detalhado:** [Experiência completa do Paciente](FASE-11-EXPERIENCIA-PACIENTE.md).

## Fase 12 — Experiência completa do Médico

**Estado:** concluída e integrada à `main` no commit de merge `b1e7fdf`; homologações externas permanecem registradas localmente. Evidências e limitações em [Implementação e validação](FASE-12-IMPLEMENTACAO-E-VALIDACAO.md).

**Plano detalhado:** [Experiência completa do Médico](FASE-12-EXPERIENCIA-MEDICO.md).

## Fase 13 — Experiência completa do Gestor

**Estado:** concluída e integrada à `main` no commit de merge `d6469d9`; homologações externas permanecem registradas localmente. Evidências e limitações em [Implementação e validação](FASE-13-IMPLEMENTACAO-E-VALIDACAO.md).

**Plano detalhado:** [Experiência completa do Gestor](FASE-13-EXPERIENCIA-GESTOR.md).

## Fase 14 — Experiência completa do Administrador e fechamento da paridade

**Estado:** implementação concluída, validada e integrada à `main` em 8 de setembro de 2026. Evidências em [Implementação e validação](FASE-14-IMPLEMENTACAO-E-VALIDACAO.md) e [Matriz final de paridade](FASE-14-MATRIZ-PARIDADE.md).

**Plano detalhado:** [Experiência completa do Administrador](FASE-14-EXPERIENCIA-ADMINISTRADOR.md).

## Fase 15 — Anexos, Cloudflare R2, domínio e CDN

**Objetivo:** substituir o armazenamento transitório usado pela paridade funcional por Cloudflare R2, migrar documentos/mídia legados e entregar objetos com segurança e eficiência.

**Estado:** implementação concluída e validada em 8 de setembro de 2026 na branch `codex/fase-15-cloudflare-r2-dominio-cdn`. Decisões, inventário, evidências e runbook em [Implementação e operação do armazenamento](FASE-15-IMPLEMENTACAO-E-OPERACAO.md).

**Entregas:**

- inventário e checksum dos objetos no storage legado;
- buckets/ambientes separados no Cloudflare R2, menor privilégio e rotação de credenciais;
- uploads validados por tamanho, extensão, MIME real e antivírus/quarentena;
- chaves de objeto não previsíveis, metadados mínimos, criptografia e política de retenção;
- URLs assinadas e curtas para conteúdo privado; nada médico deve se tornar público por CDN;
- domínio próprio, DNS, TLS, regras de cache, invalidação e proteção de hotlink quando aplicável;
- migração em lotes com retry, relatório de divergências, dual-read temporário e rollback;
- testes de autorização, cache e indisponibilidade.

**Saída:** 100% dos objetos reconciliados e acesso privado comprovado antes de desligar o storage antigo.

## Fase 16 — Operação clínica, caixa e prontuário eletrônico

**Estado:** planejada; será a próxima fase após a integração formal da Fase 15.

**Plano detalhado:** [Operação clínica, caixa e prontuário eletrônico](FASE-16-OPERACAO-CLINICA-CAIXA-E-PRONTUARIO.md).

## Fase 17 — E-mail, SMS e jobs dentro da API

**Objetivo:** ampliar os envios já visíveis na experiência Web e substituir definitivamente os workers separados por processamento durável e observável.

**Entregas:**

- transactional outbox no MySQL para e-mail, SMS e eventos internos;
- hosted services modulares com claim atômico, idempotência, backoff com jitter, dead-letter e reprocessamento administrativo;
- coordenação segura para múltiplas instâncias da API, sem duplicidade por memória local;
- templates versionados e seguros, preferências/consentimento e supressão;
- provedores de e-mail e SMSBarato encapsulados e substituíveis;
- scheduler persistido para lembretes e manutenção;
- métricas de fila, latência, falha e alertas; payloads sensíveis redigidos;
- testes de crash entre envio e confirmação, concorrência e indisponibilidade do provedor.

**Saída:** workers legados deixam de ser necessários somente após execução paralela controlada e reconciliação.

## Fase 18 — Videochamada segura

**Objetivo:** levar as jornadas WebRTC funcionais das Fases 11 e 12 a uma sinalização distribuída, resiliente e pronta para produção.

**Entregas:**

- SignalR autenticado dentro da API e autorização por agendamento/sala;
- identificadores imprevisíveis e credenciais de sala temporárias;
- mensagens de sinalização com schema/limites, rate limiting e rejeição de payloads inválidos;
- presença distribuída com expiração, sem `static Dictionary` como fonte de verdade;
- STUN/TURN com credenciais efêmeras, HTTPS/WSS e allowlist de origem;
- consentimento/permissões de câmera e microfone, estados de reconexão e teste de rede;
- nenhuma gravação por padrão; qualquer gravação futura exige fase própria de privacidade;
- testes com dois participantes, queda/reentrada, múltiplas instâncias e tentativa de acesso indevido.

**Saída:** somente participantes autorizados sinalizam na sala e a solução escala além de uma instância.

## Fase 19 — Pagamentos internos, premium e documentos

**Objetivo:** consolidar, reconciliar e preparar para produção o histórico financeiro, o fluxo Premium e seus documentos implementados funcionalmente nas Fases 11 a 14.

**Entregas:**

- histórico paginado, filtros e precisão monetária;
- vínculo consistente entre checkout, pagamento e agendamento;
- solicitação/análise de premium e documentos de plano de saúde;
- autorização por ownership/papel e auditoria administrativa;
- regras de retenção e acesso aos documentos;
- reconciliação com registros legados.

**Saída:** invariantes financeiras e de premium cobertas por testes e relatórios de reconciliação.

## Fase 20 — Administração, analytics e operação

**Objetivo:** endurecer o backoffice e os analytics funcionais da Fase 14 com controles elevados e recursos de operação de produção.

**Entregas:**

- gestão de usuários, da clínica única, agendamentos, premium e notificações;
- MFA obrigatório, step-up authentication para ações críticas e sessões administrativas curtas;
- segregação de funções e dupla confirmação para ações de alto impacto;
- dashboards calculados no servidor com consultas limitadas e dados minimizados;
- exportações assíncronas, auditadas, expiradas e protegidas;
- console de filas/reprocessamento sem revelar segredos ou payloads médicos.

**Saída:** trilha auditável e testes de elevação horizontal/vertical de privilégio.

## Fase 21 — Robustez, desempenho e segurança ofensiva

**Objetivo:** preparar o conjunto funcional para tráfego e ataques reais.

**Entregas:**

- testes de carga para login, agenda, checkout, SignalR e workers;
- otimização de queries/índices medida por evidência;
- testes de caos de provedores e retomada de jobs;
- DAST convencional, revisão OWASP ASVS, revisão humana/independente e correção dos achados antes do pentest Strix;
- SBOM, dependências fixadas, assinatura/proveniência dos artefatos e pipeline de atualização;
- revisão LGPD, acessibilidade e compatibilidade de navegadores;
- SLOs, alertas acionáveis e runbooks.

**Saída:** nenhum achado crítico/alto aberto e metas de desempenho/SLO atendidas.

## Fase 22 — SEO, privacidade, cookies e conformidade Web

**Estado:** planejada para depois do hardening funcional e antes da publicação definitiva da infraestrutura.

**Plano detalhado:** [SEO, privacidade, cookies e conformidade Web](FASE-22-SEO-PRIVACIDADE-E-CONFORMIDADE-WEB.md).

## Fase 23 — Infraestrutura, Cloudflare e CI/CD

**Objetivo:** publicar sem Azure, com entrega repetível e origem protegida.

**Entregas:**

- ambientes separados, infraestrutura documentada e configuração por segredo;
- domínio próprio no Cloudflare, DNSSEC, TLS estrito, CDN, WAF, bot management/rate rules conforme plano contratado;
- origem acessível apenas pelos caminhos necessários e headers/IPs confiáveis validados corretamente;
- deploy imutável com health checks, rollback e migrations coordenadas;
- banco MySQL 8.0.41 com backup criptografado, restore testado e plano de recuperação;
- CI com restore/build/test/format, migration script revisado, SAST/SCA/secret scan e artefatos versionados;
- monitoramento de certificado, domínio, disponibilidade, filas e pagamentos.

**Saída:** ensaio de deploy/rollback/restore aprovado. Compra e alterações externas exigem autorização do usuário.

## Fase 24 — Pentest autorizado com Strix

**Estado:** planejada para a release candidate, depois do staging endurecido e antes da migração final.

**Plano detalhado:** [Pentest autorizado com Strix](FASE-24-PENTEST-COM-STRIX.md).

## Fase 25 — Migração final e lançamento gradual

**Objetivo:** migrar dados e usuários com risco controlado.

**Entregas:**

- ensaio completo com cópia anonimizada/sintética e medição de duração;
- validações por contagem, checksum e invariantes de negócio;
- conversão final de senhas para hash ou fluxo de redefinição seguro;
- migração/reconciliação de arquivos, agendamentos, pagamentos e filas;
- janela de corte, modo manutenção quando necessário e plano de rollback;
- canário/feature flags, monitoramento intensivo e suporte;
- comunicação e procedimentos de incidentes.

**Saída:** reconciliação assinada, métricas saudáveis e rollback ainda possível.

## Fase 26 — Desativação controlada do legado

**Objetivo:** encerrar componentes antigos somente depois da estabilidade comprovada.

**Entregas:**

- período de estabilidade e critérios objetivos cumpridos;
- revogação/rotação de credenciais antigas e remoção de acessos externos;
- retenção legal de dados/logs, backup final e documentação operacional;
- desligamento do worker, API, VideoHub e storage antigos sem editar seu código-fonte;
- pós-implementação, custos e backlog de melhorias.

**Saída:** ausência de tráfego/dependências legadas confirmada e plano de recuperação arquivado.

## Marcos sugeridos

- **Marco A — Base confiável:** Fases 0 a 4.
- **Marco B — MVP clínico web:** Fases 5 a 8.
- **Marco C — Paridade funcional por perfil:** Fases 9 a 14.
- **Marco D — Ecossistema integrado:** Fases 15 a 20.
- **Marco E — Produção endurecida e validada:** Fases 21 a 25.
- **Encerramento:** Fase 26.

## Decisões que deverão ser confirmadas com o proprietário

- provedor de hospedagem da API/Blazor/MySQL fora do Azure;
- domínio e plano Cloudflare;
- estratégia de convivência ou corte do app MAUI;
- provedor definitivo de TURN e eventual substituição futura, se necessária, do SMTP ou SMSBarato já adotados;
- necessidade de gravação de chamadas (recomendação inicial: não gravar);
- política LGPD, prazos de retenção e responsáveis administrativos;
- provedor LLM, orçamento, execução local/cloud e escopo escrito do pentest Strix;
- estratégia de recuperação para contas cujo segredo legado não possa ser migrado com segurança.
