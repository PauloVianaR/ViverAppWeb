# Releases de produção com GitHub Actions

O repositório é público. Somente runners hospedados pelo GitHub compilam o código; **não registre o host IIS como self-hosted runner**. A implantação nesta máquina é manual, conforme a escolha do proprietário. Nenhum push, PR ou atualização de dependências executa SQL no banco de produção.

## Configuração no GitHub

1. Proteja a branch `main`: bloqueie force push e exclusão, exija pull request e o status `Quality gate / portable` antes de merge. Restrinja quem pode editar workflows e revisar alterações em `scripts/`, `.github/workflows/` e `database/migrations/`.
2. Em **Settings → Environments**, configure `production` para aceitar apenas `main`. Ative revisão obrigatória pelo proprietário. Não armazene senhas MySQL, chaves R2 nem configurações IIS no GitHub.
3. Em **Settings → General → Releases**, ative **immutable releases**. O instalador recusa releases mutáveis e arquivos sem digest SHA-256 do GitHub.
4. Em **Settings → Actions → General**, mantenha permissões padrão do `GITHUB_TOKEN` em leitura. O job `publish` solicita `contents: write` somente para criar a release.

## Criar uma release

Em **Actions → Production release → Run workflow**, selecione `main` e informe exatamente os IDs de migrations esperados no banco de produção, em ordem e separados por vírgula. Exemplo atual: `0050`. Deixe vazio se não houver migrations pendentes. Marque `rollback_compatible` apenas após revisar que os binários anteriores funcionam com o novo schema.

O job hospedado restaura dependências travadas, compila, confere formatação, executa os testes portáteis e gera API, Web e migrations com manifesto SHA-256. O ambiente `production` exige aprovação antes da publicação. A release é nomeada `viverapp-<SHA completo>` e inclui `deployment-plan.json`. Testes de integração MySQL não rodam nesse runner; são um portão separado no MySQL 8.0.41 persistente de homologação. Não interpretar um job verde como homologação clínica ou financeira.

## Instalar manualmente nesta máquina

Use **PowerShell como Administrador**, na conta Windows que criou a credencial DPAPI do migrador e o `mysql_config_editor` de backup. Primeiro baixe e confira a release imutável:

```powershell
cd C:\Viver\ViverAppWeb
$tag = 'viverapp-COLE_AQUI_O_SHA_COMPLETO_DA_RELEASE'
$package = (& .\scripts\Download-ProductionRelease.ps1 -Tag $tag | Select-Object -Last 1) -replace '^Release verificada: ', ''
& .\scripts\Install-ProductionRelease.ps1 -ReleaseDirectory $package
```

O pré-voo compara MySQL 8.0.41, `DATABASE() = viverappweb`, IDs e checksums de **todas** as migrations já aplicadas. Se o plano ou o histórico divergir, ele para antes de executar SQL. Revise o SQL pendente e os resultados do pré-voo. Para implantar:

```powershell
& .\scripts\Install-ProductionRelease.ps1 -ReleaseDirectory $package -Apply
```

O instalador cria um backup criptografado DPAPI e confere sua descriptografia, prepara diretórios versionados, executa cada migration pendente uma vez e só então troca os caminhos da API e da Web no IIS. Por fim, verifica `/health/ready` de ambos os sites e a página inicial por HTTPS público. Guarda `deployment-result.json` no diretório baixado. Se health falhar, volta os binários anteriores somente quando não houve migration ou quando a compatibilidade de rollback foi declarada na release; o schema nunca é revertido automaticamente.

O backup permanece nesta máquina por exceção expressa do proprietário. Ele **não** substitui uma cópia externa nem um teste de restauração. Se uma migration falhar depois de DDL parcial, pare e revise manualmente; não execute o instalador de novo por reflexo.

## Segurança operacional

- Nunca rode workflows de PR no host de produção. O repositório público permite contribuições não confiáveis; um self-hosted runner persistente nesse host exporia IIS, banco e segredos locais.
- Não inclua credenciais em variáveis de workflow, ZIPs, issues ou logs. O script lê somente a credencial cifrada já existente no host.
- O `workflow_dispatch` publica uma release, mas **não altera a produção**. A instalação requer a etapa local `-Apply`.
- Após a instalação, valide no navegador integrado os fluxos autenticados relevantes à versão, incluindo Admin com MFA, caixa e pagamentos em ambiente de teste. Health 200 não cobre essas jornadas.
