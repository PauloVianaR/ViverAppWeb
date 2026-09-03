# Verificações da Fase 6

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

Cobertura específica:

- logo, favicon, idioma, skip link, landmarks e navegação principal no HTML SSR;
- shells de paciente, médico, gestor e administrador;
- catálogo dos estados de vazio, offline, sessão expirada e carregamento;
- página inexistente com recuperação localizada;
- resolução determinística de perfil visual por rota, sem efeito de autorização;
- contraste AA das combinações usadas para texto normal;
- inspeção visual em 360 × 800, 768 × 1024, 1366 × 768, 1920 × 1080 e 640 × 360 CSS px para o cenário de zoom;
- ausência de rolagem horizontal nos breakpoints inspecionados.
