# Verificações da Fase 4

## Automatizadas

- build Release com warnings tratados como erro;
- testes de identidade para hash salgado/versionado, normalização de contato, rejeição de campos extras, configuração segura, return URL, cookies, PKCE, papéis e MFA;
- teste do contrato HTTP do SMSBarato sem colocar a chave na URL;
- testes de persistência contra MySQL 8.0.41 para as cinco migrations, 27 entidades DB-First, papel único e ausência de colunas de senha reversível;
- testes transversais anteriores de headers, CORS, rate limiting, honeypot, Data Protection e observabilidade;
- `dotnet format --verify-no-changes` e auditoria NuGet.

## Cenários de segurança cobertos pelo desenho

| Risco | Evidência |
|---|---|
| brute force | rate limit sensível, cinco falhas para lockout e cinco tentativas por código |
| enumeração | resposta genérica e request ID opaco para cadastro, login por código e recuperação |
| CSRF | filtro antiforgery global sobre mutações e cookies host-only |
| fixation/roubo de sessão | novo ID aleatório por autenticação, registro server-side e validação/revogação por requisição |
| redirect indevido | retorno Google derivado apenas de URL HTTPS fixa do servidor |
| privilege escalation | papel vem exclusivamente do banco; sessão MFA pendente perde todas as roles |
| vínculo Google indevido | provider subject único; e-mail coincidente exige sessão autenticada e vínculo explícito |
| repetição de código | consumo atômico condicionado a `consumed_at_utc IS NULL` |
| repetição de recovery code | atualização atômica condicionada a `used_at_utc IS NULL` |

## Verificações externas pendentes

O handshake Google usa credenciais OAuth Web e redirect URI mantidos somente em user-secrets; a configuração e a integração real são verificadas na Fase 5. Entregabilidade SMTP/SMS exige saldo, autorização de IP e configuração vigentes nos provedores. Esses testes externos não devem usar destinatários reais sem autorização explícita.
