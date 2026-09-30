# Fase 26 — Windows Server, IIS e Cloudflare

## Estado e decisão

O proprietário escolheu uma **máquina física Windows Server**, com MySQL 8.0.41 na mesma máquina, mas informou que ela ainda não está preparada. Esta entrega prepara artefatos e verificações locais; **nenhum serviço público, DNS, firewall, certificado ou banco de produção foi alterado**. `viverappweb_homolog` continua no MySQL local de desenvolvimento, sem dados reais. O host físico não deve ser usado como runner de pull requests nem receber um banco de teste descartável.

O desenho inicial usa dois sites e dois application pools IIS isolados: `viveralmenara.com` para Blazor e `api.viveralmenara.com` para a API. `www` será redirecionado ao canônico somente após validação do site. O IIS recebe HTTPS da Cloudflare com certificado de origem válido; em caso de origem pública, exigir **Full (strict)**, proteção da origem por firewall e Authenticated Origin Pulls configurado e testado. Cloudflare Tunnel pode substituir a origem pública, mas exige um desenho explícito de TLS/headers e validação de WebSocket antes da troca. Não usar Flexible, nem aceitar `X-Forwarded-*` de qualquer cliente, nem configurar `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` genericamente.

## Pacote reproduzível

Em checkout limpo, com PowerShell 7 e SDK do `global.json`:

```powershell
pwsh -NoProfile -File scripts/security-check.ps1
pwsh -NoProfile -File scripts/package-windows-release.ps1
pwsh -NoProfile -File scripts/verify-windows-release.ps1 -ReleaseDirectory <caminho da release>
```

O pacote ignorado em `artifacts/release/` contém ZIPs separados da API, Web e migrations/rollbacks, mais `manifest.json` com commit, tamanho e SHA-256. `-AllowDirty` existe só para ensaio local; o verificador de entrega recusa pacote sujo. Não inserir segredos nos ZIPs ou no Git. `security-check.ps1` executa a suíte completa **somente** contra o MySQL local 8.0.41 `viverappweb` do operador; nunca apontá-lo para produção. A CI em `.github/workflows/quality.yml` executa o subconjunto sem banco e declara explicitamente que isso não substitui a suíte local de integração.

Antes de instalar no servidor, executar `scripts/test-windows-server-prerequisites.ps1` nele. O script verifica apenas Windows Server, IIS, ANCM V2/.NET Hosting Bundle, runtime ASP.NET Core 10 e WebSocket; não autoriza publicação. Transferir o pacote por canal autenticado, conferir o manifesto no servidor e manter a identidade da release anterior para retorno.

## Pré-requisitos para o servidor físico

1. Inventariar versão do Windows Server, CPU/RAM/discos, IP público/roteador, UPS, acesso remoto administrativo, janela de manutenção e responsável de plantão. Restringir RDP/VPN; não disponibilizar MySQL na internet.
2. Instalar IIS com WebSocket Protocol e .NET 10 Hosting Bundle suportado. Criar dois pools com identidades distintas, sem privilégios administrativos, perfis carregados e ACLs mínimas. Desabilitar Windows Authentication para os sites públicos. O site Web não deve ler configuração, key ring ou documentos da API.
3. Instalar MySQL **8.0.41** e criar apenas o banco persistente de produção `viverappweb` com conta de runtime de privilégio mínimo. Separar credencial de migration/backup; exigir backup criptografado, retenção acordada, cópia fora do servidor, teste de restauração isolado e monitoramento de espaço/consistência. Não copiar os dados alfa do desenvolvimento sem a Fase 28.
4. Provisionar cofre de segredos do host ou mecanismo equivalente com ACL restrita. `ConnectionStrings:LocalConnection` deve apontar a `viverappweb`/8.0.41; `Backend:BaseUrl` da Web deve ser `https://api.viveralmenara.com/`; `AllowedHosts` de cada processo deve ser seu hostname exato; CORS da API deve aceitar apenas `https://viveralmenara.com` (e `www` apenas se realmente hospedado). Completar Google OAuth, SMTP/SMS, R2 privado, PagBank e TURN somente após os respectivos ensaios e aprovação de ativação. Não publicar `.env`, user-secrets ou arquivos de configuração com senhas.
5. Provisionar caminhos persistentes de Data Protection separados para API e Web, fora dos diretórios de publicação, com ACL por pool; usar certificado de proteção e senha no cofre. Preservar o anel em troca de release e backup. Nunca copiar ou remover as chaves DPAPI do desenvolvimento. Validar sessão e MFA após reinício e restauração antes de abrir ao público.
6. Provisionar Microsoft Defender atualizado e um diretório temporário privado fora da publicação, com ACL exclusiva para a API. Configurar `Security:MalwareScan:ExecutablePath` (caminho absoluto de `MpCmdRun.exe`) e `Security:MalwareScan:WorkDirectory` (diretório absoluto existente). A API recusa partida em produção se faltarem; arquivos são rejeitados se o scanner falhar/expirar. Confirmar sob a identidade real do pool, com arquivos de teste não sensíveis e EICAR, antes de habilitar uploads. Não elevar o pool a administrador para contornar uma falha de scanner.
7. Instalar certificado TLS de origem e política de renovação/alerta. Configurar Cloudflare somente após HTTPS direto na origem, health checks, AOP/firewall ou Tunnel, WebSocket, Google callback e cache sem dados sensíveis terem sido conferidos. Validar DNSSEC e regras WAF/rate/bot disponíveis no plano real, sem pressupor funcionalidades pagas. `cdn.viveralmenara.com` já serve apenas ativos públicos do bucket apropriado.

## Ordem segura de publicação

1. Aprovar backup restaurável e a lista exata de migrations pendentes, com checksum. Aplicá-las **uma vez**, em janela controlada, com credencial separada e revisão humana; não rodar migration automaticamente em cada instância nem executar rollback SQL sem avaliar dados novos. O runner atual foi criado para o banco local de desenvolvimento e **não é** uma ferramenta autorizada de migration de produção.
2. Verificar ZIPs/commit no servidor. Extrair em diretórios imutáveis de release, não sobrepor a versão em uso. Configurar secrets fora desses diretórios; não deixar `appsettings.Development.json` ativo nem permitir `Development` publicamente.
3. Apontar os pools/sites em janela controlada. Conferir `/health/live` e `/health/ready` da API, `/health/ready` da Web, headers/cookies, login de conta de ensaio autorizada, MFA, SignalR/WebSocket, anexo de teste, e ausência de redirecionamento TLS em loop. Manter o tráfego público fechado até isso passar.
4. Liberar canário de baixo volume e observar erros 4xx/5xx, p95/p99, filas, notificações, pagamento Sandbox e eventos de segurança. Não ativar checkout de produção ou entregas externas reais apenas porque a página respondeu 200.
5. Só então aprovar e registrar os DNS públicos `viveralmenara.com`, `www` e `api` na Cloudflare e a indexação pública. Conferir certificado/hostname, rota direta bloqueada, cache `no-store` em páginas privadas, WAF, regras de rate e bot, DNSSEC e alertas. Documentar valores antigos para retorno.

## Retorno e recuperação

Se health ou jornada crítica falhar, retirar tráfego e voltar os dois sites aos diretórios da release anterior. Preservar banco, auditoria append-only, outbox, key rings, comprovantes, pagamentos e logs. Antes de iniciar a versão anterior, confirmar que as migrations recentes são compatíveis; se não forem, parar e executar o plano de incidente, sem apagar colunas ou dados. Em perda de banco, restaurar backup criptografado **em instância isolada**, verificar integridade e tempo de recuperação antes de qualquer troca. Reconciliar PagBank/outbox por chaves idempotentes após recuperação, sem reenvio cego. Registrar horário, commit, operador, verificações e decisão.

## Portões ainda abertos

- Máquina física, IIS, cofre, certificado, IP/rota de origem, firewall e acesso operacional não fornecidos; nenhum deploy/rollback/restore real foi ensaiado.
- A CI não está ativa porque o repositório Web não possui remoto Git configurado. O job hospedado não usa banco descartável, portanto os testes MySQL completos e migrations continuam no portão local até existir runner de homologação persistente e isolado, sem acesso ao banco de produção.
- Assinatura/atestado de artefato, backup/restore real, monitoramento externo, Cloudflare Full (strict)/DNSSEC/WAF e scanner sob a identidade IIS exigem infraestrutura, responsáveis e validação posterior.
- Fase 25 foi aceita como marco pelo proprietário, mas seus portões de segurança/carga externos permanecem em `.local/PENDENCIAS.md` e devem bloquear ativação pública se ainda houver risco crítico/alto.

## Fontes técnicas de referência

- [Hospedagem ASP.NET Core no IIS](https://learn.microsoft.com/en-us/aspnet/core/tutorials/publish-to-iis?view=aspnetcore-10.0)
- [WebSocket no IIS](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0)
- [Proteção do key ring](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [Cloudflare Full (strict)](https://developers.cloudflare.com/ssl/origin-configuration/ssl-modes/full-strict/) e [Authenticated Origin Pulls](https://developers.cloudflare.com/ssl/origin-configuration/authenticated-origin-pull/)
