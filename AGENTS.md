# Regras obrigatórias para agentes de IA

Estas regras valem para toda a árvore `ViverAppWeb`. O texto normativo usa **DEVE**, **NÃO DEVE** e **PODE** em sentido obrigatório.

## 1. Escopo e preservação do legado

- O agente DEVE modificar exclusivamente arquivos dentro de `ViverAppWeb`.
- Para consultar o aplicativo MAUI, a única fonte permitida é `ViverAppMobileNew`.
- A pasta `ViverAppMobile[obsolete]` é proibida: o agente NUNCA DEVE pesquisá-la, listá-la, lê-la, indexá-la ou usá-la como referência.
- `ViverAppMobileNew`, `ViverAppApi`, `ViverAppEmailWorker`, `ViverAppVideoHub` e `ViverApp.Shared` são fontes de consulta somente leitura.
- O agente NÃO DEVE formatar, mover, renomear, excluir, gerar artefatos, restaurar pacotes nem alterar configurações nos projetos legados.
- O agente DEVE preservar alterações preexistentes do usuário, inclusive alterações não relacionadas e arquivos não rastreados.
- Código legado é referência de comportamento, não um padrão a ser copiado. Vulnerabilidades, acoplamento e contratos defeituosos DEVEM ser redesenhados e cobertos por testes.

## 2. Git e fases

- `main` e `master` são branches protegidas para fins de trabalho dos agentes.
- Cada fase do `docs/ROADMAP.md` DEVE ser executada individualmente, uma por vez, em uma branch própria criada a partir da branch principal atualizada e nunca diretamente em `main` ou `master`.
- O agente DEVE executar somente a fase expressamente solicitada pelo usuário e parar ao concluí-la. É proibido iniciar, antecipar ou incluir entregas de fases posteriores, mesmo que pareçam dependências convenientes.
- Antes de iniciar uma fase, a fase anterior DEVE estar concluída e integrada à branch principal, salvo instrução expressa do usuário em sentido diferente.
- O nome recomendado é `codex/fase-NN-descricao-curta`.
- Uma branch DEVE conter somente o escopo da fase. Mudanças de outra fase exigem outra branch ou autorização expressa do usuário.
- O agente NÃO DEVE fazer merge, rebase, force-push, push, apagar branches ou criar commits sem solicitação explícita.

## 3. Exploração e CodeGraph

- Antes de procurar ou ler código para entender uma funcionalidade, o agente DEVE verificar se existe `.codegraph/` na raiz do repositório.
- Quando `.codegraph/` existir, o agente DEVE consultar CodeGraph primeiro, preferencialmente com `codegraph_explore` e, na falta da ferramenta MCP, com `codegraph explore "pergunta ou símbolos"`.
- Busca textual e leitura direta de arquivos só DEVEM complementar o CodeGraph ou ser usadas quando `.codegraph/` ainda não existir, estiver indisponível ou não responder à questão. A limitação DEVE ser registrada no relato da fase.
- O agente NÃO DEVE criar, apagar ou reindexar `.codegraph/` sem autorização; a indexação pertence ao usuário.

## 4. Plataforma e arquitetura

- Todo projeto novo DEVE usar .NET 10 LTS e recursos compatíveis com a versão fixada em `global.json`, salvo decisão arquitetural documentada e autorizada.
- A solução DEVE permanecer organizada como monólito modular: Blazor para apresentação, API para fronteira HTTP/SignalR e módulos internos com dependências direcionadas ao domínio.
- O Blazor NÃO DEVE acessar o banco diretamente nem conter regras de negócio autoritativas. Toda operação de negócio deve passar por contratos/casos de uso da API.
- Novos recursos DEVEM ser organizados por capacidade de negócio, com contratos explícitos, validação na fronteira e baixa dependência entre módulos.
- Workers incorporados à API DEVEM usar abstrações próprias, processamento durável, idempotência e coordenação distribuída. Eles NÃO DEVEM depender de memória local para garantir entrega única.
- O único canal de comunicação externa da nova aplicação é e-mail. Firebase, push e SMS pertencem somente ao inventário legado e NÃO DEVEM ser implementados, configurados ou migrados sem nova autorização expressa.
- O SignalR/WebRTC PODE residir na API, mas autenticação, autorização de sala e estado distribuído são obrigatórios antes de produção.

## 5. Banco de dados e migrations

- O único banco relacional permitido é **MySQL Community Server 8.0.41**. O agente NÃO DEVE introduzir SQL Server, SQLite, Azure SQL, PostgreSQL ou `ServerVersion.AutoDetect`.
- A versão do provedor DEVE ser configurada explicitamente como MySQL `8.0.41`.
- Em desenvolvimento, somente `ConnectionStrings:LocalConnection` PODE ser usada como origem das credenciais locais. Connection strings legadas de Azure NÃO DEVEM ser copiadas.
- Existem dois bancos com papéis fixos e não intercambiáveis:
  - `viverappmobile` é o banco legado de referência. O agente PODE consultar seu schema e seus dados somente quando forem indispensáveis à fase, mas NUNCA DEVE executar nele `CREATE`, `ALTER`, `DROP`, `INSERT`, `UPDATE`, `DELETE`, `TRUNCATE`, migrations, seeds, procedures mutáveis ou qualquer outra operação de escrita;
  - `viverappweb` é o único banco pertencente à nova aplicação web. Toda criação ou alteração de schema do novo projeto DEVE ocorrer exclusivamente nele.
- O acesso de descoberta a `viverappmobile` DEVE usar uma sessão/credencial somente leitura quando disponível. Antes de qualquer comando, o agente DEVE confirmar programaticamente o nome do banco alvo e abortar se ele não for o esperado.
- O Entity Framework Core DEVE seguir **DB-First**. O schema de `viverappweb` é a fonte de verdade; o agente NÃO DEVE usar EF Code-First, `EnsureCreated`, `dotnet ef migrations add` ou `dotnet ef database update` para governar o schema.
- Toda alteração estrutural ou seed versionado DEVE possuir uma migration SQL incremental, imutável e versionada, executada pelo mecanismo de migrations adotado pelo projeto.
- O agente DEVE aplicar primeiro todas as migrations pendentes em `viverappweb` e somente depois executar novamente o scaffold DB-First do EF com `dotnet ef dbcontext scaffold`.
- O scaffold DEVE ser determinístico e reproduzível. Entidades e `DbContext` gerados NÃO DEVEM ser editados manualmente; extensões devem usar arquivos parciais, mapeamentos ou serviços separados.
- Models, entidades e contratos da nova aplicação DEVEM seguir exclusivamente a estrutura nova de `viverappweb`. É proibido referenciar, copiar ou reutilizar diretamente models, DTOs, `DbContext` ou contratos do banco/projetos legados.
- O agente DEVE inspecionar cada migration e o SQL efetivo, avaliar perda de dados, concorrência e reversibilidade antes de executar.
- **Toda migration pendente DEVE ser executada no MySQL 8.0.41, exclusivamente em `viverappweb`, na mesma fase em que for criada.** A fase NÃO pode ser declarada concluída se alguma migration não tiver sido aplicada, registrada pelo runner e verificada.
- Antes e depois da atualização, o agente DEVE listar as migrations e registrar no resumo quais foram aplicadas. Também DEVE executar os testes de integração relevantes.
- Migration já aplicada NÃO DEVE ser editada. Correções exigem uma nova migration.
- Operações destrutivas, renomes ambíguos e transformações de dados exigem backup verificado, plano de rollback e autorização explícita do usuário.
- Dados de produção NÃO DEVEM ser copiados para testes ou logs. Testes DEVEM usar dados sintéticos e banco isolado.

## 6. Segredos e configuração

- Segredos NUNCA DEVEM ser gravados no Git, `appsettings*.json`, código, scripts, documentação, exemplos, URLs, logs ou snapshots de teste.
- Desenvolvimento local DEVE usar .NET User Secrets. Produção DEVE usar variáveis de ambiente ou um gerenciador de segredos aprovado.
- Chaves do PagBank, credenciais do MySQL, OAuth, SMTP, Cloudflare e chaves criptográficas DEVEM permanecer apenas no backend.
- O agente NÃO DEVE imprimir valores secretos. Diagnósticos podem mostrar somente nomes de chaves e presença/ausência.
- Credenciais encontradas em histórico, artefatos publicados ou arquivos versionados DEVEM ser tratadas como potencialmente comprometidas e gerar recomendação de rotação; o agente não pode rotacioná-las sem autorização.

## 7. Segurança e privacidade por padrão

- Nenhum endpoint de negócio pode ser anônimo por acidente. A política padrão DEVE exigir autenticação e as exceções DEVEM ser explícitas e testadas.
- Autorização DEVE ser baseada em políticas e ownership; esconder botões na interface não substitui autorização na API.
- Senhas DEVEM usar hash adaptativo versionado e salgado por usuário. Criptografia reversível, AES/ECB, hash rápido e comparação manual são proibidos.
- Tokens, cookies e sessões DEVEM ter expiração, revogação, rotação e armazenamento seguros. Tokens de acesso NÃO DEVEM ficar em `localStorage` ou `sessionStorage`.
- Contas administrativas DEVEM exigir MFA resistente a phishing sempre que viável, com recuperação segura e auditoria.
- Toda entrada DEVE ser validada no servidor. Toda saída, upload, URL de retorno e conteúdo HTML DEVEM ser tratados segundo o contexto para evitar injeção, XSS, SSRF, path traversal e mass assignment.
- A solução DEVE aplicar HTTPS, HSTS em produção, CSP, headers defensivos, antiforgery, CORS por allowlist, limitação de tamanho, rate limiting por risco, bloqueio progressivo, honeypot apenas como camada adicional e logs de segurança sem dados sensíveis.
- Pagamentos e webhooks DEVEM validar autenticidade, ser idempotentes, tolerar reenvio e nunca confiar em preço, status ou identidade enviados pelo navegador.
- Dados pessoais e de saúde DEVEM seguir minimização, finalidade, retenção, consentimento, trilha de auditoria e controles compatíveis com a LGPD.
- Dependências DEVEM ser oficiais, mantidas e verificadas. Alertas críticos/altos impedem a conclusão da fase, salvo exceção documentada e autorizada.

## 8. Qualidade, testes e observabilidade

- Antes de alterar código, o agente DEVE entender o fluxo afetado e definir critérios de aceite verificáveis.
- Toda regra de negócio nova ou corrigida DEVE possuir testes automatizados. Correções de bugs DEVEM incluir teste de regressão.
- A API DEVE usar contratos próprios; entidades de persistência não devem ser expostas diretamente.
- Operações assíncronas DEVEM propagar `CancellationToken`. Datas persistidas DEVEM usar UTC e conversões de fuso devem acontecer nas bordas.
- Operações financeiras, webhooks, filas e comandos repetíveis DEVEM possuir chave de idempotência e transações com limites claros.
- Logs DEVEM ser estruturados, correlacionáveis e redigidos; senhas, tokens, documentos, dados médicos e payloads sensíveis nunca podem aparecer neles.
- Ao final de cada fase, o agente DEVE executar restore, build com warnings como erros, testes, migrations pendentes e verificações específicas de segurança/layout. O resumo DEVE informar comandos e resultados sem esconder falhas.
- O agente NÃO DEVE silenciar warnings, desabilitar testes ou reduzir controles de segurança apenas para fazer a pipeline passar.

## 9. API e compatibilidade

- Endpoints DEVEM ser versionados e documentados com OpenAPI quando a API começar a ser implementada.
- Erros DEVEM seguir Problem Details sem stack trace ou detalhes internos para o cliente.
- Listagens DEVEM ter paginação e limites máximos. Consultas DEVEM evitar carregamento irrestrito e N+1.
- Mudanças incompatíveis exigem estratégia de versão e plano de transição. Compatibilidade com o MAUI só será mantida quando fizer parte explícita da fase.

## 10. Interface web

- O layout DEVE ser concebido para web, não transcrito literalmente do XAML.
- Componentes DEVEM funcionar em celular, tablet e desktop, com acessibilidade WCAG 2.2 AA, navegação por teclado, foco visível, semântica, contraste e respeito a redução de movimento.
- A interface DEVE contemplar loading, vazio, erro, sucesso, sessão expirada e conectividade degradada.
- Nenhuma decisão crítica de autorização, pagamento ou integridade pode existir somente no cliente.

## 11. Limites de autonomia

- O agente PODE criar e editar arquivos dentro de `ViverAppWeb`, executar builds/testes e aplicar migrations locais requeridas pela fase.
- O agente NÃO DEVE publicar em produção, comprar domínio, alterar DNS/CDN/WAF, enviar e-mails reais, disparar cobranças, criar usuários externos ou acessar dados de produção sem autorização explícita.
- O agente NÃO DEVE executar ações destrutivas, limpar banco, apagar dados, redefinir Git ou remover arquivos do usuário sem alvo exato, backup quando aplicável e autorização.
- Dúvidas que alterem produto, custo, provedor, modelo de dados ou segurança DEVEM ser registradas como decisão pendente e levadas ao usuário antes da implementação.
