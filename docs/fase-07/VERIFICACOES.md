# Verificações da Fase 7

Execute na raiz de `ViverAppWeb`:

```powershell
dotnet tools/ViverApp.Database/bin/Release/net10.0/ViverApp.Database.dll apply
dotnet tools/ViverApp.Database/bin/Release/net10.0/ViverApp.Database.dll status
dotnet tools/ViverApp.Database/bin/Release/net10.0/ViverApp.Database.dll verify
dotnet build ViverApp.slnx -c Release --no-restore
dotnet test ViverApp.slnx -c Release --no-build --no-restore
dotnet format ViverApp.slnx --no-restore --verify-no-changes
dotnet list ViverApp.slnx package --vulnerable --include-transitive
git diff --check
```

O teste de concorrência usa somente `viverappweb`, cria registros com domínio reservado `example.test`, executa duas transações reais e remove os dados funcionais no bloco de limpeza. Eventos de auditoria, por definição append-only, nunca são apagados pelo teste.
