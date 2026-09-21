# Fase 20 — inventário e portão de publicação

## Estado

Os controles técnicos foram preparados para o domínio `https://viveralmenara.com`, mas a indexação pública está **bloqueada no código** (`PublicSitePolicy.LegalTextReviewedAndPublished = false`). Em Development, preview e staging, `robots.txt` bloqueia o rastreamento e `X-Robots-Tag` marca as respostas como `noindex, nofollow, noarchive`. Em produção, o mesmo bloqueio permanece até aprovação institucional, revisão jurídica independente e validações finais. `robots.txt` não substitui autenticação nem `noindex`; as áreas restritas permanecem protegidas e com `no-store`.

O sitemap contém somente as oito rotas públicas fixas: início, sobre, contato, perguntas frequentes, termos, privacidade, cookies e acessibilidade. Enquanto o portão estiver fechado, `/sitemap.xml` retorna 404. Não há submissão ao Search Console nem alteração de DNS nesta fase.

## Inventário inicial de cookies e armazenamento

| Item | Origem | Necessidade/finalidade | Duração e atributos conhecidos |
| --- | --- | --- | --- |
| `__Host-ViverApp.Session` (`ViverApp.Session.Local` em HTTP local) | API, autenticação | necessário; sessão autenticada revogável no servidor | até 30 dias; `HttpOnly`, `Secure` fora do HTTP local, `SameSite=Lax`, caminho `/` |
| `__Host-ViverApp.External` (`ViverApp.External.Local`) | API, vínculo externo | necessário; etapa temporária do login Google | até 5 minutos; `HttpOnly`, `Secure` fora do HTTP local, `SameSite=Lax` |
| `__Host-ViverApp.Google.Correlation.*` (sufixo `.Local.` no HTTP local) | middleware OAuth | necessário; proteção de correlação do login Google | temporário; `Secure` fora do HTTP local, `SameSite=Lax` |
| `__Host-ViverApp.Api.Antiforgery` / `__Host-ViverApp.Web.Antiforgery` (`.Local` no HTTP local) | API/Web | necessário; proteção de requisições | `HttpOnly`, `Secure` fora do HTTP local, `SameSite=Strict` |
| `viverappweb.cookie-preferences` | Web `localStorage` | lembrar decisão local, sem identidade nem conteúdo clínico | até limpeza do navegador ou mudança de versão; contém versão, categorias, instante e origem |

O middleware de autenticação e o navegador podem criar artefatos técnicos transitórios adicionais. O inventário precisa de confirmação no domínio final e nos fluxos reais de Google, pagamentos e videochamada antes da publicação. Não há Google Analytics, pixel, heatmap, replay de sessão ou script de marketing no Web. A preferência `analytics=false` e `marketing=false` é fixa; aceitar todos **não** instala rastreadores. Não carregar scripts opcionais com base apenas em um registro de preferência: futura integração exige nova revisão técnica e jurídica.

## Documentos e aceite existentes

As páginas de Termos, Privacidade e Cookies são **minutas de homologação, não vigentes**. Elas receberam revisão técnica interna em 21/09/2026 pela equipe de desenvolvimento do ViverApp, conduzida pelo Codex/OpenAI, com foco em coerência com o produto, minimização, segurança, LGPD, retenção clínica e transparência. Esta revisão não é parecer jurídico nem substitui a aprovação do representante legal da clínica e a revisão de profissional jurídico independente antes da vigência.

O cadastro legado Web já tinha `account_consents` e gravava a versão técnica `2026-09`; esse registro não comprova revisão jurídica nem substitui novo aceite após publicação. O texto da interface explicita a condição de minuta; novos cadastros usam a versão `draft-2026-09` para distingui-la de uma versão vigente. Os registros existentes foram preservados; a versão jurídica definitiva terá identificador novo, histórico imutável e mecanismo de renovação de aceite antes da abertura pública. Aviso de privacidade, aceite contratual, consentimento de cookies e consentimento clínico não serão fundidos em uma única caixa.

Não foi criada migration nesta etapa técnica: o armazenamento local da escolha anônima não exige banco e `account_consents` já existe. Histórico jurídico definitivo, pedidos de titulares e sua auditoria exigem schema, migration DB-First aplicada no MySQL 8.0.41 e fluxos autorizados quando os textos/canais estiverem definidos; não criar registros fictícios agora.

## Identidade institucional confirmada

Dados extraídos do comprovante oficial fornecido pelo proprietário e conferidos visualmente:

- razão social: `CLINICA DE OLHOS JUSTINIANO LTDA`;
- CNPJ: `35.843.469/0001-77`;
- endereço: Rua Tude Tupy, 214, Centro, Almenara/MG, CEP 39.900-000;
- telefone: `(33) 9951-2186`;
- abertura e situação: 23/12/2019, ativa;
- atividade principal: atividade médica ambulatorial com recursos para realização de procedimentos cirúrgicos;
- atividades secundárias: consultas médicas e atendimento em pronto-socorro/unidades de urgência.

O nome fantasia veio mascarado no comprovante, portanto não foi inventado. `Centro Médico Viver` permanece como marca de apresentação do portal, sem declaração de que seja o nome fantasia cadastral. O endereço de e-mail constante do comprovante aparenta pertencer ao escritório contábil e não será divulgado como contato assistencial ou de privacidade.

Os aliases institucionais `contato@viveralmenara.com`, `privacidade@viveralmenara.com` e `seguranca@viveralmenara.com` foram ativados no Cloudflare Email Routing em 21/09/2026 e encaminham para o endereço administrativo verificado `vivermobileapp@gmail.com`. Os três aparecem como regras ativas no painel. Os três registros MX, SPF e DKIM gerenciados pela Cloudflare foram confirmados no DNS público. `/.well-known/security.txt` passou a divulgar exclusivamente o canal de segurança, sem prometer recompensa ou autorizar exploração.

## Aprovações faltantes para liberar publicação

1. Aprovação do representante legal da clínica e revisão jurídica independente dos textos completos, com nova versão, início de vigência, histórico e decisão sobre aceites expressos por finalidade.
2. Nomeação e divulgação do encarregado, caso a clínica decida designá-lo, e formalização do fluxo interno e dos responsáveis que acompanharão os três aliases institucionais.
3. Aprovação da matriz detalhada de bases legais por tratamento, subprocessadores, países/garantias de transferências internacionais e responsabilidades contratuais.
4. Política de retenção por tipo de dado, documento, auditoria e backup. O prontuário já explicita o mínimo legal de 20 anos a partir do último registro, mas as demais classes ainda exigem prazo aprovado.
5. Teste de entrega ponta a ponta dos três aliases usando remetente externo autorizado e definição de prazo operacional de resposta. A configuração e o DNS já estão ativos; não foi enviado e-mail representacional de teste nesta execução.
6. Horário de atendimento e relação pública de especialidades/equipe, que não constam do comprovante e não foram inventados.
7. Medição real de Core Web Vitals, auditoria WCAG/zoom 200% e inspeção visual autenticada exclusivamente no monitor físico 3, além de validação de crawler no host público após a Fase 26.

Não remover o portão de indexação, ativar analytics ou publicar documento jurídico como vigente antes dessas decisões. O portão deve ser removido em revisão de código rastreável, nunca apenas por uma variável de ambiente.

## Referências primárias consultadas

- [ANPD — Guia Orientativo de Cookies e Proteção de Dados Pessoais](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/guia-orientativo-cookies-e-protecao-de-dados-pessoais.pdf)
- [Google Search Central — robots.txt não é mecanismo de privacidade](https://developers.google.com/search/docs/crawling-indexing/robots/intro)
- [Google Search Central — robots meta e X-Robots-Tag](https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag)
- [LGPD — Lei nº 13.709/2018](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709.htm)
- [Lei nº 13.787/2018 — digitalização, uso e guarda de prontuário](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13787.htm)
- [CFM — Resolução nº 2.314/2022 sobre telemedicina](https://portal.cfm.org.br/noticias/apos-amplo-debate-cfm-regulamenta-pratica-da-telemedicina-no-brasil/)
