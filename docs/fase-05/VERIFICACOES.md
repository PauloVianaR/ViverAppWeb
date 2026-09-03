# Verificações da Fase 5

Execute na raiz de `ViverAppWeb`:

```powershell
dotnet tools/ViverApp.Database/bin/Release/net10.0/ViverApp.Database.dll status
dotnet tools/ViverApp.Database/bin/Release/net10.0/ViverApp.Database.dll verify
./database/scaffold.ps1 -NoBuild
dotnet build ViverApp.slnx -c Release --no-restore
dotnet test ViverApp.slnx -c Release --no-restore
dotnet format ViverApp.slnx --no-restore --verify-no-changes
dotnet list ViverApp.slnx package --vulnerable --include-transitive
git diff --check
```

Cobertura específica desta fase:

- schema e migration `0006` verificados em MySQL 8.0.41 real;
- scaffold com 28 entidades e concorrência configurada;
- consultas anônimas recusadas pela API;
- policy de gestão restrita a gestor e administrador;
- JSON desconhecido recusado e DTOs desacoplados das entidades EF;
- configuração Google validada como conjunto indivisível, callback fixo e PKCE habilitado;
- início real do OAuth direcionado ao Google com retorno para `/signin-google`;
- CRUD de especialidade exercitado em transação real com criação, update versionado, desativação e auditoria;
- regressão completa das fases anteriores, totalizando 49 testes aprovados e nenhum ignorado;
- solution compilada sem erros ou avisos e formatação validada;
- nenhuma dependência vulnerável identificada nas fontes NuGet atuais;
- navegação, abas e layout inspecionados em desktop, tablet e celular.
