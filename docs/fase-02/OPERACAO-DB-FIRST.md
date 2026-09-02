# Operação DB-First

## Pré-condições

- MySQL Community Server exatamente 8.0.41;
- `ConnectionStrings:LocalConnection` em user-secrets apontando para `viverappweb`;
- branch da fase ativa e árvore de trabalho revisada;
- `.codegraph/` consultado primeiro quando existir;
- backup verificado e autorização explícita antes de qualquer operação destrutiva.

## Alterar o schema

1. crie a próxima migration incremental em `database/migrations`, sem editar arquivos já aplicados;
2. crie o rollback correspondente em `database/rollbacks` e classifique o risco de perda de dados;
3. revise o alvo, SQL, índices, constraints, locks e transformação de dados;
4. liste o estado antes da aplicação;
5. aplique todas as migrations pendentes;
6. verifique checksums e ausência de pendências;
7. regenere o scaffold;
8. mantenha customizações apenas em arquivos parciais fora de `Generated`;
9. execute build, testes de integração e varreduras.

```powershell
dotnet tool restore
dotnet run --project tools/ViverApp.Database -- status
dotnet run --project tools/ViverApp.Database -- apply
dotnet run --project tools/ViverApp.Database -- verify
./database/scaffold.ps1
dotnet build ViverApp.slnx --configuration Release --no-restore
dotnet test ViverApp.slnx --configuration Release --no-build
dotnet format ViverApp.slnx --verify-no-changes --no-restore
```

O runner aborta se o servidor não for 8.0.41, se o alvo não for `viverappweb`, se faltar arquivo local para uma migration registrada ou se o checksum tiver mudado.

O scaffold é produzido primeiro em uma pasta temporária validada. O modelo anterior só é substituído depois que contexto e todas as 24 entidades forem gerados com sucesso. Assim, falhas de conexão, build ou ferramenta não apagam uma geração válida.

O provider oficial gera propriedades `DbSet` sem inicializador mesmo com nullable reference types habilitado. A supressão de `CS8618` fica limitada por `.editorconfig` ao diretório `Generated`; ela não vale para código autoral, e o EF inicializa essas propriedades em runtime.

## Rollback

Os arquivos `.down.sql` são deliberadamente separados do comando de aplicação para impedir execução acidental. Antes de usá-los:

1. obtenha autorização explícita para a operação destrutiva;
2. gere e valide um backup restaurável de `viverappweb`;
3. confira o database selecionado e a versão do servidor;
4. execute rollbacks em ordem inversa;
5. remova os respectivos registros de `__schema_migrations` somente após sucesso;
6. valide o estado e, no ensaio, reaplique todas as migrations e regenere o scaffold.

O rollback da `0001` apaga todo o schema de aplicação. Ele nunca deve ser executado no legado nem automatizado sem as salvaguardas acima.
