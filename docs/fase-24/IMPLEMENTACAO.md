# Fase 24 — administração, analytics e operação

## Escopo entregue

- A gestão de usuários, clínica única, agendamentos, Premium e notificações permanece no backoffice administrativo existente, com autorizações na API por papel e sessão. A operação de filas e reprocessamento utiliza o console administrativo da Fase 22, sem apresentar payloads ou segredos.
- Ações administrativas de escrita exigem autenticação recente (step-up) com MFA. Bloqueio/reativação de conta e cancelamento Premium exigem também digitar o nome completo do titular; a mudança do modo manutenção exige frase específica. A confirmação é verificada na API e as alterações críticas são auditadas na mesma transação.
- O sistema foi definido para uma única conta administradora. Portanto, “dupla confirmação” significa MFA recente **mais** confirmação explícita da ação; não significa aprovação por um segundo administrador inexistente. A segregação continua entre papéis e políticas da API. Não há bypass de MFA.
- Indicadores da página inicial e de notificações são calculados por contagem no servidor. A prévia de origens é limitada a 100 entradas por indicador e a interface informa quando há mais registros; consultas analíticas aceitam no máximo 366 dias e agrupam no banco. Não são enviados prontuários nos relatórios.
- A exportação analítica CSV é solicitada pelo Administrador, processada por worker em segundo plano, limitada a duas solicitações ativas por conta, restrita ao próprio solicitante e disponível por 24 horas. O arquivo fica protegido com Data Protection no banco, com hash de integridade; download exige MFA recente, não usa cache e gera evento de auditoria. A planilha neutraliza fórmulas injetadas nas células. O worker usa lease, retentativas e expiração, limpando o conteúdo ao expirar.
- Migration `0047__administrator_analytics_exports.sql` aplicada exclusivamente ao MySQL 8.0.41 local `viverappweb`, seguida de scaffold DB-First. O banco legado `viverappmobile` e os projetos Mobile não foram alterados.

## Validação

- Build Release sem avisos ou erros; suíte completa de testes, incluindo autorização da rota, segregação de exportações por conta, proteção de CSV, confirmação de ações críticas e contrato de banco.
- `dotnet run --project tools/ViverApp.Database -- verify` confirmou MySQL 8.0.41, `viverappweb` e migrations.
- O navegador integrado abriu o site em HTTP local, mas redirecionou para `acesso?estado=sessao-expirada`; a inspeção visual autenticada de Analytics, exportação e confirmações permanece registrada em `.local/PENDENCIAS.md`. O MFA não foi contornado.

## Operação e limites

- A exportação depende do mesmo key ring persistente e protegido da API. Antes de múltiplas instâncias, configurar o key ring de produção compartilhado e certificado conforme a Fase 26; não copiar chaves para o Git.
- A expiração é lógica e remove o conteúdo protegido pelo worker após o prazo. Não purgar registros de auditoria ou metadados de exportação sem política de retenção aprovada.
- A medição de carga, índices e metas de latência fica na Fase 25; a Fase 24 limita materialização e período, mas não declara SLO de produção homologado.
