# Fase 2 — Banco DB-First, modelo de domínio e baseline de dados

**Estado:** concluída em 2 de setembro de 2026
**Branch:** `codex/fase-02-banco-db-first`

## Resultado

- repositório Web separado do repositório MAUI, com histórico próprio e branch principal `main`;
- MySQL Community Server 8.0.41 e database alvo `viverappweb` verificados antes das escritas;
- database legado `viverappmobile` inspecionado em transação somente leitura e sem alteração;
- snapshot exclusivamente estrutural do legado salvo em `database/reference/viverappmobile-schema.sql`;
- migrations SQL `0001`, `0002` e `0003` aplicadas e registradas com SHA-256 em `viverappweb`;
- 24 tabelas de aplicação criadas com `utf8mb4`, checks, FKs, índices, UTC e valores monetários `decimal(13,2)`;
- scaffold DB-First reproduzido com hashes idênticos pelo provider oficial `MySql.EntityFrameworkCore` 10.0.9 e ferramenta `dotnet-ef` 10.0.9;
- `ViverAppDbContext` registrado na API, que recusa outro database e outra versão de MySQL ao iniciar;
- testes de integração aprovados contra o MySQL real;
- credenciais existentes de PagBank, SMTP e SMSBarato mantidas apenas em user-secrets;
- nenhuma credencial de Azure, OneSignal ou Firebase foi migrada.

Não havia `.codegraph/` na raiz do repositório Web durante esta fase. Por isso, a exploração permitida usou leitura direta e busca textual, conforme `AGENTS.md`. A pasta proibida `ViverAppMobile[obsolete]` não foi consultada.

## Decisões de produto refletidas no schema

- cada conta possui obrigatoriamente um único `role_code`: `patient`, `doctor`, `manager` ou `administrator`;
- o produto não possui tenant, associação multiclínica nem seletor de clínica;
- `clinic.singleton_id = 1` limita o sistema a uma única clínica; o registro nasce vazio e será preenchido no fluxo de configuração da Fase 5;
- médicos e gestores pertencem implicitamente à clínica única;
- contas podem ter e-mail, telefone ou ambos;
- senha local é representada apenas por `password_hash`; não existe coluna para senha reversível;
- Google é o único provedor externo previsto;
- desafios de login, confirmação e recuperação aceitam `email` ou `sms` e armazenam somente o hash do código e do destino;
- a outbox aceita e-mail e SMS; Firebase e push não existem no novo schema.

Os detalhes das tabelas e invariantes estão em [MODELO-FISICO.md](MODELO-FISICO.md). A estratégia futura, sem copiar cegamente dados do legado, está em [PLANO-MIGRACAO-DADOS.md](PLANO-MIGRACAO-DADOS.md).

## Inventário do legado

A consulta a `information_schema`, sem leitura de registros clínicos, encontrou:

| Item | Quantidade |
|---|---:|
| Tabelas base | 20 |
| Colunas | 149 |
| Chaves estrangeiras | 17 |
| Views | 0 |
| Triggers | 0 |
| Rotinas | 0 |
| Eventos agendados | 1 |

O snapshot não contém dados nem credenciais. O único evento legado foi exportado sem `DEFINER`, desabilitado e sem referência qualificada ao database original. Ele só pode ser restaurado em ambiente isolado e descartável.

## Migrations e scaffold

| Migration | Estado em `viverappweb` | Finalidade |
|---|---|---|
| `0001__baseline.sql` | aplicada | baseline de 24 tabelas e seeds de roles/configuração |
| `0002__remove_ambiguous_boolean_defaults.sql` | aplicada | remove defaults que tornavam flags obrigatórias anuláveis no scaffold |
| `0003__restrict_role_codes.sql` | aplicada | restringe o catálogo aos quatro papéis aprovados |

Fluxo obrigatório:

```powershell
dotnet run --project tools/ViverApp.Database -- status
dotnet run --project tools/ViverApp.Database -- apply
dotnet run --project tools/ViverApp.Database -- verify
./database/scaffold.ps1
dotnet test ViverApp.slnx --configuration Release
```

O runner confere o database, a versão exata do servidor, migrations pendentes e checksums. O script de scaffold regenera somente `Infrastructure/Persistence/Generated`, usa a connection string nomeada e exclui `__schema_migrations` das entidades. Consulte [OPERACAO-DB-FIRST.md](OPERACAO-DB-FIRST.md) antes de alterar o schema.

## Validação realizada

- restore e build Release: aprovados, sem avisos;
- testes automatizados: 6 aprovados, sendo 3 contra o MySQL real e 3 de simetria dos scripts;
- migrations: `0001`, `0002` e `0003` aplicadas, nenhuma pendente;
- contratos testados: MySQL 8.0.41, alvo `viverappweb`, 24 entidades, papel obrigatório único, clínica singleton, concurrency tokens, quatro roles de referência e ausência de Firebase/push;
- scripts de rollback: correspondência e simetria validadas automaticamente; execução destrutiva dispensada pelo proprietário.

## Decisão sobre rollback

O rollback da baseline removeria todas as tabelas e dados de `viverappweb`. O proprietário decidiu que esse ensaio destrutivo não é necessário. Os arquivos `.down.sql` permanecem versionados e cobertos por testes estruturais, mas sua execução não é critério de conclusão. Nenhuma Fase 3 foi antecipada nesta branch.
