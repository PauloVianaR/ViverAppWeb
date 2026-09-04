# Sigilo e ciclo clínico

## Estados

O agendamento continua seguindo os estados definidos no schema. Nesta fase foram implementadas apenas duas transições operacionais:

- `confirmed → completed`, exclusivamente pelo médico atribuído, depois do início da consulta e com relatório publicado;
- `confirmed → no_show`, depois do início, pelo médico atribuído, gestor ou administrador.

Ambas preservam ator, horário e período em `appointment_status_history`. A consulta recebe também os campos explícitos de conclusão ou falta para leitura operacional eficiente.

## Relatório médico

`medical_reports` mantém um relatório por consulta e possui `draft` e `published`. O rascunho pode ser criado ou atualizado somente pelo médico atribuído. A publicação ocorre atomicamente com a conclusão da consulta e passa a ser imutável.

O conteúdo não é inserido em logs, métricas, erros ou dados de auditoria. A auditoria registra apenas identificadores e códigos de evento. Leituras do conteúdo pelo médico e pelo paciente também são auditadas.

## Visibilidade

- médico atribuído: observação do paciente, rascunho e relatório publicado;
- outro médico: a consulta não aparece e retorna `404` em acesso direto;
- gestor/administrador: dados operacionais e estado do relatório, com conteúdo e observação clínica removidos do contrato;
- paciente: somente relatório próprio, concluído e publicado;
- navegador: nunca decide autorização; apenas apresenta as capacidades retornadas pela API.

Dados de saúde continuam sujeitos à definição jurídica de retenção, retificação e descarte antes da produção.
