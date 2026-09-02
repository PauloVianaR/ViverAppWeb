# ViverApp Web

Este diretório é um repositório Git independente do repositório legado/MAUI. O código MAUI não deve ser incorporado aqui; ele permanece apenas como referência externa somente leitura nas fases que autorizarem consulta.

Reimplementação web do ViverApp em .NET 10, composta inicialmente por:

- `ViverApp.Web`: Blazor Web App com interatividade Server;
- `ViverApp.Api`: ASP.NET Core Web API;
- `docs/ROADMAP.md`: plano completo e incremental da reimplementação;
- `docs/fase-01/`: descoberta funcional, riscos, arquitetura e backlog;
- `docs/adr/`: decisões arquiteturais duráveis;
- `AGENTS.md`: regras obrigatórias para agentes de IA.

As Fases 0 e 1 criaram a fundação compilável e documentaram o legado. Ainda não há domínio, persistência, autenticação, integrações, workers ou regras de negócio implementados.

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
- `PagBank:TokenProduction`.

As connection strings de Azure não foram copiadas. Nenhum valor secreto deve ser incluído no repositório, em exemplos, logs, testes ou documentação.

O banco legado `viverappmobile` será somente leitura. A aplicação web usará um banco novo chamado `viverappweb`, governado por migrations SQL e consumido pelo EF Core em abordagem DB-First. A criação desse banco pertence à Fase 2 e não foi antecipada nesta fundação.

Para conferir somente os nomes configurados, sem compartilhar valores:

```powershell
dotnet user-secrets list --project src/ViverApp.Api/ViverApp.Api.csproj
```

## Estado atual

A Fase 1 está documentada no [índice de descoberta](docs/fase-01/README.md). O próximo passo possível é revisar e aprovar essas decisões. A Fase 2 não deve começar na mesma branch nem sem uma solicitação explícita.
