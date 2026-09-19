# Fase 20 — inventário e portão de publicação

## Estado

Os controles técnicos foram preparados para o domínio `https://viveralmenara.com`, mas a indexação pública está **bloqueada no código** (`PublicSitePolicy.LegalTextReviewedAndPublished = false`). Em Development, preview e staging, `robots.txt` bloqueia o rastreamento e `X-Robots-Tag` marca as respostas como `noindex, nofollow, noarchive`. Em produção, o mesmo bloqueio permanece até revisão de código, dados institucionais confirmados e aprovação dos textos jurídicos. `robots.txt` não substitui autenticação nem `noindex`; as áreas restritas permanecem protegidas e com `no-store`.

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

As páginas de Termos, Privacidade e Cookies são **minutas de homologação, não vigentes**. O cadastro legado Web já tinha `account_consents` e gravava a versão técnica `2026-09`; esse registro não comprova revisão jurídica nem substitui novo aceite após publicação. O texto da interface foi corrigido para explicitar a condição de minuta; novos cadastros usam a versão `draft-2026-09` para distingui-la de uma versão vigente. Os registros existentes foram preservados; a versão jurídica definitiva terá identificador novo, histórico imutável e mecanismo de renovação de aceite antes da abertura pública. Aviso de privacidade, aceite contratual, consentimento de cookies e consentimento clínico não serão fundidos em uma única caixa.

Não foi criada migration nesta etapa técnica: o armazenamento local da escolha anônima não exige banco e `account_consents` já existe. Histórico jurídico definitivo, pedidos de titulares e sua auditoria exigem schema, migration DB-First aplicada no MySQL 8.0.41 e fluxos autorizados quando os textos/canais estiverem definidos; não criar registros fictícios agora.

## Dados e aprovações faltantes para liberar publicação

1. Razão social, CNPJ, endereço público, canais de suporte e de privacidade, responsável/controlador e dados de contato do encarregado, se designado.
2. Finalidades e bases legais aprovadas para cadastro, prontuário, teleconsulta, pagamentos, notificações, marketing inexistente e cookies; subprocessadores e transferências internacionais.
3. Política de retenção por tipo de dado, backup e solicitação de titulares, incluindo verificação de identidade, prazos e escalonamento.
4. Textos completos de Termos, Privacidade e Cookies revisados por profissional competente, com versão, vigência, responsável e histórico; decidir se aceite expresso é necessário por finalidade.
5. Canal monitorado para direitos dos titulares e divulgação de vulnerabilidades. `security.txt` não foi publicado porque nenhum contato monitorado foi confirmado; não prometer bug bounty inexistente.
6. Contato, horários, localização e especialidades públicos confirmados. Páginas de Contato/Sobre ainda informam que os dados aguardam confirmação; não há mapa, endereço ou telefone inventado.
7. Medição real de Core Web Vitals, auditoria WCAG/zoom 200% e inspeção visual autenticada exclusivamente no monitor físico 3, além de validação de crawler no host público após a Fase 26.

Não remover o portão de indexação, ativar analytics ou publicar documento jurídico como vigente antes dessas decisões. O portão deve ser removido em revisão de código rastreável, nunca apenas por uma variável de ambiente.

## Referências primárias consultadas

- [ANPD — Guia Orientativo de Cookies e Proteção de Dados Pessoais](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/guia-orientativo-cookies-e-protecao-de-dados-pessoais.pdf)
- [Google Search Central — robots.txt não é mecanismo de privacidade](https://developers.google.com/search/docs/crawling-indexing/robots/intro)
- [Google Search Central — robots meta e X-Robots-Tag](https://developers.google.com/search/docs/crawling-indexing/robots-meta-tag)
- [LGPD — Lei nº 13.709/2018](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709.htm)
