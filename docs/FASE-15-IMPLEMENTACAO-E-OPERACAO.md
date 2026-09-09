# Fase 15 — implementação e operação do armazenamento

## Decisões de arquitetura

- `viveralmenara.com` é a zona oficial. O apontamento do site e da API aguarda a origem da Fase 21; nenhum registro DNS fictício deve ser criado.
- Documentos clínicos, laudos, anexos e comprovantes pertencem exclusivamente ao bucket privado. Eles nunca usam `r2.dev`, domínio público ou cache compartilhado.
- A API continua sendo a fronteira de autorização. O download mediado valida sessão, papel, vínculo com o atendimento, estado do documento, tamanho e SHA-256.
- URLs pré-assinadas R2 existem apenas como recurso interno, expiram em no máximo 300 segundos e usam o endpoint S3. O fluxo clínico padrão não as entrega diretamente ao navegador.
- A CDN `cdn.viveralmenara.com` é reservada a ativos deliberadamente públicos. Ela não compartilha bucket, credencial ou prefixo com documentos privados.
- Chaves privadas seguem `private/{environment}/{yyyy}/{MM}/{guid}` e não contêm nome, conta, prontuário, CPF, e-mail nem nome original do arquivo.

## Recursos Cloudflare

| Ambiente | Bucket | Acesso público | Uso |
|---|---|---:|---|
| Desenvolvimento | `viverappweb-development-private` | Não | Testes locais autorizados e migração controlada |
| Desenvolvimento | `viverappweb-development-public` | Não | Preparação de ativos públicos sem exposição externa |
| Produção | `viverappweb-production-private` | Não | Documentos sensíveis da aplicação publicada |
| Produção | `viverappweb-production-public` | Apenas por `cdn.viveralmenara.com` | Imagens e ativos classificados como públicos |

O subdomínio `r2.dev` permanece desabilitado em todos os buckets. Em 8 de setembro de 2026 foi criada a credencial `ViverAppWeb Development Private`, com leitura/escrita de objetos restrita a `viverappweb-development-private`, sem permissão administrativa sobre a conta, DNS ou outros buckets, e validade de um ano. As chaves S3 foram armazenadas somente em user-secrets.

## Segredos

Os valores abaixo ficam exclusivamente em user-secrets no desenvolvimento e no cofre do ambiente publicado:

```text
Storage:Private:Provider=R2
Storage:R2:AccountId
Storage:R2:BucketName
Storage:R2:AccessKeyId
Storage:R2:SecretAccessKey
Storage:R2:PresignedUrlLifetimeSeconds=60
Storage:R2:RetainDatabaseFallbackDays=30
```

Credenciais nunca entram em `appsettings.json`, logs, documentação, URL, query string ou histórico Git. O token deve ser rotacionado ao trocar de ambiente, suspeitar de exposição ou concluir uma migração que usou permissão temporária.

## Upload e leitura

1. A API limita o corpo antes da alocação e lê no máximo 5 MB para comprovantes ou 10 MB para anexos clínicos.
2. Nome, extensão, MIME declarado e assinatura real do arquivo devem corresponder.
3. PDF com conteúdo ativo, arquivo poliglota conhecido, EICAR e imagem estruturalmente inválida são recusados.
4. O antivírus precisa responder limpo; falha ou timeout significa rejeição fechada.
5. Somente depois da inspeção o objeto é gravado no R2, com chave opaca e metadado mínimo de SHA-256.
6. O banco registra provedor, chave, ETag, tamanho e hash. O nome original fica no banco, não na chave do objeto.
7. Na leitura, a autorização ocorre antes do acesso ao R2. Tamanho e SHA-256 são verificados novamente.
8. Indisponibilidade sem fallback retorna `503` sem revelar bucket, chave ou detalhes do provedor.

## Migração e rollback

O inventário inicial de 8 de setembro de 2026 encontrou 9 objetos no storage legado e 1 blob protegido no `viverappweb`. No inventário imediatamente anterior à cópia, o B2 retornou 12 objetos; a transferência foi interrompida até o proprietário ampliar explicitamente a autorização. Depois dessa autorização, os 12 objetos foram copiados e reconciliados. O banco legado foi consultado em transação somente leitura.

Para os blobs já pertencentes ao sistema web:

```powershell
dotnet run --project tools/ViverApp.StorageMigration -- stage-current
dotnet run --project tools/ViverApp.StorageMigration -- migrate-current
dotnet run --project tools/ViverApp.StorageMigration -- verify-current
```

Para os objetos do B2 legado, depois de mover a configuração de origem exclusivamente para user-secrets e receber autorização explícita para a transferência:

```powershell
dotnet run --project tools/ViverApp.StorageMigration -- migrate-legacy
dotnet run --project tools/ViverApp.StorageMigration -- verify-legacy
```

A ferramenta:

- recusa qualquer banco diferente de `viverappweb` e qualquer servidor diferente de MySQL 8.0.41;
- reserva uma chave opaca no banco antes do upload, permitindo retry idempotente;
- descriptografa somente em memória usando o key ring local já existente;
- confere SHA-256 e tamanho antes e depois do envio;
- promove cada linha para `r2` com controle de concorrência;
- preserva temporariamente o blob protegido para dual-read e rollback;
- não oferece comando de exclusão do blob antigo.

Quando o processo com acesso externo não compartilha a identidade DPAPI que criou o blob, `stage-current` gera um envelope temporário protegido com DPAPI `LocalMachine`. O conteúdo continua criptografado em disco, é aberto somente em memória pelo processo de envio e o envelope é excluído depois da confirmação do R2. A migração executada confirmou 1 de 1 objeto atual, sem pendências e sem envelopes restantes.

Na origem B2, a ferramenta possui apenas operações de listagem e leitura. Cada nome de origem é convertido por SHA-256 em uma chave determinística sob `legacy-quarantine/development`, o que evita PII na chave e torna o retry idempotente. O conteúdo passa por limite de 20 MB e verificação de tamanho/SHA-256 antes e depois do envio; a saída informa somente contagens. O conteúdo descriptografado existe apenas em memória durante a cópia, sem arquivo temporário em disco.

Os 12 objetos inventariados no B2 foram copiados para a quarentena privada e reconciliados por tamanho e SHA-256: 12 verificados, 0 pendentes. A verificação releu a origem e confirmou que os 12 originais continuam presentes. O B2 não foi desativado nem apagado. A remoção posterior depende da política de retenção/LGPD, da Fase 23 e de autorização específica.

## CDN e DNS

- `cdn.viveralmenara.com` está conectado somente ao bucket `viverappweb-production-public` pela integração nativa de domínio personalizado do R2. O registro CNAME e o certificado são gerenciados pela Cloudflare. O painel confirmou o domínio como ativo e o HTTPS respondeu corretamente com `404 Object not found` enquanto o bucket ainda estava vazio.
- A cópia pública não sensível da logo foi enviada como `logo.png` e validada por HTTPS em `https://cdn.viveralmenara.com/logo.png`, com dimensão 288×288. Nenhum documento ou upload de usuário foi publicado.
- Cache longo e imutável será aplicado apenas a ativos versionados. HTML, respostas autenticadas, uploads e qualquer conteúdo clínico usam `no-store`.
- CORS do bucket público, quando necessário, aceitará `GET` e `HEAD` somente de `https://viveralmenara.com` e `https://www.viveralmenara.com`.
- Hotlink protection/WAF deve restringir uso indevido sem bloquear navegadores legítimos do site.
- DNSSEC, TLS estrito com a origem, registros do site/API e regras finais de WAF pertencem à Fase 21, quando existir uma origem publicada verificável.

## Critério de encerramento

A fase foi encerrada com os quatro buckets criados, token privado de desenvolvimento restrito ao bucket correspondente, 1 objeto atual e 12 objetos legados reconciliados, bucket privado sem domínio personalizado nem `r2.dev`, e a logo não sensível entregue pelo domínio público. O storage anterior permanece intacto até decisão de retenção posterior.

## Evidências finais

- solution compilada com 0 erros e 0 avisos;
- 181 testes aprovados;
- MySQL 8.0.41, database `viverappweb` e migrations até `0021` verificados;
- objeto atual: 1 verificado, 0 pendentes;
- objetos B2: 12 verificados, 0 pendentes, origem preservada;
- retry da migração B2: 0 novos uploads e 12 objetos já presentes, comprovando idempotência;
- bucket privado: nenhum domínio personalizado e URL `r2.dev` desabilitada;
- CDN: `https://cdn.viveralmenara.com/logo.png` respondeu por HTTPS com a imagem 288×288;
- nenhuma credencial incluída no Git, nos logs ou nesta documentação.
