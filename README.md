# ViverApp Web

Este diretório é um repositório Git independente do repositório legado/MAUI. O código MAUI não deve ser incorporado aqui; ele permanece apenas como referência externa somente leitura nas fases que autorizarem consulta.

Reimplementação web do ViverApp em .NET 10, composta inicialmente por:

- `ViverApp.Web`: Blazor Web App com interatividade Server;
- `ViverApp.Api`: ASP.NET Core Web API;
- `docs/ROADMAP.md`: plano completo e incremental da reimplementação;
- `docs/fase-01/` a `docs/fase-04/`: descoberta, persistência, segurança e identidade;
- `docs/adr/`: decisões arquiteturais duráveis;
- `AGENTS.md`: regras obrigatórias para agentes de IA.

As Fases 0 a 3 estão integradas à `main`. A Fase 4 está concluída e com integração autorizada, acrescentando identidade real, login por senha/e-mail/SMS/Google/passkey, MFA TOTP, sessões revogáveis e entrega dos códigos de autenticação por SMTP/SMSBarato. A configuração externa e a validação real do Google foram transferidas para a Fase 5 por decisão do proprietário. Endpoints de negócio e a interface visual de acesso ainda não fazem parte desta etapa.

## Pré-requisitos

- .NET SDK 10.0.400 ou feature band compatível;
- MySQL Community Server **8.0.41** nas fases que utilizarem banco de dados;
- HTTPS local configurado para os projetos web (`dotnet dev-certs https --trust`, executado pelo proprietário quando necessário).

## Executar a base

```powershell
dotnet restore ViverApp.slnx
dotnet build ViverApp.slnx --no-restore
dotnet run --project src/ViverApp.Api
dotnet run --project src/ViverApp.Web
```

## Segredos de desenvolvimento

O projeto `ViverApp.Api` possui um `UserSecretsId`. As credenciais legadas necessárias e o novo segredo da identidade ficam somente no armazenamento local do .NET:

- `ConnectionStrings:LocalConnection`;
- `PagBank:SandboxUrl`;
- `PagBank:TokenSandbox`;
- `PagBank:ProductionUrl`;
- `PagBank:TokenProduction`;
- `SmsBarato:ApiKey` e `SmsBarato:BaseUrl`;
- `Smtp:Host`, `Smtp:Port`, `Smtp:User` e `Smtp:Password`.
- `Authentication:ChallengePepper`.

As credenciais `Authentication:Google:ClientId` e `Authentication:Google:ClientSecret` deverão ser adicionadas depois de criar um cliente OAuth Web no Google; elas não possuem fallback no repositório.

As connection strings de Azure não foram copiadas. Nenhum valor secreto deve ser incluído no repositório, em exemplos, logs, testes ou documentação.

O banco legado `viverappmobile` é somente leitura. A aplicação web usa o banco novo `viverappweb`, criado na Fase 2 e governado por migrations SQL antes do scaffold DB-First. As migrations `0001` a `0005` estão aplicadas e o modelo EF foi gerado exclusivamente desse schema.

Para conferir somente os nomes configurados, sem compartilhar valores:

```powershell
dotnet user-secrets list --project src/ViverApp.Api/ViverApp.Api.csproj |
    ForEach-Object { ($_ -split ' = ', 2)[0] } |
    Sort-Object
```

## Estado atual

As Fases 1 a 3 estão concluídas e documentadas nos respectivos índices: [descoberta e arquitetura](docs/fase-01/README.md), [persistência DB-First](docs/fase-02/README.md) e [segurança e observabilidade](docs/fase-03/README.md). A implementação ainda não integrada da Fase 4 está em [identidade e autorização](docs/fase-04/README.md).
