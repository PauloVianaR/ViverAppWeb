# Fase 30 — Prontuário oftalmológico

## Escopo e decisões

- Identificar o laudo oftalmológico pelo vínculo do profissional responsável com a especialidade normalizada `OFTALMOLOGIA`, independentemente de ela ser primária ou secundária.
- Substituir o campo de resumo clínico no atendimento por seis caixas de uma linha que crescem com o conteúdo: história oftalmológica, acuidade visual, refração, biomicroscopia, tonometria e fundo de olho. Recomendações permanecem disponíveis.
- Médico responsável, Gestor e Administrador podem ler e salvar o laudo oftalmológico; cada versão identifica a conta editora real. Um profissional não pode alterar o atendimento de outro. Acesso administrativo requer autenticação recente/MFA. Atendimentos pendentes ou cancelados não aceitam edição.
- A aba **Atendimentos** do prontuário lista os registros do mais recente para o mais antigo, em expanders, com paginação. A interpretação adotada para a frase de exclusão foi **não mostrar os estados Pendente e Cancelado**. Médico e Psicólogo veem somente seus próprios atendimentos; Gestor e Administrador seguem a autorização clínica existente.
- O PDF individual é privado, auditado e inclui somente o atendimento selecionado e seus campos clínicos pertinentes, não a linha do tempo ou o financeiro completo do paciente.

## Banco DB-First

- `0051__ophthalmology_report_fields.sql`: acrescenta seis campos nullable ao laudo corrente e às versões; copia o `clinical_summary` anterior para `ophthalmic_history` nos laudos já existentes de profissionais com Oftalmologia, preservando o texto antigo.
- `0052__medical_report_version_editor.sql`: cria `editor_account_id` nas versões, retropreenche com o autor profissional nas versões antigas e vincula à conta. A assinatura profissional original não é substituída por um Gestor/Administrador.
- Ambas as migrations foram executadas no MySQL local 8.0.41, banco `viverappweb`, e confirmadas pelo comando de status até a `0052`. O EF foi regenerado a partir desse banco; nenhum modelo gerado foi editado manualmente. Os scripts de rollback estão versionados, mas **não foram executados**.

## Validações realizadas

- Build Release da solution: aprovado, zero avisos e zero erros.
- Testes de integração `ClinicalOperationsIntegrationTests`: 12/12, incluindo escrita por Médico/Gestor/Admin, histórico do editor e recusa de profissional alheio e de finalização legada capaz de sobrescrever o laudo estruturado.
- Testes Web: 69/69; contrato do banco: 6/6; `dotnet format --verify-no-changes`: aprovado.
- Suíte geral executada de forma sequencial. Todos os projetos passaram exceto dois testes preexistentes de ambiente: o teste do gatilho append-only referencia `viver_migrator@localhost`, ausente nesta instância MySQL; o teste `RegistrationOptions_ListEveryRecognizedMedicalSpecialty` configura explicitamente `Port=1` e obtém 500 ao tentar consultar especialidades. Esses pontos não foram corrigidos nesta fase nem houve enfraquecimento das proteções.
- A inspeção visual autenticada não pôde ser realizada: o navegador integrado do Codex falhou ao iniciar com `windows sandbox failed: helper_unknown_error: apply deny-read ACLs`. Não houve substituição por navegador externo, alteração de certificados ou bypass de login.

## Critério para encerramento operacional

Validar no navegador integrado, em desktop/tablet/celular e com contas autorizadas, edição e persistência do laudo nos três papéis, expanders e ordenação da aba, ocultação de Pendente/Cancelado, PDF individual e preservação do texto migrado em atendimento oftalmológico anterior. Registrar o resultado sem inserir dados clínicos reais de teste.
