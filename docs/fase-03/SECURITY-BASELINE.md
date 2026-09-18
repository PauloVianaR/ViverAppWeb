# Controles da fundação de segurança

## Fronteira HTTP

| Controle | API | Blazor Web |
|---|---|---|
| Limite de corpo | 1 MiB por padrão | 1 MiB por padrão |
| Headers | 64 headers, 32 KiB total | 64 headers, 32 KiB total |
| Conexões | 500 HTTP, 100 atualizadas | 500 HTTP, 100 atualizadas |
| Rate limit global | 120/min por partição | 300/min por partição |
| Timeout | 30 s; política nomeada de 2 min | timeout de conexão do Kestrel |
| CORS | lista explícita do cliente Web | não habilitado |
| CSP | `default-src 'none'` | nonce criptográfico por resposta |
| Cache | `no-store` na API e health | política dos recursos estáticos |
| Autorização | nega por padrão; health explícito anônimo | identidade será integrada na Fase 4 |

A partição do rate limit usa o claim `sub` quando autenticado e o endereço remoto nos demais casos. O endereço não é escrito nos logs. As políticas nomeadas são:

- `public-form`: 8 requisições/minuto;
- `sensitive`: 10 requisições/5 minutos;
- `write`: 30 requisições/minuto.

Endpoints futuros devem escolher a política adequada e podem apertar os limites, nunca removê-los sem justificativa documentada.

## Respostas e logs

- erros HTTP usam Problem Details e um `correlationId` seguro;
- identificadores fornecidos pelo cliente só são aceitos no formato restrito de 16 a 64 caracteres;
- query strings, bodies, IPs, nomes, e-mails, telefones, tokens e documentos não entram no log de acesso próprio;
- health retorna somente `Healthy`, `Degraded` ou `Unhealthy`, sem nome de host, banco, exceção ou tempo interno;
- os logs são JSON UTC e traces de health não são coletados;
- OTLP é opcional; em produção, o endpoint deve ser HTTPS e não pode conter credenciais na URL.

## Proteção de chaves

Em Windows/Development, cada processo grava seu key ring em `.local/data-protection/{api|web}`, fora do Git e protegido com DPAPI do usuário. Isso evita compartilhar o key ring padrão do perfil e não apaga nem altera chaves externas.

Fora de Development, a inicialização falha se não existirem estas configurações secretas:

- `Security:DataProtectionKeysPath`;
- `Security:DataProtectionCertificatePath`;
- `Security:DataProtectionCertificatePassword`.

O certificado e a senha não podem ser versionados. API e Web usam nomes de aplicação distintos para impedir que um processo desproteja payloads do outro.

## Auditoria append-only

A migration `0004__protect_audit_events.sql` adiciona triggers que recusam alterações e exclusões em `audit_events`. A aplicação só poderá acrescentar eventos. Isso reduz adulteração acidental ou feita pela credencial normal da aplicação; não substitui backup imutável, exportação para destino independente nem controle de acesso administrativo ao MySQL.

## Controles obrigatórios para código futuro

- DTOs de entrada com campos explícitos; nunca bind direto em entidades EF;
- validação no servidor e encoding contextual na saída;
- URLs externas por allowlist, resolução segura e bloqueio de redes privadas antes de qualquer fetch;
- nomes de arquivo gerados pelo servidor, canonicalização de path e armazenamento fora da raiz pública;
- queries parametrizadas pelo EF/driver, sem SQL construído com entrada;
- `CancellationToken` em I/O;
- autenticação/autorização por política e ownership na API;
- honeypot apenas nos formulários públicos, combinado com rate limit e proteção adaptativa.
