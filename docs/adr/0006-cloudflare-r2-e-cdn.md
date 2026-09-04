# ADR 0006 — Cloudflare R2 e CDN

- **Estado:** aceita
- **Data:** 2026-09-02

## Contexto

Anexos podem conter dados de saúde. O novo produto também terá domínio próprio e precisa de distribuição eficiente de conteúdo público sem tornar documentos clínicos públicos.

## Decisão

Migrar objetos para Cloudflare R2 com credenciais distintas por ambiente e menor privilégio. Separar fisicamente conteúdo privado de ativos publicáveis.

Conteúdo privado será entregue somente após autorização, por download mediado ou URL assinada curta compatível com as limitações vigentes do R2. Domínio próprio/CDN será usado apenas para conteúdo classificado como público e com regras explícitas de cache. Uploads passarão por limites, inspeção de MIME real, nomes imprevisíveis e quarentena/antivírus.

## Consequências

- CDN não será um atalho para expor prontuários ou anexos médicos.
- A Fase 15 deverá revalidar a documentação vigente do R2 antes da implementação.
- Migração exige checksums, reconciliação, dual-read temporário e rollback.
