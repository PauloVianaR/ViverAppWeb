# Configuração e operação da identidade

Todos os valores abaixo ficam em User Secrets no desenvolvimento e em secret store/variáveis protegidas na implantação. Nenhum valor deve entrar em `appsettings*.json`, logs, commits ou documentação.

| Chave | Finalidade |
|---|---|
| `ConnectionStrings:LocalConnection` | conexão exclusivamente com `viverappweb` |
| `Authentication:ChallengePepper` | Base64 de pelo menos 32 bytes para HMAC de códigos, destinos e metadados |
| `Authentication:Google:ClientId` | OAuth Client ID do tipo Web |
| `Authentication:Google:ClientSecret` | segredo OAuth Web |
| `Authentication:WebReturnUrl` | URL HTTPS fixa da página Web que recebe o resultado do Google |
| `Authentication:Passkeys:ServerDomain` | RP ID/domínio efetivo das passkeys |
| `Authentication:Delivery:Enabled` | habilita o consumidor da outbox; `true` por padrão |
| `Smtp:Host`, `Smtp:Port` | servidor e porta TLS do SMTP |
| `Smtp:User`, `Smtp:Password` | remetente/autenticação SMTP |
| `SmsBarato:BaseUrl`, `SmsBarato:ApiKey` | host HTTPS oficial e chave do SMSBarato |

O callback a cadastrar no Google é `https://<host-da-api>/signin-google`. As duas credenciais Google devem ser configuradas juntas. A API recusa URL de retorno sem HTTPS, URL com credenciais e host de SMSBarato diferente do oficial.

O switch de entrega existe para testes isolados e manutenção controlada. Ele não descarta mensagens: com o valor `false`, novos códigos continuam pendentes na outbox e expiram normalmente. Produção deve mantê-lo habilitado e monitorar retries/dead-letter.

## Outbox de identidade

O código existe em texto claro apenas na memória necessária para compor a mensagem. No desafio ele é persistido em HMAC; na outbox ele fica dentro de payload criptografado por Data Protection. O worker reclama um item com lock e lease no MySQL, entrega fora da transação e marca sucesso, retry exponencial com jitter ou dead-letter depois de cinco tentativas.

A semântica de SMTP/SMS é pelo menos uma vez: uma queda depois da aceitação do provedor e antes do commit local pode gerar duplicidade, nunca perda silenciosa. O código continua único, curto e expirável. Logs incluem apenas ID, canal e classe/código seguro do erro — nunca destinatário, código, senha ou chave.

## Passkeys e MFA

Passkeys são credenciais primárias e passwordless no ASP.NET Core 10; não são tratadas como o segundo fator nativo do Identity. Administradores sempre precisam concluir TOTP ou usar um recovery code após senha, código, Google ou passkey. A chave TOTP é protegida por Data Protection e não pode ser reconfigurada enquanto ativa; essa restrição evita desativação acidental do MFA em sessão existente.

## Data Protection

Em desenvolvimento Windows, as chaves ficam somente em `.local/` e são protegidas por DPAPI. Em produção é obrigatório configurar key ring persistente e certificado, conforme a Fase 3. Perder o key ring invalida cookies, chaves TOTP protegidas e mensagens pendentes da identidade.
