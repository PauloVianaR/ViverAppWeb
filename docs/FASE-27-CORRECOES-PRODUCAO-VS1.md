# Correções de produção vS1 — achados F27-01 a F27-03

Esta correção foi desenvolvida em `codex/correcoes-producao-vs1`, a partir da `main` que continha as duas rodadas da Fase 27. Os relatórios originais continuam históricos; este documento registra somente as alterações e seus limites.

| Achado | Correção | Regressão |
| --- | --- | --- |
| F27-01 | Migration 0049 inclui `psychologist` nas restrições de `clinical_access_events` e permite finalidade nula para os dois papéis clínicos. Auditoria e prontuário permanecem append-only. | Teste transacional do prontuário exige acesso vinculado, 404 não vinculado e dois eventos auditados do Psicólogo. |
| F27-02 | Alias `api/v1/admin/account` autoriza o papel real `administrator`, sem incluir papéis adicionais. | Teste de contrato do atributo de autorização, além da suíte de identidade. |
| F27-03 | Busca Unicode deixa de comparar o termo com `phone_e164` (charset ASCII). Nome/e-mail permanecem pesquisáveis; termos ASCII continuam buscando telefone e, no Gestor, CPF. | Teste de integração busca “João” nas visões clínica, médica e gerencial, sem HTTP 500. |

A causa de F27-03 foi confirmada no MySQL 8.0.41: comparar um termo UTF-8 acentuado à coluna ASCII `phone_e164` devolve erro 3854; a mesma comparação com `full_name` funciona.

A migration 0049 foi aplicada integralmente em `viverappweb` e `viverappweb_homolog`, ambos bancos locais persistentes; nenhum evento clínico foi apagado. Ela é avanço de schema: não executar rollback automático para voltar a proibir eventos legítimos de Psicólogo. Na futura máquina de produção, revisar e aplicar somente as migrations ainda pendentes em janela controlada, com backup e credencial separada. O runner de desenvolvimento não deve apontar para produção.

Estas três correções removem os defeitos confirmados das rodadas locais, mas **não certificam a aplicação para produção**. A matriz de pentest continua parcial: autorização horizontal integral, arquivos/R2, PagBank fake, identidade externa, vídeo, jobs, navegador integrado, IIS/TLS/Cloudflare e revisão humana independente ainda precisam da homologação descrita nos relatórios e em `.local/PENDENCIAS.md`. Pacotes de Release são artefatos de transferência, não autorização para publicar.
