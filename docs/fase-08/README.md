# Fase 8 — Jornadas de médico e gestor

**Estado:** implementada na branch `codex/fase-08-jornadas-medico-gestor`; ainda não integrada à `main`.

Esta fase transforma os shells de médico e gestor em uma operação clínica funcional. A API permanece como autoridade de acesso: trocar a URL ou manipular a interface não amplia o conjunto de consultas, pacientes ou relatórios visíveis.

## Entregas

- agenda e histórico por período, status, profissional e busca textual;
- detalhes operacionais de consulta, com observações do paciente visíveis somente ao médico responsável;
- pacientes derivados exclusivamente de vínculos reais por consulta;
- disponibilidade semanal reutilizando os contratos concorrentes da Fase 5;
- rascunho de relatório, conclusão da consulta e publicação imutável;
- registro de falta por médico responsável, gestor ou administrador;
- visualização do conteúdo publicado pelo próprio paciente;
- trilha de status e auditoria sem conteúdo clínico nos eventos;
- interface adaptativa para celular, tablet e desktop.

## Limites deliberados

- consultas pendentes não podem ser concluídas; a confirmação financeira pertence à Fase 9;
- anexos e armazenamento de documentos pertencem à Fase 10;
- o relatório publicado não pode ser editado silenciosamente; eventual retificação exigirá um fluxo próprio e auditável;
- gestor e administrador veem metadados do relatório, nunca seu conteúdo;
- não existe tenancy ou vínculo multiclínica.

Detalhes: [API e autorização](API-E-AUTORIZACAO.md), [sigilo e ciclo clínico](SIGILO-E-CICLO-CLINICO.md) e [verificações](VERIFICACOES.md).
