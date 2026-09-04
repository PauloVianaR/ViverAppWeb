# Fase 3 — Fundação de segurança, privacidade e observabilidade

**Estado:** implementada em 2 de setembro de 2026  
**Branch:** `codex/fase-03-seguranca-observabilidade`

## Resultado

A API e o Blazor receberam uma fundação transversal reutilizável antes de existir qualquer endpoint de negócio. A autenticação real continua deliberadamente fora desta fase: até a Fase 4, a API usa uma estratégia temporária que recusa toda identidade e uma política padrão que exige usuário autenticado. Somente os endpoints mínimos de saúde são anônimos.

Foram entregues:

- limites de conexão, cabeçalhos, corpo da requisição e tempo de leitura no Kestrel;
- HTTPS e HSTS fora de desenvolvimento;
- antiforgery com cookie `__Host-`, `Secure`, `HttpOnly` e `SameSite=Strict`;
- CORS por lista explícita, sem wildcard, credenciais apenas para origens aprovadas;
- CSP distinta para API e Web; o Blazor usa nonce criptográfico por resposta no import map e no script do framework;
- headers de framing, MIME, referrer, permissions e isolamento de origem;
- rate limiting global e políticas nomeadas para formulários públicos, operações sensíveis e escritas;
- timeout padrão de 30 segundos e política explícita de 2 minutos para operações justificadamente longas;
- Problem Details sem stack trace, detalhe interno ou URL da requisição, sempre com identificador de correlação;
- detector/filtro de honeypot reutilizável para os formulários públicos que surgirão nas próximas fases;
- logs JSON estruturados que não registram query string, corpo, IP, usuário, token ou dado clínico;
- health e readiness com resposta mínima; a API verifica conectividade com o MySQL sem revelar a causa ao cliente;
- métricas e traces OpenTelemetry, com exportação OTLP opcional e amostragem validada;
- key rings separados para API e Web: DPAPI em diretório local ignorado no Windows/Development e certificado obrigatório fora de Development;
- migration `0004` aplicada em `viverappweb`, tornando `audit_events` append-only por triggers de bloqueio de `UPDATE` e `DELETE`;
- seis testes automatizados da fundação de segurança, além dos seis testes de persistência existentes;
- verificador automatizado em `scripts/security-check.ps1`.

Não havia `.codegraph/` na raiz do repositório. A inspeção desta fase usou leitura direta, conforme permitido pelo `AGENTS.md`. Nenhum projeto legado e nenhuma pasta fora de `ViverAppWeb` foram alterados; a pasta obsoleta não foi consultada.

## Limites intencionais desta fase

- lockout progressivo e respostas contra enumeração só podem ser associados a contas reais na Fase 4;
- MFA, cookies de sessão, recuperação por e-mail/SMS e políticas por papel pertencem à Fase 4;
- validações específicas contra SSRF, path traversal, upload malicioso e mass assignment serão anexadas aos contratos que criarem essas superfícies;
- CAPTCHA adaptativo não foi adicionado sem evidência de abuso e sem um provedor aprovado; honeypot nunca é tratado como controle suficiente;
- alertas externos, WAF, DNS e configuração de produção pertencem à infraestrutura da Fase 21.

Esses itens não foram antecipados para respeitar a execução de uma fase por vez.

## Artefatos

- [controles implementados](SECURITY-BASELINE.md);
- [checklist automatizado e manual](SECURITY-CHECKLIST.md);
- [atualização do threat model](THREAT-MODEL-UPDATE.md);
- [operação, retenção e incidentes](OPERATIONS.md).

## Validação executada

- MySQL `8.0.41` e database `viverappweb` confirmados;
- migrations `0001` a `0004` registradas, sem pendências, e checksums aprovados;
- scaffold DB-First executado depois da migration `0004`;
- build sem warnings;
- 12 testes aprovados: 6 de persistência/MySQL e 6 de segurança;
- health/live e health/ready da API responderam `200` e somente `{"status":"Healthy"}`;
- página Blazor respondeu `200`, sem header `Server`, com dois nonces iguais ao nonce da CSP e sem `unsafe-inline`;
- nenhuma execução destrutiva de rollback, conforme decisão já registrada na Fase 2.
