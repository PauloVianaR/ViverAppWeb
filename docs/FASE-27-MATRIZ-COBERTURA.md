# Fase 27 — Matriz de cobertura do pentest

**Referência:** relatório FASE-27-RELATORIO-PENTEST.md, commit b374ad3d5ec8c7167c3d67ed3a6e83e0a72c9890, homologação local em 01/10/2026. Referências metodológicas: OWASP WSTG 4.2, ASVS 5.0.0, Top 10:2025 e API Top 10:2023. Esta matriz distingue resultado de **um cenário** da segurança da **classe inteira**.

Legenda: **T** = cenário executado, somente no alcance descrito; **P** = parcialmente executado; **B** = bloqueado por ambiente/pré-requisito; **D** = ainda não executado; **NA** = não aplicável ao produto neste commit. F27-01 é o único defeito confirmado nesta execução.

| ID | Classe e referência | Estado | Evidência e próximo teste |
| --- | --- | --- | --- |
| ID-01 | Inventário de rotas e métodos; WSTG-INFO/API9 | P | Código, OpenAPI e caminhos críticos revisados; faltam todas as rotas/métodos e callbacks por papel. |
| ID-02 | Anônimo × API protegida; WSTG-ATHN/API2 | T | Cinco áreas sensíveis sem sessão responderam 401. Não representa inventário completo. |
| ID-03 | Autorização vertical; WSTG-ATHZ/API5 | P | Gestor, Paciente, Médico, Psicólogo e Administrador sem/com MFA exercitados em rotas representativas; faltam todos os métodos de escrita, SignalR e exportações. |
| ID-04 | BOLA horizontal; WSTG-ATHZ/API1 | P | Matriz Médico A/B × Paciente A/B deu 200/404. Psicólogo A/B bloqueado por F27-01; arquivos, agendamentos e pagamentos ainda não testados. |
| ID-05 | MFA administrativo; WSTG-ATHN/ASVS V2 | P | Sem inscrição, configurações/prontuário 403; após TOTP, 200. Faltam replay, recuperação, revogação, step-up e sessões simultâneas. |
| ID-06 | Autoatribuição de papel/mass assignment; API3/API5 | P | Cadastro de administrador e propriedade extra isAdmin rejeitados 400. Repetir em DTOs de atualização, vínculos, cobrança, Premium e controles gerenciais. |
| ID-07 | Autenticação por senha/código, recuperação e enumeração; WSTG-ATHN | D | Não exercitado em todos os canais. Usar contas sintéticas e fake de e-mail/SMS. |
| ID-08 | Aprovação de profissional e contas bloqueadas; WSTG-ATHZ | P | Contas sintéticas foram bloqueadas e sessões revogadas; falta tentativa HTTP posterior e fluxo de aprovação completo. |
| ID-09 | Google OAuth: state/nonce/callback/vínculo; WSTG-ATHN | B | Integração Google desligada por regra de isolamento; testar com provedor de teste em execução posterior. |
| ID-10 | Sessão, logout e fixation; WSTG-SESS | P | Logout válido 204 e sessão posterior 401; faltam fixation, troca de senha, sessões paralelas e revogação cruzada. |
| ID-11 | CSRF; WSTG-SESS-05/ASVS V3 | P | Logout/preferência sem token ou com token inválido 400; token vinculado à sessão aceito. Repetir nos métodos de escrita restantes e com Origin/Referer alterados. |
| ID-12 | CORS/preflight; WSTG-CLNT/API8 | P | Origem hostil sem ACAO; origem Web permitida com ACAO. Faltam combinações de credenciais, subdomínios e WebSocket. |
| ID-13 | Cookies, CSP, clickjacking, cache; WSTG-CLNT | P | Cookie HttpOnly/Lax em HTTP local; CSP e X-Frame-Options DENY na landing. Faltam HTTPS/Secure/HSTS/cache clínico e inspeção no navegador integrado. |
| ID-14 | XSS refletido/armazenado/DOM; WSTG-INPV-01 | P | Marcador textual em busca não causou 5xx/stack; busca estática dirigida sem sink próprio evidente. Sem execução no navegador, não há conclusão sobre XSS. |
| ID-15 | SQL injection e consultas dinâmicas; WSTG-INPV-05 | P | Marcador seguro em busca, resposta 200; revisão estática dirigida não achou concatenação de SQL com entrada. Faltam outros parâmetros e confirmação por instrumentação. |
| ID-16 | SSTI, comando, desserialização, XML/XXE, CRLF; WSTG-INPV | D | Exige inventário completo dos parsers e entradas; não houve payload ativo além da busca. LDAP/NoSQL/GraphQL só aplicar se surgirem tais tecnologias. |
| ID-17 | Host Header, smuggling e proxy; WSTG-INPV/API8 | B | IIS/reverse proxy e origem de produção não estão disponíveis; não extrapolar do Kestrel local. |
| ID-18 | Paginação, limites e tipos inesperados; API4 | P | Página negativa e tamanho extremo deram 400. Faltam overflow, Unicode, fuso, corpos grandes e parâmetros duplicados. |
| ID-19 | SSRF/redirect/caminhos/arquivos; WSTG-INPV/API7 | D | Nenhum upload ou URL controlada foi exercitado; testar com recursos sintéticos e sem acessar endereços internos. |
| ID-20 | Objetos privados, R2/CDN e PDF; API1/API8 | B | R2 externo desligado; acesso anônimo a rota privada deu 401. Faltam autorização de download, presigned URL, cache, MIME e PDF. |
| ID-21 | Webhook PagBank, replay e idempotência; API6/ASVS V10 | B | Requisição sem assinatura recebeu 503 porque gateway estava desligado; assinatura/replay/estorno não foram avaliados dinamicamente. |
| ID-22 | Preço, Premium, caixa e fechamento; API6 | D | Homologação sem massa financeira inicial; nenhuma transação de teste foi criada. |
| ID-23 | Agenda, conflitos, estados e concorrência; API6/API4 | D | Sem massa de agendamentos; repetir com profissionais e horários sintéticos, observando rollback e auditoria. |
| ID-24 | Vídeo, convidados, SignalR, TURN; WSTG/API1 | B | Infraestrutura de mídia e salas não iniciada. Troca de convidado sem CSRF deu 400, mas ingresso/isolamento/limite de quatro não foram testados. |
| ID-25 | Notificações, e-mail, SMS e jobs; API4/API6 | B | Entregas externas e scheduler desligados. Faltam spoofing, retries, filas, preferência e vazamento em mensagens. |
| ID-26 | Rate limiting de login e IP forjado; WSTG-ATHN/API4 | T | Tentativas sintéticas resultaram em 401 e depois 429; X-Forwarded-For falso não retirou o bloqueio. Não prova controles de outros endpoints ou proxy real. |
| ID-27 | Abuso de APIs, busca, upload e exportação; API4 | D | Fora do subconjunto de login; requer quotas e identidades por papel. |
| ID-28 | DoS camada 7 limitado; API4/ASVS V13 | P | Quatro clientes, cerca de 10 req/s por 20 s em landing e agenda; 0 resposta 5xx, health 200. Não houve saturação, carga distribuída, fila ou limite máximo. |
| ID-29 | Privacidade de erro e logs; ASVS V7 | P | Corpos de erro observados sem stack; log local expôs exceção somente ao operador. Falta revisar dados pessoais/financeiros em todos os logs, URLs e exports. |
| ID-30 | Dependências/SBOM e segredos; Top 10 A03/A08 | P | Auditoria local de pacotes restaurados não sinalizou vulnerabilidade; sem confirmação de feed atualizado, scanner de segredos completo ou CI. |
| ID-31 | Prompt injection; LLM Top 10 | NA | Busca dirigida em código e projetos não encontrou recurso do aplicativo que consuma LLM/prompt. Reabrir se a arquitetura mudar. |
| ID-32 | Windows Server/IIS, TLS, Cloudflare, backup/rollback; ASVS V14 | B | Servidor físico/origem ainda não preparados; checar apenas após a Fase 26 e com allowlist própria. |
| ID-33 | Visual desktop/tablet/celular, DevTools e fluxos | B | Navegador integrado do Codex falhou no helper/sandbox; nenhuma alternativa visual foi usada. Retestar quando o navegador estiver operacional. |
| ID-34 | Auditoria clínica do Psicólogo; WSTG-ATHZ/ASVS V10 | T — F27-01 | Dois Psicólogos receberam 500 em prontuário vinculado e não vinculado; schema rejeita o papel. Corrigir em fase posterior e retestar 200/404 + evento auditado. |

## Critério de retomada

Não converter linhas P, B ou D em aprovação automática. Antes de ampliar escopo, confirmar autorização e portões da fase: banco, backup, integrações fakes, dados sintéticos, disponibilidade do navegador integrado e tetos de carga. Separar testes de produção futura desta homologação. Uma revisão humana independente continua recomendada.

## Complemento da segunda rodada — 02/10/2026

Os estados da tabela acima descrevem **somente a primeira rodada**; este complemento registra o avanço sem converter P/B/D em T automaticamente. O [relatório da segunda rodada](FASE-27-RELATORIO-PENTEST-RODADA-2.md) contém reprodução, limites e limpeza.

| ID da matriz | Complemento observado | Estado global após duas rodadas |
| --- | --- | --- |
| ID-01/03 | Inventário estático aproximado de 251 métodos de controllers e dois hubs; 756 GETs para sete papéis, mas placeholders e parâmetros incompletos. Sem cobertura dinâmica dos 96 POST, 29 PUT e 12 DELETE como classe. | P |
| ID-04/34 | Médicos A/B: 200 vinculado, 404 alheio. Psicólogos A/B: 500 nos dois, reproduz F27-01. | P / F27-01 aberto |
| ID-05/10 | Admin sintético: configurações 403 antes do MFA e 200 após TOTP; `admin/account/security` ainda 403 por papel `admin` divergente de `administrator` (F27-02). | P / F27-02 aberto |
| ID-06/11/22 | Entrada de caixa: papel incorreto 403, CSRF ausente/trocado 400, campo extra e direção inválida 400; replay da chave retornou mesmo movimento, um registro e uma auditoria. Somente um caminho financeiro manual sintético. | P |
| ID-14/15/16/18/29 | Corpus pequeno de busca GET sem evidência de SQLi/XSS, mas caracteres não ASCII produziram 500 nas buscas Gestor/Médico/Clínica (F27-03). Sem navegador ou parser/sink completo. | P / F27-03 aberto |
| ID-26/28 | 750 GETs da landing em 25,82 s, cerca de 29 req/s, 15 clientes: 300×200, 450×429, nenhum 5xx; API pronta. Não é DoS distribuído nem prova de capacidade de operações caras. | P |
| ID-09/13/17/19–25/27/32/33 | Provedor/fake/massa ou navegador integrado ausente; o browser falhou novamente. Não executado e não aprovado. | B/D conforme a tabela original |

### Novos achados

- **F27-02:** o Administrador com MFA válido recebe 403 na segurança da própria conta porque o atributo de autorização usa `admin`, mas o papel real é `administrator`.
- **F27-03:** pesquisas autenticadas de pacientes com caracteres não ASCII comuns retornam 500 em rotas de Gestor e Médico; a causa SQL exata ainda requer isolamento.

Nenhum achado desta rodada foi corrigido na Fase 27. A matriz de 100% das rotas × papéis × objetos × métodos, a inspeção visual e a revisão humana seguem pendentes.
