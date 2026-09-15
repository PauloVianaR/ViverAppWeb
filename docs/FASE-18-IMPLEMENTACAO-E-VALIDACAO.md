# Fase 18 — Implementação e validação

## Resultado

O prontuário eletrônico foi implementado na branch `codex/fase-18-prontuario-eletronico`, sem iniciar e-mail, SMS ou jobs da Fase 19. Médico, Gestor e Administrador acessam uma visão centrada no Paciente; somente o Médico vinculado possui autoria clínica.

## Dados e integridade

A migration `0031__electronic_health_record.sql` cria o prontuário, rascunhos, encontros, versões, documentos e histórico de acesso. A migration complementar `0032__optional_medical_record_content.sql` remove a antiga exigência mínima de conteúdo: somente o vínculo com o atendimento é obrigatório, enquanto todos os campos clínicos e sinais vitais são opcionais. Ambas foram aplicadas no banco local `viverappweb` em MySQL 8.0.41 antes do scaffold. Campos informados permanecem tipados. Versões finalizadas e acessos são append-only por trigger; retificações criam uma nova versão com motivo, hash e encadeamento.

## API, autorização e privacidade

As rotas `/api/v1/medical-records` possuem policies por capacidade e respostas sem cache. A API deriva o ator da sessão, valida o papel no banco, aplica vínculo Médico–Paciente e não usa entidades EF como contratos. Gestor precisa justificar conteúdo clínico; Administrador precisa justificar e ter sessão elevada recente. Tentativas negadas também são registradas sem guardar conteúdo clínico.

Documentos passam pela validação de extensão, MIME real, assinatura, limite e malware já usada pelo produto. O objeto fica no R2 privado quando configurado, sem CDN. O PDF é montado no servidor a partir de um snapshot limitado por papel/período, sem HTML ativo, scripts, links ou recursos remotos.

## Experiência Web

Os cards de pacientes dos três perfis abrem o prontuário. O layout apresenta resumo recolhível com a visão geral incorporada, Linha do tempo, Prontuário, Financeiro e Documentos; o Administrador também vê Auditoria. No celular, resumo, abas, filtros, formulários, timeline, tabelas e documentos refluem sem transformar o desktop existente.

O Médico seleciona um atendimento vinculado, preenche anamnese e sinais vitais, recebe autosave sinalizado, recupera o rascunho e finaliza somente após iniciar o atendimento. A retificação exige motivo e deixa todas as versões consultáveis. Gestor e Administrador leem o recorte autorizado, sem botões de autoria.

## Evidências

- `dotnet build ViverApp.slnx --no-restore`: zero erros e avisos;
- `dotnet test ViverApp.slnx --no-build --no-restore --maxcpucount:1`: 212 testes aprovados; a execução serial evita disputa entre suítes que compartilham o MySQL local;
- `dotnet format ViverApp.slnx --no-restore --verify-no-changes`: exigido antes do encerramento;
- `ViverApp.Database verify`: MySQL 8.0.41, `viverappweb` e migrations até `0032` aprovados;
- teste clínico transacional deixa o banco inalterado e comprova versionamento, concorrência, autoria, finalidade, step-up e triggers append-only;
- homologações manuais e externas remanescentes estão exclusivamente em `.local/PENDENCIAS.md`.

## Limites legais declarados

O produto não exibe selo nem afirma certificação SBIS, NGS2, assinatura ICP-Brasil ou eliminação segura do papel. A Lei nº 13.787/2018 prevê requisitos específicos para digitalização e guarda, e a SBIS mantém processo formal de certificação. Política de retenção, textos jurídicos e eventual assinatura digital precisam de validação especializada antes da produção.

## Correção posterior ao primeiro teste manual

- corrigida a incompatibilidade entre a restrição `long` das rotas e o antigo parâmetro `ulong` do componente, que encerrava o circuito Blazor ao abrir qualquer prontuário;
- adicionada uma barreira global para conter falhas não tratadas de componentes, registrar o diagnóstico técnico somente no servidor e acionar o popup padronizado para o usuário;
- o fallback de desconexão do circuito também passou a usar uma apresentação modal responsiva com a mensagem segura em português;
- mensagens técnicas de transporte, como `TypeError`, `Failed to fetch` e falha de negociação, não são mais exibidas literalmente ao usuário.

## Ajustes posteriores do formulário clínico

- mensagens catalogadas são separadas do stack trace de JavaScript antes de alimentar aviso e popup; os detalhes técnicos permanecem no console;
- todos os campos clínicos e sinais vitais passaram a ser opcionais no banco, na API e na interface; pressão `12/8` e altura `1,70` são normalizadas para `120/80 mmHg` e `170 cm`, e zeros oriundos de campos vazios são tratados como ausência;
- o seletor médico recebe somente atendimentos confirmados, com chegada ou iniciados;
- a visão geral foi incorporada ao resumo e a informação redundante de privacidade foi removida;
- ao trocar de atendimento, alterações pendentes são salvas no atendimento anterior antes do rascunho do novo contexto ser carregado, permitindo retornar sem perda de conteúdo.
