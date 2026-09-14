# Fase 18 — Implementação e validação

## Resultado

O prontuário eletrônico foi implementado na branch `codex/fase-18-prontuario-eletronico`, sem iniciar e-mail, SMS ou jobs da Fase 19. Médico, Gestor e Administrador acessam uma visão centrada no Paciente; somente o Médico vinculado possui autoria clínica.

## Dados e integridade

A migration `0031__electronic_health_record.sql` cria o prontuário, rascunhos, encontros, versões, documentos e histórico de acesso. Ela foi aplicada no banco local `viverappweb` em MySQL 8.0.41 antes do scaffold. Campos clínicos essenciais e sinais vitais são colunas tipadas. Versões finalizadas e acessos são append-only por trigger; retificações criam uma nova versão com motivo, hash e encadeamento.

## API, autorização e privacidade

As rotas `/api/v1/medical-records` possuem policies por capacidade e respostas sem cache. A API deriva o ator da sessão, valida o papel no banco, aplica vínculo Médico–Paciente e não usa entidades EF como contratos. Gestor precisa justificar conteúdo clínico; Administrador precisa justificar e ter sessão elevada recente. Tentativas negadas também são registradas sem guardar conteúdo clínico.

Documentos passam pela validação de extensão, MIME real, assinatura, limite e malware já usada pelo produto. O objeto fica no R2 privado quando configurado, sem CDN. O PDF é montado no servidor a partir de um snapshot limitado por papel/período, sem HTML ativo, scripts, links ou recursos remotos.

## Experiência Web

Os cards de pacientes dos três perfis abrem o prontuário. O layout apresenta resumo recolhível, Visão geral, Linha do tempo, Prontuário, Financeiro e Documentos; o Administrador também vê Auditoria. No celular, resumo, abas, filtros, formulários, timeline, tabelas e documentos refluem sem transformar o desktop existente.

O Médico seleciona um atendimento vinculado, preenche anamnese e sinais vitais, recebe autosave sinalizado, recupera o rascunho e finaliza somente após iniciar o atendimento. A retificação exige motivo e deixa todas as versões consultáveis. Gestor e Administrador leem o recorte autorizado, sem botões de autoria.

## Evidências

- `dotnet build ViverApp.slnx --no-restore`: zero erros e avisos;
- `dotnet test ViverApp.slnx --no-build --no-restore --maxcpucount:1`: 206 testes aprovados; a execução serial evita disputa entre suítes que compartilham o MySQL local;
- `dotnet format ViverApp.slnx --no-restore --verify-no-changes`: exigido antes do encerramento;
- `ViverApp.Database verify`: MySQL 8.0.41, `viverappweb` e migration `0031` aprovados;
- teste clínico transacional deixa o banco inalterado e comprova versionamento, concorrência, autoria, finalidade, step-up e triggers append-only;
- homologações manuais e externas remanescentes estão exclusivamente em `.local/PENDENCIAS.md`.

## Limites legais declarados

O produto não exibe selo nem afirma certificação SBIS, NGS2, assinatura ICP-Brasil ou eliminação segura do papel. A Lei nº 13.787/2018 prevê requisitos específicos para digitalização e guarda, e a SBIS mantém processo formal de certificação. Política de retenção, textos jurídicos e eventual assinatura digital precisam de validação especializada antes da produção.
