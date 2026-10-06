# ViverApp Web

Aplicação web de uma clínica, construída em **.NET 10** com Blazor Web App, ASP.NET Core API e MySQL **8.0.41**. Este é o repositório independente da versão Web; o aplicativo MAUI não faz parte dele.

> **Estado:** o produto está em desenvolvimento e homologação. A preparação para Windows Server/IIS existe, mas a publicação pública e a ativação de integrações de produção ainda dependem dos portões operacionais e de segurança descritos no [roteiro](docs/ROADMAP.md) e no [guia de implantação](docs/fase-26/WINDOWS-SERVER-IIS.md).

## O que já existe

- Acessos separados para Paciente, Médico, Psicólogo, Gestor e Administrador; login por Google, senha ou código por e-mail/SMS quando o canal estiver habilitado, e MFA obrigatório para Administração.
- Agenda visual e agendamento, disponibilidade recorrente ou variável por data, serviços combinados da mesma categoria, atendimentos sem cobrança, fila de chegada e notificações.
- Cadastro de pacientes, Premium, prontuário eletrônico, laudos e documentos privados, respeitando as permissões de cada perfil.
- PagBank Checkout para pagamentos online; pagamentos presenciais, inclusive divididos entre formas de pagamento, descontos pontuais, caixa diário e reversões auditadas. Cobranças e estornos reais exigem configuração e autorização próprias.
- Videochamadas WebRTC com sinalização pela API, link temporário para convidado sem conta e até quatro participantes por sala.
- Armazenamento privado em Cloudflare R2, ativos públicos por CDN, páginas institucionais, política de privacidade, cookies e controles de SEO.
- Processamento assíncrono de e-mail, SMS e rotinas agendadas por hosted services na API, apoiado por outbox no MySQL.

## Estrutura

| Caminho | Responsabilidade |
| --- | --- |
| `src/ViverApp.Web` | Interface Blazor Web App com interatividade Server. |
| `src/ViverApp.Api` | API, regras de negócio, autenticação, vídeo e workers hospedados. |
| `src/ViverApp.Security` | Controles de segurança compartilhados. |
| `database/migrations` | Evolução versionada do schema MySQL. |
| `database/scaffold.ps1` | Regeneração DB-First das entidades e do DbContext após migrations. |
| `tools/ViverApp.Database` | Comandos de inspeção, aplicação e verificação das migrations locais. |
| `tests` | Testes de contrato, integração, segurança e jornadas. |
| `scripts` | Verificações de segurança, homologação e pacote Windows Server. |
| `docs` | Roteiro, planos de fase, decisões e runbooks. |

O EF Core é **DB-First**: a estrutura de `viverappweb` é a fonte dos modelos gerados. Não edite entidades ou o DbContext gerados manualmente. O banco legado `viverappmobile` é somente leitura e não é necessário para executar a aplicação Web.

## Desenvolvimento local

Pré-requisitos: SDK definido em [global.json](global.json) (.NET 10), MySQL Community Server **8.0.41** e PowerShell 7 para os scripts. O banco de desenvolvimento é `viverappweb`; nunca aponte os comandos abaixo para produção ou para o banco legado.

Configure fora do Git os user-secrets da API, sobretudo `ConnectionStrings:LocalConnection` e `Authentication:ChallengePepper`. Google OAuth, SMTP, SMSBarato, PagBank e R2 precisam de suas próprias credenciais somente quando suas jornadas forem habilitadas. Não coloque valores em `appsettings*.json`, no README ou em comandos compartilhados. A Web tem configuração local de `Backend:BaseUrl` em `appsettings.Development.json`.

Na raiz do repositório:

```powershell
dotnet restore ViverApp.slnx --locked-mode
dotnet run --project tools/ViverApp.Database -- status
dotnet run --project tools/ViverApp.Database -- apply
dotnet run --project tools/ViverApp.Database -- verify
dotnet build ViverApp.slnx --no-restore
```

Antes de executar `apply`, confira a conexão local, o nome do banco e as migrations pendentes em `status`; o comando altera o schema de `viverappweb`. Para iniciar a aplicação, use dois terminais:

```powershell
dotnet run --project src/ViverApp.Api --launch-profile https
dotnet run --project src/ViverApp.Web --launch-profile https
```

Os perfis de desenvolvimento usam `https://localhost:7176` para a API e `https://localhost:7110` para a Web. Se o certificado HTTPS local não funcionar, há uma opção de HTTP **restrita a Development**; não remova certificados existentes nem enfraqueça a configuração de produção.

## Verificação e entrega

```powershell
dotnet test ViverApp.slnx --no-restore -m:1
pwsh -NoProfile -File scripts/security-check.ps1
```

Os testes de integração e a verificação de migrations exigem o MySQL local real; a [CI](.github/workflows/quality.yml) executa apenas o subconjunto que não depende desse banco. Antes de qualquer release, resolva falhas dos portões locais e as pendências de homologação, sem tratar um build aprovado como autorização para produção.

O [empacotador Windows](scripts/package-windows-release.ps1) gera ZIPs separados da Web, API e migrations, além de manifesto com commit e SHA-256. O destino planejado é uma máquina física Windows Server com IIS e MySQL 8.0.41 na mesma máquina. Consulte o [runbook de implantação](docs/fase-26/WINDOWS-SERVER-IIS.md) antes de instalar ou atualizar; segredos, key rings, backup e migrations de produção ficam fora dos pacotes Git.

## Planejamento e regras de trabalho

O [ROADMAP](docs/ROADMAP.md) aponta para os planos detalhados das fases; as [decisões arquiteturais](docs/adr/README.md) registram as escolhas duráveis. Cada fase é trabalhada isoladamente em uma branch própria. `AGENTS.md`, `.local/`, `.config/` e `.codegraph/` são locais e ignorados pelo Git; quem preparar um novo ambiente deve receber as regras operacionais e os segredos por canal seguro, nunca por commit.
