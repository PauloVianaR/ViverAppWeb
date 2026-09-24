# Fase 20 — SEO, privacidade, cookies e conformidade Web

## Estado e objetivo

**Estado:** antecipada por decisão do proprietário; controles técnicos implementados na branch `codex/fase-20-seo-privacidade-cookies`. A identidade institucional foi confirmada pelo comprovante oficial fornecido, os textos receberam revisão técnica interna e foram aprovados para publicação pelo proprietário em 21/09/2026, e os canais `contato@`, `privacidade@` e `seguranca@viveralmenara.com` foram ativados no Cloudflare Email Routing. A versão jurídica vigente é `2026-09-21`. Em ambientes não produtivos a indexação continua bloqueada; em produção ainda depende da configuração explícita, teste de entrega dos canais e validações de publicação.

**Branch:** `codex/fase-20-seo-privacidade-cookies`.

Preparar o ViverApp para presença pública responsável em `viveralmenara.com`: indexar somente conteúdo público útil, estabelecer um padrão técnico de SEO, obter preferências de cookies de forma transparente, publicar documentos jurídicos versionados e fechar lacunas essenciais de confiança, acessibilidade e operação de um software Web de saúde.

Esta fase não transforma `robots.txt` em controle de segurança, não indexa áreas autenticadas e não publica textos jurídicos fictícios como se tivessem revisão profissional.

**Evidências e bloqueios:** [inventário de cookies e portão de publicação](fase-20/PUBLICACAO-E-INVENTARIO.md). Como a Fase 20 foi antecipada antes da atual Fase 24 de Administração (antiga Fase 25), as medições de produção, hardening final e revisão jurídica são critérios de liberação, não premissas consideradas já realizadas.

## Dependências e limites

- executar agora, após a Fase 19, antecipando o fundamento público, de privacidade e cookies; o hardening da nova Fase 25 continua obrigatório antes de produção;
- concluir os controles técnicos antes da Fase 26 de infraestrutura e da Fase 27 de pentest;
- mudanças externas em DNS, Search Console, serviços analíticos ou fornecedores continuam dependentes de autorização;
- os conteúdos de Termos, Privacidade e Cookies usam os dados reais fornecidos e foram aprovados institucionalmente na versão `2026-09-21`; qualquer mudança material exige nova versão e renovação do aceite quando aplicável;
- nenhum tracker de marketing é requisito desta fase; a escolha padrão será não instalar rastreamento desnecessário;
- dados de saúde nunca serão enviados a analytics, pixels, mapas de calor, gravação de sessão ou ferramentas publicitárias.

## Domínio canônico e ambientes

- domínio canônico proposto: `https://viveralmenara.com`;
- `www.viveralmenara.com` redirecionará permanentemente para o canônico na Fase 26;
- `api.viveralmenara.com`, páginas autenticadas e arquivos privados nunca serão indexáveis;
- produção recebe configuração pública seletiva;
- desenvolvimento, preview e staging respondem `X-Robots-Tag: noindex, nofollow, noarchive` e `robots.txt` com bloqueio geral;
- ambientes não produtivos também exigem autenticação/rede restrita: `robots.txt` não protege conteúdo.

## Robots, indexação e sitemap

### `robots.txt`

Produção deve:

- permitir crawling apenas das páginas públicas úteis;
- bloquear caminhos de baixo valor de crawling, sem confiar nisso para esconder dados;
- referenciar o sitemap absoluto;
- não conter segredo, caminho interno sensível ou informação de infraestrutura;
- ser testado com o host canônico e `Content-Type: text/plain; charset=utf-8`.

Rotas privadas serão protegidas por autenticação e terão `noindex`, `noarchive` e cache privado/no-store. A orientação oficial do Google deixa claro que `robots.txt` gerencia crawling, mas não é mecanismo para impedir indexação.

### `sitemap.xml`

- gerar somente URLs canônicas públicas, absolutas e em HTTPS;
- incluir landing, páginas institucionais, contato, Termos, Privacidade, Cookies e Acessibilidade;
- excluir login, cadastro, recuperação, callbacks OAuth, API, perfis, agenda, pagamentos, prontuários, arquivos e páginas com parâmetros pessoais;
- emitir UTF-8 e datas de modificação verdadeiras;
- referenciar o sitemap no `robots.txt`;
- validar XML, códigos HTTP, redirects e URLs órfãs;
- preparar submissão ao Google Search Console somente após autorização e publicação.

## Padrão SEO técnico e editorial

### Metadados por página pública

- título único, conciso e contextual;
- meta description útil e não enganosa;
- URL curta, estável, minúscula e sem identificador sensível;
- `link rel="canonical"` autorreferente para o domínio oficial;
- `lang="pt-BR"`, headings hierárquicos e landmarks semânticos;
- Open Graph e metadados de compartilhamento com imagem oficial do próprio domínio;
- favicon, ícones e web manifest coerentes com a logo atual;
- breadcrumbs quando a hierarquia realmente existir;
- dados estruturados somente com fatos públicos verificáveis da clínica, validados e sem avaliações inventadas;
- nenhuma keyword stuffing, página doorway ou conteúdo gerado apenas para buscador.

### Conteúdo público mínimo

- landing com proposta clara, especialidades/serviços realmente ativos e chamada para cadastro Google fácil;
- sobre a clínica e informações de contato aprovadas;
- localização/horários reais quando autorizados a publicar;
- página de contato sem revelar dados pessoais de profissionais além do aprovado;
- perguntas frequentes objetivas;
- páginas jurídicas e de acessibilidade;
- títulos e textos consistentes com a clínica única e sem alegações médicas não comprovadas.

### Performance e renderização

- HTML inicial útil para páginas públicas do Blazor;
- imagens responsivas, dimensões explícitas, formatos eficientes e lazy loading fora da primeira dobra;
- fontes e CSS críticos sem bloquear desnecessariamente a renderização;
- evitar layout shift e JavaScript excessivo;
- medir Core Web Vitals em celular e desktop com orçamento documentado;
- CDN/cache apenas para conteúdo público seguro; HTML personalizado e áreas autenticadas não entram em cache compartilhado.

## Cookies e preferências

### Inventário antes do banner

Catalogar cada cookie/storage item por:

- nome, domínio, caminho e duração;
- first-party ou terceiro;
- finalidade e dados tratados;
- categoria e base legal a validar;
- ambiente e código responsável;
- condição de criação e método de exclusão.

Categorias mínimas:

- **Necessários:** sessão, antiforgery, segurança, balanceamento e preferências indispensáveis;
- **Preferências:** escolhas de interface não essenciais;
- **Medição/analytics:** somente se um provedor privacy-friendly for aprovado;
- **Marketing:** desabilitado por padrão e fora do produto enquanto não houver justificativa explícita.

### Popup/banner

- primeira visita mostra linguagem simples, finalidade e link para detalhes;
- oferecer “Aceitar todos”, “Recusar não necessários” e “Personalizar” com destaque equivalente e sem dark patterns;
- cookies não necessários ficam bloqueados antes da escolha;
- consentimento é granular, livre, informado, inequívoco e revogável quando for a base usada;
- não condicionar atendimento clínico a cookie não necessário;
- preferências podem ser revistas por link persistente “Preferências de cookies”;
- registrar versão da política, categorias, instante e origem de modo minimizado;
- respeitar retirada de consentimento e apagar/desativar cookies não necessários quando tecnicamente possível;
- mudança material de finalidade/fornecedor exige nova decisão;
- banner acessível por teclado/leitor de tela, responsivo, com foco correto e sem bloquear conteúdo legal.

O desenho seguirá o [Guia Orientativo — Cookies e Proteção de Dados Pessoais da ANPD](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/guia-orientativo-cookies-e-protecao-de-dados-pessoais.pdf).

## Documentos e aceite

### Aviso/Política de Privacidade

Conteúdo mínimo sujeito a revisão jurídica:

- identidade e contato do controlador, operador(es) e encarregado/canal de privacidade;
- categorias de dados, com destaque para dados de saúde;
- fontes, finalidades e bases legais por operação;
- compartilhamentos, subprocessadores e transferências internacionais;
- retenção e critérios de eliminação;
- segurança e limites realistas, sem promessa absoluta;
- direitos do titular e como exercê-los;
- uso de cookies e tecnologias semelhantes;
- tratamento de crianças/adolescentes, se aplicável;
- decisões automatizadas, se existirem;
- incidentes e canais de contato;
- versão, vigência e histórico de alterações.

### Termos de Uso e termos do serviço

- escopo da plataforma e papéis;
- requisitos de conta, contato e segurança;
- aprovação de Médico/Gestor;
- marcação, cancelamento, pagamento, estorno e responsabilidades;
- limites da videochamada e contingência;
- conduta proibida e uso aceitável;
- propriedade intelectual;
- disponibilidade, suporte e encerramento;
- legislação/foro definidos por revisão jurídica;
- distinção clara entre plataforma, clínica e ato médico;
- versão, vigência e alteração material.

### Política de Cookies

- inventário legível e atualizado;
- explicação das categorias e escolhas;
- terceiros e durações;
- como retirar consentimento/limpar cookies;
- vínculo com a Política de Privacidade.

### Aceite e versionamento

- termos indispensáveis e avisos de privacidade não serão confundidos com consentimento genérico para todas as operações;
- aceite explícito quando juridicamente requerido, vinculado à versão e registrado com minimização;
- nova versão material solicita renovação de aceite sem bloquear acesso a informações legais;
- guardar histórico imutável das versões publicadas;
- disponibilizar download/visualização da versão aceita;
- contas existentes recebem fluxo controlado de atualização;
- não usar caixas pré-marcadas.

## Direitos do titular e governança

- publicar canal e procedimento para confirmação, acesso, correção, portabilidade quando aplicável, informação, oposição/revogação e eliminação nos limites legais;
- autenticar o solicitante proporcionalmente ao risco antes de entregar dados;
- registrar solicitação, prazo, responsável, decisão e entrega sem expor o conteúdo em logs;
- exportação de dados sensíveis é privada, expira e exige step-up;
- definir matriz de retenção para conta, prontuário, financeiro, documentos, notificações, auditoria e backups;
- manter registro de subprocessadores e propósito;
- executar avaliação de impacto para prontuário/telemedicina e revisar base legal com profissional competente;
- estabelecer fluxo de incidente e comunicação à ANPD/titulares conforme avaliação jurídica vigente;
- revisar separação entre consentimento de cookies, consentimento clínico e outras bases legais.

Dados referentes à saúde são classificados como sensíveis pela [LGPD, Lei nº 13.709/2018](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709compilado.htm). O conteúdo final não poderá ser aprovado apenas por desenvolvimento.

## Acessibilidade e confiança

- manter WCAG 2.2 nível AA nas páginas públicas, banner, formulários e documentos;
- publicar Declaração de Acessibilidade e canal para barreiras encontradas;
- navegação por teclado, foco visível/não encoberto, contraste, labels, mensagens de erro e autenticação acessível;
- respeitar redução de movimento e tamanhos mínimos de alvo;
- linguagem simples e alternativa textual para conteúdo visual;
- validar com automação e inspeção manual no monitor 3.

Referência: [WCAG 2.2 — W3C Recommendation](https://www.w3.org/WAI/standards-guidelines/wcag/).

## Outros elementos Web essenciais

### Segurança pública e vulnerabilidades

- publicar `/.well-known/security.txt` conforme RFC 9116 somente depois de existir contato monitorado, escopo e política de divulgação;
- incluir `Contact`, `Expires`, `Canonical`, idiomas e link da política; não prometer bug bounty inexistente;
- manter página de divulgação responsável sem autorizar ataque a produção;
- revisar headers, TLS, CSP, cookies, CORS, cache, páginas de erro e ausência de stack trace;
- página 404 útil e 500 genérica, ambas sem PII e com telemetria correlacionável;
- `security.txt` seguirá a [RFC 9116](https://datatracker.ietf.org/doc/html/rfc9116).

### Identidade visual e aplicação instalável

- favicon e ícones em tamanhos corretos;
- `manifest.webmanifest` com nome, nome curto, cores, ícones e `start_url` pública segura;
- nenhuma alegação de PWA/offline para jornadas clínicas sem cache seguro e testes específicos;
- não armazenar prontuário, pagamentos ou respostas privadas em service worker/cache offline.

### Comunicação e suporte

- páginas de Contato, Suporte e Status/indisponibilidade com canais reais;
- rodapé consistente com CNPJ/endereço quando aprovado, links legais, privacidade, cookies e acessibilidade;
- e-mails transacionais incluem identificação, motivo do contato e links seguros, sem misturar marketing;
- preferências de marketing, se um dia existirem, são separadas de mensagens essenciais de cuidado/segurança.

### Observabilidade sem vigilância

- telemetria técnica first-party e minimizada;
- IP/user-agent retidos somente pelo prazo justificado e redigidos quando possível;
- erros do browser não enviam formulário, token, URL com dado sensível ou conteúdo do prontuário;
- session replay, heatmaps e pixels publicitários permanecem proibidos sem nova fase, avaliação de impacto e autorização explícita.

## Banco DB-First planejado

Somente se o inventário demonstrar necessidade, criar migration SQL para:

- versões publicadas de documentos jurídicos;
- aceites por conta/finalidade/versão;
- preferências de cookies versionadas e minimizadas;
- solicitações de titulares e trilha de atendimento;
- registro de subprocessadores/versões quando for requisito operacional.

A migration será aplicada integralmente em `viverappweb` no MySQL local 8.0.41 antes do scaffold. Nenhuma alteração ocorrerá em `viverappmobile`.

## Plano de execução

1. inventariar páginas públicas, cookies, storage, scripts, terceiros, headers e telemetria;
2. confirmar domínio canônico, conteúdo público e dados institucionais;
3. obter/revisar textos jurídicos e política de retenção com responsável competente;
4. projetar schema de versões/aceites somente se necessário;
5. criar/aplicar migration e regenerar EF DB-First;
6. implementar `robots.txt`, `sitemap.xml`, `noindex` por ambiente e canonicals;
7. criar componentes de metadados, Open Graph, JSON-LD e padrão editorial;
8. implementar páginas legais, rodapé e histórico de versões;
9. implementar inventário e gerenciador de preferências de cookies sem carregar scripts antes da escolha;
10. implementar canal/fluxo mínimo dos direitos do titular;
11. implementar acessibilidade, manifest, páginas de erro e `security.txt` quando os contatos existirem;
12. medir SEO técnico, performance, acessibilidade, segurança e vazamento de dados;
13. validar no monitor 3 e testar celular/tablet/desktop, crawler e impressão;
14. registrar pendências jurídicas/externas reais e parar sem iniciar a Fase 21.

## Testes e evidências

- produção: `robots.txt` e sitemap corretos; staging/dev: bloqueio de indexação em header e robots;
- nenhuma rota autenticada, callback, API ou URL privada no sitemap;
- canonical, title, description, idioma, Open Graph e structured data válidos;
- redirects não criam loop e apontam ao domínio canônico;
- cookies não necessários inexistem antes do consentimento;
- aceitar, rejeitar, personalizar e retirar preferência funcionam em sessão anônima e autenticada;
- mudança de versão solicita nova decisão quando aplicável;
- banner e páginas legais passam teclado, leitor de tela, contraste e zoom 200%;
- páginas privadas usam `no-store` e não vazam PII em metadados/URLs/analytics;
- Core Web Vitals/orçamentos registrados em celular e desktop;
- links, status HTTP, sitemap e JSON-LD validados automaticamente;
- `security.txt` válido e com expiração futura, se publicado;
- textos finais possuem responsável, versão, vigência e evidência de revisão;
- validação visual registrada exclusivamente no monitor 3.

## Critérios de saída

- crawling e indexação estão corretos por ambiente;
- sitemap contém somente URLs públicas canônicas;
- padrão SEO é reutilizável e aplicado a todas as páginas públicas;
- cookies não necessários dependem de escolha válida e revogável;
- Termos, Privacidade, Cookies e Acessibilidade estão publicados, versionados e aprovados;
- canal de direitos do titular e governança de retenção estão definidos;
- páginas privadas e dados de saúde não aparecem em índice, cache compartilhado ou telemetria de terceiros;
- performance, acessibilidade e metadados atendem aos gates documentados;
- migrations eventualmente necessárias foram aplicadas e o EF regenerado;
- pendências externas/jurídicas reais estão registradas e a Fase 21 não foi iniciada.
