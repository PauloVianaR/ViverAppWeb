# Checklist de segurança

## Verificação automatizada

Após restaurar as dependências, executar:

```powershell
./scripts/security-check.ps1
```

O script falha se encontrar segredo preenchido em `appsettings`, arquivo criptográfico sensível rastreado, warning/erro de build, teste reprovado, desvio de formatação, migration pendente/checksum divergente ou dependência conhecida como vulnerável. A auditoria de dependências exige acesso ao NuGet; somente em diagnóstico offline pode-se usar `-SkipDependencyAudit`, sem considerar a fase ou release aprovado.

## Checklist de mudança

- [ ] `.codegraph/` foi consultado primeiro, quando existente.
- [ ] A branch contém somente uma fase.
- [ ] Nenhum segredo, PII, dado clínico ou payload foi incluído em código, teste, log ou documentação.
- [ ] Todo endpoint de negócio exige autenticação e política/ownership apropriados.
- [ ] Toda exceção anônima está explícita e testada.
- [ ] DTOs limitam os campos aceitos e possuem validação de servidor.
- [ ] Limites de payload, paginação, timeout e rate limit correspondem ao risco.
- [ ] Operações de I/O propagam cancelamento.
- [ ] URLs, paths, uploads e conteúdo HTML têm defesa específica antes de serem aceitos.
- [ ] Logs novos usam campos permitidos e não incluem corpo/query string por conveniência.
- [ ] Mudança de schema possui migration SQL aplicada em `viverappweb` antes do scaffold DB-First.
- [ ] Build, testes, format, migration status/verify e auditoria de pacotes foram aprovados.

## Gate para produção

Além do script local, produção exigirá certificado do key ring, origem CORS real, endpoint OTLP HTTPS, TLS válido, proxy confiável configurado explicitamente, backups criptografados, restauração testada, WAF/CDN e alertas externos. Esses itens dependem do ambiente e serão fechados na Fase 21; valores vazios fazem a aplicação falhar com segurança onde aplicável.
