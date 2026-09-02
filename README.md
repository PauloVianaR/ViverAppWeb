# ViverApp Web

Este diretório é um repositório Git independente do repositório legado/MAUI. O código MAUI não deve ser incorporado aqui; ele permanece apenas como referência externa somente leitura nas fases que autorizarem consulta.

Reimplementação web do ViverApp em .NET 10, composta inicialmente por:

- `ViverApp.Web`: Blazor Web App com interatividade Server;
- `ViverApp.Api`: ASP.NET Core Web API;
- `docs/ROADMAP.md`: plano completo e incremental da reimplementação;
- `docs/fase-01/`, `docs/fase-02/` e `docs/fase-03/`: descoberta, persistência e baseline de segurança;
- `docs/adr/`: decisões arquiteturais duráveis;
- `AGENTS.md`: regras obrigatórias para agentes de IA.

As Fases 0 a 3 criaram a fundação compilável, o banco DB-First e os controles transversais de segurança/observabilidade. Ainda não há autenticação real, endpoints de negócio, integrações ativas, workers ou regras de negócio implementados.

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

O projeto `ViverApp.Api` possui um `UserSecretsId`. Foram transferidas para o armazenamento local do .NET apenas estas chaves do legado:

- `ConnectionStrings:LocalConnection`;
- `PagBank:SandboxUrl`;
- `PagBank:TokenSandbox`;
- `PagBank:ProductionUrl`;
- `PagBank:TokenProduction`;
- `SmsBarato:ApiKey` e `SmsBarato:BaseUrl`;
- `Smtp:Host`, `Smtp:Port`, `Smtp:User` e `Smtp:Password`.

As connection strings de Azure não foram copiadas. Nenhum valor secreto deve ser incluído no repositório, em exemplos, logs, testes ou documentação.

O banco legado `viverappmobile` é somente leitura. A aplicação web usa o banco novo `viverappweb`, criado na Fase 2 e governado por migrations SQL antes do scaffold DB-First. As migrations `0001` a `0004` estão aplicadas e o modelo EF foi gerado exclusivamente desse schema.

Para conferir somente os nomes configurados, sem compartilhar valores:

```powershell
dotnet user-secrets list --project src/ViverApp.Api/ViverApp.Api.csproj
```

## Estado atual

As Fases 1 a 3 estão concluídas e documentadas nos respectivos índices: [descoberta e arquitetura](docs/fase-01/README.md), [persistência DB-First](docs/fase-02/README.md) e [segurança e observabilidade](docs/fase-03/README.md).
