# Migrations SQL

O schema `viverappweb` é governado por arquivos imutáveis no formato `NNNN__descricao.sql`, aplicados em ordem pela ferramenta `ViverApp.Database`.

Regras:

- confirmar MySQL 8.0.41 e `SELECT DATABASE() = 'viverappweb'` antes de escrever;
- revisar o SQL e o rollback antes da aplicação;
- nunca alterar uma migration já registrada em `__schema_migrations`;
- aplicar todas as migrations pendentes antes de executar o scaffold DB-First;
- não incluir segredo, dados reais nem comandos contra `viverappmobile`.

Depois da aplicação, execute `database/scaffold.ps1`. O script regenera somente `Infrastructure/Persistence/Generated`, usa a connection string nomeada do user-secrets e exclui `__schema_migrations` do modelo da aplicação.
