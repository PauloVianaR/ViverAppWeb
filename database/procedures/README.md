# Procedures operacionais preparadas — não instaladas nem executadas

Estes arquivos **não são migrations**. Ficam fora de `database/migrations` porque o proprietário pediu expressamente que a limpeza não seja executada durante o desenvolvimento. Instalar uma procedure também altera o schema; fazê-lo futuramente requer janela de manutenção e autorização específica. Nunca apontar para `viverappmobile`.

## Corte limpo para o cliente

[`prepare_clean_client_database.sql`](prepare_clean_client_database.sql) prepara um `viverappweb` **novo**, no servidor de destino, e importa de uma cópia arquivada `viverappweb_alpha` apenas:

- a conta administradora ativa, remapeada para ID 1, seu hash de senha, vínculo Google, TOTP/códigos de recuperação, consentimento, endereço e preferências;
- dados cadastrais/horários/feriados da clínica, catálogo de especialidades, planos Premium e `application_settings`.

Não importa outros acessos, pacientes, profissionais, tipos de atendimento, vínculos, agenda, prontuários, documentos, pagamentos, caixa, auditoria, sessões, notificações, outbox, jobs nem passkeys. IDs das entidades operacionais começam novamente em 1; o número do primeiro atendimento permanece 100. A origem alfa continua intacta e arquivada, inclusive seus registros append-only. O procedimento rejeita destino com dados operacionais e migrations divergentes.

Antes da futura instalação: backup verificado dos dois schemas; restaurar a cópia alfa sob o nome `viverappweb_alpha`; aplicar **todas** as migrations no novo `viverappweb` MySQL 8.0.41; revisar o script frente à versão corrente do schema; manter a aplicação e workers parados; conferir segredos e URLs de produção. O TOTP do administrador está protegido por ASP.NET Data Protection: o anel de chaves correspondente precisa ser migrado por canal seguro, ou o proprietário deve aprovar um fluxo separado de recadastro de MFA. Os códigos de recuperação existentes também dependem do mesmo `Authentication:ChallengePepper`; se ele mudar, gerar códigos novos em fluxo seguro. Sem MFA funcional, não abrir o acesso de produção. Conferir também o vínculo Google após o corte. O script não migra nem expõe o anel de chaves ou o pepper.

Executar primeiro a prévia `CALL sp_prepare_clean_client_database(FALSE)`. A chamada com `TRUE` somente após nova autorização e conferência do backup. Após o corte, confirmar uma conta administradora, MFA funcional, configurações, tabelas operacionais vazias, próximo atendimento em 100 e ausência de dados alfa no armazenamento privado de produção. Manter o alfa em acesso restrito conforme política de retenção, nunca publicar seu dump ou seus documentos.

## Atendimentos vencidos

[`cancel_overdue_appointments.sql`](cancel_overdue_appointments.sql) prepara uma rotina **distinta** para um banco operacional: pendentes de datas anteriores à data local da clínica viram cancelados; confirmados viram não compareceram. Ela registra o motivo no histórico e na observação do atendimento, com auditoria de lote. A prévia (`FALSE`) não altera dados. Antes de uma execução futura (`TRUE`), conferir pagamentos/checkout externos em aberto, fuso MySQL da clínica, backup e janela de manutenção. Atendimentos iniciados, concluídos e já cancelados não são afetados. Ela não cancela transações PagBank por conta própria.
