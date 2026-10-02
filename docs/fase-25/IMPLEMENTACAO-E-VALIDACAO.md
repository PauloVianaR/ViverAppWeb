# Fase 25 — robustez, desempenho e segurança ofensiva

## Estado e limites

Esta fase introduz instrumentos de medição e endurecimento verificáveis no ambiente local. **Ainda não satisfaz o critério de saída da fase**: a carga autenticada, os provedores externos, DAST autenticado, revisão independente, assinatura de artefatos e SLOs sob tráfego representativo dependem de homologação isolada e/ou pessoas autorizadas. Não inferir aprovação desses itens a partir de testes unitários ou de uma medição em loopback.

O desenvolvimento e os testes de regressão usam `viverappweb` local 8.0.41. Após autorização expressa em 28/09/2026, foi criado também o banco **persistente** `viverappweb_homolog`, no mesmo servidor, somente para dados sintéticos e ensaios ofensivos isolados. Nenhum banco descartável, cobrança real ou dado clínico real foi usado.

Em 28/09/2026, `scripts/security-check.ps1` passou integralmente: build Release com zero avisos/erros, **253 testes** aprovados, `dotnet format --verify-no-changes`, migrations 0001–0048 aplicadas, verificação do MySQL 8.0.41 e auditoria NuGet sem vulnerabilidades reportadas. Treze arquivos com dívida de whitespace preexistente foram formatados mecanicamente para que o portão global passasse; não houve alteração intencional de lógica nesses arquivos. O SBOM contém 122 pacotes em 13 projetos. Gerar novamente depois do commit e em cada release para vincular o artefato ao commit correspondente.

Após acrescentar a trava e o teste de homologação, o mesmo portão foi repetido em PowerShell 7: build Release sem avisos/erros, **254 testes** aprovados, formatação verificada, os 48 scripts confirmados em `viverappweb`, auditoria NuGet sem vulnerabilidades reportadas e SBOM CycloneDX gerado. `viverappweb_homolog` também foi verificado separadamente com as 48 migrations. Uma tentativa pelo Windows PowerShell 5 falhou apenas na geração do SBOM por não suportar `ConvertFrom-Json -AsHashtable`; por isso a execução oficial deste script exige PowerShell 7.

No navegador integrado, a landing pública foi inspecionada em larguras de 1280, 820 e 390 px: cabeçalho, chamada principal e ação de cadastro permaneceram legíveis, com `scrollWidth` não excedendo a largura útil; existe `main` e exatamente um `h1`. Isso é uma amostra visual da superfície pública, não uma validação de todos os papéis, fluxos privados, zoom ou engines de navegador. Durante o início local houve mensagens de chaves antigas Data Protection não decifráveis por DPAPI; nenhuma chave/certificado foi removido, e a página pública respondeu. O anel de chaves da implantação precisará de revisão antes de produção.

## Implementado

| Área | Entrega | Evidência e alcance |
| --- | --- | --- |
| Agenda | Uma projeção resumida substitui as múltiplas leituras completas para indicadores; a página preserva filtros e ordenação. | Testes de operações clínicas e agendamento; medir novamente sob carga representativa. |
| Banco | Migration `0048__appointment_calendar_range_index.sql` cria `(starts_at_utc, id)`; scaffold DB-First regenerado. | `analyze-hot-paths` usa `EXPLAIN` de consultas fixas, sem mutação. Antes: agenda clínica via `ux_appointments_doctor_start`, acesso `index`, estimativa 12 linhas; depois: `ix_appointments_start_id`, acesso `range`, estimativa 1 linha na amostra local. A estimativa não é um benchmark de produção. |
| Checkout | Chave de idempotência do provedor estável para paciente, atendimento e versão do registro; timeout após aceite remoto pode ser repetido com a mesma chave. | Teste de integração no MySQL local simula primeiro timeout e segundo sucesso, sem pagamento duplicado. Não substitui homologação PagBank Sandbox. |
| Observabilidade | Métricas OTel de tentativas/duração de checkout e exportação analítica, somente com rótulo `success`/`failure`. | Sem CPF, nome, token ou identificador de paciente como atributo. Coletor e alertas externos pertencem à infraestrutura futura. |
| Carga local | `tools/ViverApp.LoadProbe` limita alvo a loopback, duração, concorrência e quantidade; cenários `api-ready`, `landing` e `agenda` com cookie sintético fornecido pelo operador. | API pronta: 6.171 respostas/10 s, 8 clientes, nenhuma falha, p95 23,61 ms. Landing com espaçamento para não induzir rate limit: 78 respostas/10 s, 8 clientes, nenhuma falha, p95 78,97 ms. Hardware e dados locais; não extrapolar para produção. Agenda autenticada, login, checkout, SignalR e workers não medidos. |
| Dependências | Lock files NuGet, restore em `--locked-mode`, auditoria de pacotes transitivos e configuração semanal de Dependabot. | A auditoria local não retornou vulnerabilidades conhecidas na data da execução; atualizar a checagem antes de release. Dependabot só operará após o repositório ser hospedado com a funcionalidade habilitada. |
| Inventário | `scripts/generate-sbom.ps1` gera CycloneDX 1.6, hashes SHA-512 de pacotes e digest SHA-256 do documento, ligado a commit/estado Git. | Saída em `artifacts/` ignorada; digest não é assinatura. Gerar novamente após o commit final e em cada release. |
| DAST | `scripts/dast-passive.ps1` executa ZAP Baseline em alvo local explícito, com imagem fixada por digest; `scripts/dast-homolog-manager.ps1` faz pré-voo autenticado e scan ativo limitado a uma rota GET de busca. | Scan público e um ensaio autenticado pontual executados em 28/09/2026; detalhes abaixo. Cobertura integral por papel ainda pendente. |

### Ensaio ZAP em Docker — 28/09/2026

Docker Desktop Linux Engine 29.6.2; imagem oficial `ghcr.io/zaproxy/zaproxy@sha256:781a2bdaea47324e7bab583e2263f21d257b0aee61ed51521a5be45f5f5081ef`. API e Web foram executadas em `Development` por HTTP local, na Web em porta separada `5196`; nenhum banco ou contêiner de banco foi criado. A permissão adicional de Host para `host.docker.internal` existiu somente na variável de ambiente desse processo. O primeiro scan retornou apenas HTTP 400 por Host não permitido e foi descartado como inválido.

No scan válido inicial, 28 URLs públicas produziram 0 FAIL e 5 WARN, incluindo um alerta **médio** de CSP: `connect-src` aceitava `ws:` e `wss:` para qualquer host. A política foi alterada para origens WebSocket exatas do próprio Host e das origens HTTP(S) já permitidas, com teste automatizado. O reteste percorreu 28 URLs, retornou **0 FAIL, 4 WARN e 63 PASS**; o alerta médio de CSP desapareceu. O código 2 do script reflete avisos ainda presentes, não aprovação irrestrita.

Triagem dos quatro avisos restantes: `Cross-Origin-Embedder-Policy` ausente (baixo; decidir em HTTPS/staging com Google e vídeo, pois `require-corp` pode bloquear recursos entre origens); comentário de reconexão Blazor (informativo, texto de interface sem segredo); `no-store` em páginas públicas (informativo, política conservadora de cache); identificação de cookie de antifalsificação (informativo, esperado, `HttpOnly` e `SameSite`). Os relatórios JSON/HTML estão em `artifacts/dast/`, ignorados pelo Git. A abertura no navegador integrado e a interação com as preferências de cookies funcionaram sem erros no console após o endurecimento da CSP; isso não valida todos os fluxos SignalR autenticados.

### Homologação local isolada — 28/09/2026

O runner de migrations ganhou `--homolog` somente para `inspect-homolog`, `bootstrap`, `status`, `apply`, `verify` e `seed-homolog`. O pré-voo confirmou que `viverappweb_homolog` não existia; ele foi criado no MySQL 8.0.41 e recebeu as **48 migrations** versionadas. O schema foi verificado após os ensaios. Não houve importação de `viverappweb` ou `viverappmobile`. O seed adicionou apenas duas contas com domínio `.invalid` e senha aleatória transitória, de Gestor e Paciente; hashes unidirecionais, sem credencial registrada em arquivo ou relatório. O seed pode rotacionar as senhas dessas mesmas contas em futuras execuções.

A API exige `Homologation:Enabled=true`, ambiente `Development`, conexão estritamente com `viverappweb_homolog` e recusa a partida se entrega de e-mail/SMS, notificações externas, PagBank, R2 ou Google OAuth estiverem habilitados. `scripts/start-homolog-api.ps1` deriva a conexão dos user-secrets **sem editá-los**; `scripts/start-homolog-web.ps1` aponta a interface para a API local. Os processos usaram HTTP local nas portas 5197/5198; a permissão de Host para Docker existiu só no processo. Ambos responderam 200; ao terminar, os processos e contêineres foram encerrados, deixando apenas o banco persistente. Avisos DPAPI de chaves antigas continuaram aparecendo no boot; nenhuma chave ou certificado foi alterado.

Na Web de homologação, o ZAP Baseline percorreu 28 URLs públicas: **0 FAIL, 4 WARN, 63 PASS**, os mesmos tipos já triados acima. O primeiro teste do ZAP autenticado foi deliberadamente interrompido quando seu pré-voo recebeu 401; o script final bloqueia automaticamente a varredura ativa nessa condição. Após corrigir a passagem da sessão sintética, o pré-voo recebeu 200. Um scan ativo em `/api/v1/manager/home` (sem parâmetro) levou 40 s e não apontou alertas; por ter pouca superfície de entrada, repetiu-se o ensaio na busca GET `/api/v1/manager/patients?search=Paciente`. Este scan levou **1m47s**, limitou-se a **um endpoint**, teve respostas HTTP 2xx e **nenhum alerta** no JSON. O relatório registra uma falha de rede informativa e respostas lentas; requerem correlação antes de inferir estabilidade. O resultado não prova ausência de vulnerabilidades nem cobre outros papéis, escrita, pagamento, documentos, vídeo ou prestadores externos. Relatórios ignorados em `artifacts/dast/phase25-homolog-*.json`.

Um probe autenticado adicional de Agenda usou a mesma conta sintética de Gestor por 15 s e 4 clientes: **116/116 respostas HTTP 200**, 7,59 req/s, p50 19,47 ms, p95 78,4 ms e p99 2.643,07 ms. O p99 isolado merece investigação; a base de homologação quase vazia e apenas uma sessão não representam um pico clínico. Não converter essa medição em SLO aprovado.

## Portões de segurança

O padrão de referência é **OWASP ASVS 5.0.0**, com versão explícita. A revisão deve cobrir, com evidências por requisito aplicável: autenticação/MFA e recuperação; sessão e CSRF; autorização por papel e ownership; validação/saída/CSP; dados clínicos, upload e R2 privado; criptografia/segredos; pagamento/webhook/idempotência; API/SignalR; logs/auditoria; disponibilidade e configuração. Os testes atuais são evidência parcial, **não** uma avaliação ASVS concluída. Uma pessoa independente da implementação deve registrar achados, severidade, reprodução e reteste. Achados críticos/altos abertos bloqueiam o critério de saída.

DAST Baseline é passivo e não prova ausência de falhas. Antes do pentest abrangente da Fase 27, executar também DAST autenticado convencional em staging isolado, com contas sintéticas de todos os papéis, regras de exclusão de operações destrutivas e relatório revisado. Não apontar scanner para produção, provedores reais ou endpoints de pagamento sem autorização específica.

## Critérios de desempenho e operação a homologar

Medir separadamente login, agenda/consulta, checkout Sandbox, conexões/reconexões SignalR, processamento de outbox/jobs e downloads privados. Usar cargas por perfil, período de aquecimento, tamanho de base, concorrência, taxa, ambiente e percentis p50/p95/p99 registrados. Definir SLOs formais com o proprietário e a infraestrutura antes de declarar sucesso. A primeira proposta de alerta e resposta está em [OPERACAO.md](OPERACAO.md); os valores são candidatos, não metas aprovadas nem atendidas.

## Conformidade e experiência

Revisar inventário de dados, base legal, retenção, direitos do titular, contratos de operadores e fluxo de incidente com profissional jurídico responsável. A revisão técnica não equivale a conclusão jurídica LGPD. Reexecutar testes automatizados de acessibilidade e conferir jornadas nos tamanhos desktop/tablet/celular no navegador integrado; compatibilidade entre engines requer navegadores adicionais em homologação. Não declarar WCAG ou compatibilidade plena com base em uma inspeção única.

## Fontes normativas e operacionais

- [OWASP ASVS](https://owasp.org/projects/asvs/)
- [ZAP Baseline](https://www.zaproxy.org/docs/docker/baseline-scan/)
- [NuGet lock files](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)
- [ANPD — guia de cookies](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/guia_orientativo_cookies_e_protecao_de_dados_pessoais)
