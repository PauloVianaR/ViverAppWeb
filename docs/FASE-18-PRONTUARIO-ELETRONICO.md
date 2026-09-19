# Fase 18 — Prontuário eletrônico

## Estado e objetivo

**Estado:** implementada em 14 de setembro de 2026 na branch `codex/fase-18-prontuario-eletronico`; validações manuais e externas remanescentes estão em `.local/PENDENCIAS.md`.

**Branch prevista:** `codex/fase-18-prontuario-eletronico`, criada a partir da `main` atualizada após autorização de merge da Fase 17.

Entregar um prontuário eletrônico centrado no Paciente para Médico, Gestor e Administrador, com visão geral, linha do tempo, informações clínicas, anamnese/evolução, financeiro, documentos e PDF privado, preservando autoria médica, sigilo, versionamento e auditoria.

Esta fase não inicia a Agenda/Psicólogo da Fase 19, e-mail/SMS/jobs da Fase 21 nem altera o legado.

## Referência visual

O anexo fornecido pelo proprietário é apenas inspiração para:

- resumo do Paciente;
- indicadores laterais;
- abas de linha do tempo, prontuário e financeiro;
- filtros por período e ordenação;
- geração de PDF;
- exibição cronológica de avaliações/anamneses.

A Web não copiará aparência, fotografia, dados ou textos do exemplo. Em desktop poderá usar resumo lateral fixo; em tablet/celular esse resumo vira cabeçalho recolhível e as abas adotam navegação acessível. Todo o fluxo deve funcionar com teclado, leitor de tela, touch e zoom de 200%.

## Regras invariáveis

- alterar somente `ViverAppWeb`;
- usar exclusivamente `viverappweb`; `viverappmobile` permanece somente leitura;
- seguir DB-First: aplicar migration SQL integral no MySQL local 8.0.41 antes do scaffold;
- nunca editar manualmente entidades/contexto gerados;
- conteúdo clínico é dado pessoal sensível e permanece privado;
- Médico mantém autoria clínica; Gestor/Administrador não criam, assinam ou retificam registros médicos;
- registros finalizados são imutáveis; correção ocorre por nova versão/retificação;
- PDF e documento obedecem exatamente ao recorte autorizado da tela;
- nenhuma informação clínica entra em logs, telemetria, analytics, popup ou CDN pública;
- acesso sensível usa autorização revalidada, justificativa quando aplicável, step-up, `no-store` e auditoria append-only;
- nenhuma funcionalidade da Fase 19 ou da Fase 21 será antecipada.

## Decisão de acesso por papel

O pedido atual revisa a decisão anterior que limitava Gestor e Administrador a metadados. A Fase 18 disponibilizará a área integrada aos três papéis, com limites claros:

| Capacidade | Gestor | Médico | Administrador |
|---|---:|---:|---:|
| Ver dados cadastrais e agenda | Sim | Pacientes vinculados | Sim |
| Ver linha do tempo operacional | Sim | Pacientes vinculados | Sim |
| Ver financeiro | Sim | Resumo mínimo quando necessário | Sim |
| Acessar conteúdo clínico | Ação explícita, motivo e auditoria | Sim, dentro do vínculo | Step-up MFA, motivo e auditoria |
| Criar rascunho clínico | Não | Sim | Não |
| Finalizar/retificar conteúdo clínico | Não | Sim | Não |
| Gerar PDF administrativo | Sim | Quando autorizado | Sim com step-up |
| Gerar PDF clínico | Somente recorte autorizado e justificado | Sim, dentro do vínculo | Step-up e justificativa |

Durante a implementação, as documentações das Fases 8, 13 e 14 e a matriz de paridade deverão registrar formalmente essa decisão superveniente.

## Estrutura da experiência

### Resumo do Paciente

- foto opcional privada;
- nome, nome preferido e idade;
- CPF/contatos somente conforme papel;
- próximos atendimentos e último atendimento;
- alertas clínicos críticos autorizados;
- contadores de atendimentos, concluídos, faltas, cancelados e documentos;
- status da conta e Premium quando operacionalmente necessário;
- ação de retorno à lista sem perder filtros.

### Abas

- **Visão geral**;
- **Linha do tempo**;
- **Prontuário**;
- **Financeiro**;
- **Documentos**.

As abas não ampliam autorização: cada consulta da API revalida papel, vínculo, finalidade e sessão elevada.

### Filtros

- período limitado;
- Médico;
- tipo de atendimento/evento;
- status;
- ordem crescente/decrescente;
- busca apenas em campos autorizados;
- limpar filtros e paginação estável;
- nenhum texto clínico enviado a mecanismo externo de busca.

## Visão geral

- identificação e contatos autorizados;
- alergias e alertas críticos registrados por Médico;
- condições/problemas ativos;
- medicamentos informados;
- antecedentes resumidos quando permitido;
- última consulta e próximos atendimentos;
- resumo financeiro sem dados completos do meio de pagamento;
- autoria e instante da última atualização;
- indicadores com alternativa textual e estado “não informado”.

## Linha do tempo

Unificar cronologicamente, sem misturar permissões:

- criação, confirmação, chegada, senha, início, conclusão, reagendamento, cancelamento e falta;
- pagamentos, reversões e pagamento substituto vindos da Fase 17;
- anamnese/evolução finalizada e retificações;
- laudos e documentos clínicos por metadados;
- documentos administrativos e PDFs gerados;
- autor, papel, instante e tipo do evento.

Quando a existência do evento for operacionalmente útil, mas o conteúdo não estiver autorizado, exibir “registro restrito” sem título, trecho ou metadado sensível.

## Registro clínico

### Estrutura mínima

O Médico poderá registrar de forma estruturada:

- motivo/queixa principal;
- história da doença atual;
- antecedentes pessoais e familiares;
- alergias;
- medicamentos;
- hábitos relevantes;
- sinais vitais;
- exame físico;
- hipóteses/diagnósticos e códigos padronizados quando aprovados;
- conduta e orientações;
- solicitações e plano de acompanhamento;
- evolução clínica;
- observações e anexos privados vinculados.

Campos estruturados terão “não informado/não se aplica” quando legítimo. Texto livre terá limites, validação, encoding na saída e nunca será interpretado como HTML.

### Rascunho

- privado ao Médico autor;
- vinculado a Paciente e atendimento autorizado;
- autosave controlado e sinalizado;
- concorrência otimista entre abas/dispositivos;
- recuperação após perda de conexão sem sobrescrever versão mais nova;
- rascunho não aparece como registro clínico final para outros papéis;
- expiração/retenção definida e documentada.

### Finalização

- revisão explícita antes de concluir;
- associa Médico, CRM/UF, Paciente, atendimento, instante do servidor e versão;
- conteúdo torna-se imutável;
- hash/controle de versão detecta alteração indevida;
- evento entra na linha do tempo;
- falha parcial não conclui atendimento sem registro clínico exigido;
- conclusão duplicada é idempotente.

### Retificação

- nunca sobrescreve o registro anterior;
- exige motivo e confirmação;
- cria nova versão encadeada;
- mantém autor e instante de todas as versões;
- destaca qual é a versão vigente;
- permite consultar o histórico somente a papéis autorizados;
- auditoria não guarda o conteúdo clínico, somente metadados mínimos.

### Assinatura e validade

- a interface não afirmará certificação ICP-Brasil, NGS2 ou validade probatória que não tenha sido tecnicamente/juridicamente comprovada;
- assinatura digital, quando exigida, será integrada apenas com método legalmente validado e credencial do Médico;
- requisitos vigentes do CFM/SBIS serão revistos na data da implementação;
- relógio do servidor, UTC e fuso da clínica determinam todos os instantes.

## Financeiro no prontuário

A aba Financeiro consome o ledger da Fase 17, sem criar uma segunda fonte de verdade.

- atendimentos e respectivos valores;
- status de pagamento;
- método redigido;
- pagamento original, reversão e substituto;
- totais limitados ao Paciente e ao intervalo;
- links para o atendimento conforme autorização;
- Gestor/Admin podem abrir o detalhe operacional;
- Médico recebe somente o mínimo necessário e não opera caixa;
- nenhum PAN, CVV, token, segredo ou referência completa do provedor.

## Documentos

- laudos, anexos clínicos e documentos administrativos separados por tipo;
- metadados mínimos: nome seguro, tipo, tamanho, autor, atendimento, versão e data;
- upload somente para papéis/casos autorizados;
- validação de tamanho, extensão, MIME real, assinatura e malware;
- armazenamento no R2 privado, com chave imprevisível;
- download curto, autorizado no momento da requisição e auditado;
- exclusão lógica/retenção conforme política, sem apagar histórico clínico indevidamente;
- nenhuma URL pública ou cache compartilhado.

## Geração de PDF

### Conteúdo

- recorte por papel, aba, atendimento ou período;
- cabeçalho aprovado da clínica;
- Paciente e Médico/CRM quando aplicável;
- data de emissão, paginação e versão;
- linha do tempo/anamnese/evolução somente conforme autorização;
- rodapé com identificador verificável e aviso de confidencialidade;
- marca “Rascunho” quando juridicamente/funcionalmente aplicável.

### Segurança

- gerado no servidor a partir de snapshot consistente;
- nenhuma ampliação de permissão pela URL/parâmetro;
- sem scripts, HTML ativo, links inseguros ou conteúdo remoto;
- streaming/download privado com `no-store` e `Content-Disposition` seguro;
- expiração curta quando houver objeto temporário;
- geração e download auditados sem conteúdo clínico no log;
- Gestor/Admin informam motivo e usam step-up conforme o recorte;
- PDF não será enviado por e-mail/SMS nesta fase.

## Privacidade e conformidade

O prontuário deve preservar finalidade, necessidade, confidencialidade, integridade, autenticidade, rastreabilidade e direitos do titular. A política final de retenção e os textos jurídicos precisam de validação profissional antes da produção.

Referências normativas para revalidação na implementação:

- [LGPD — Lei nº 13.709/2018, texto compilado](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13709compilado.htm);
- [Lei nº 13.787/2018 — prontuário de paciente](https://www.planalto.gov.br/ccivil_03/_ato2015-2018/2018/lei/l13787.htm);
- normas vigentes do CFM/SBIS, sem afirmar certificação antes de auditoria apropriada.

## Banco DB-First planejado

A migration sequencial seguinte às migrations aplicadas das Fases 16 e 17 deve contemplar, conforme o schema real:

- prontuário por Paciente;
- encontros/registros clínicos vinculados a atendimento e Médico;
- anamnese/evolução estruturada;
- rascunhos;
- versões, retificações e encadeamento;
- alergias, medicamentos, condições, sinais vitais e alertas tipados;
- documentos/anexos e relação com versões/atendimentos;
- histórico de acesso clínico justificado;
- índices para Paciente, atendimento, Médico, período, versão e timeline;
- constraints de autoria/estado, FKs restritivas, timestamps UTC e `row_version`;
- proteção contra update/delete de versões finalizadas e trilhas de acesso.

Evitar EAV ou JSON genérico como única fonte clínica. JSON pode guardar snapshot complementar versionado, mas campos essenciais/pesquisáveis serão tipados.

Aplicar a migration integralmente em `viverappweb` no MySQL local 8.0.41 e somente depois regenerar o EF DB-First.

## API e contratos planejados

- resumo autorizado do Paciente;
- linha do tempo paginada/filtrada;
- visão financeira projetada do ledger;
- listar/obter registros e versões;
- criar/atualizar rascunho;
- finalizar registro;
- retificar versão finalizada;
- listar/enviar/baixar documentos autorizados;
- gerar/baixar PDF;
- registrar finalidade de acesso Gestor/Admin;
- consultar histórico de acesso para auditoria administrativa autorizada.

Queries projetam apenas campos permitidos, limitam intervalo/página, evitam N+1 e aceitam cancelamento. Entidades EF nunca são contratos externos.

## Segurança obrigatória

- policies granulares para resumo, timeline, clínico, financeiro, documentos, PDF e auditoria;
- ownership do Médico validado no banco em toda operação;
- Gestor usa ação explícita e motivo para conteúdo clínico;
- Administrador usa MFA/step-up recente e motivo;
- rascunho somente do Médico autor;
- proteção contra IDOR, mass assignment, XSS armazenado, enumeração e exportação excessiva;
- antiforgery em mutações e rate limiting em busca/PDF/download;
- conteúdo clínico redigido de logs, tracing, erros, auditoria e outbox;
- respostas privadas com `no-store` e sem PII em URL;
- R2 privado, MIME fixo e proteção contra conteúdo ativo;
- trilha de acesso e versões append-only;
- auditoria nunca é removida para facilitar testes.

## Plano de execução

1. consultar CodeGraph, schema, laudos, documentos e policies atuais;
2. revisar normas vigentes e fechar matriz de acesso/autoria;
3. fechar modelo físico de registros, versões, timeline, documentos e acessos;
4. criar/revisar/aplicar migration no `viverappweb` MySQL 8.0.41;
5. regenerar EF DB-First e revisar o diff;
6. implementar domínio/persistência e testes de imutabilidade/concorrência;
7. implementar APIs/policies e testes negativos cruzados;
8. implementar resumo, abas, filtros e timeline responsiva;
9. implementar rascunho, finalização e retificação médica;
10. integrar Financeiro ao ledger da Fase 17;
11. integrar documentos privados e PDF;
12. atualizar decisões anteriores e matriz de paridade;
13. executar build, suíte integral, MySQL, segurança e privacidade;
14. validar exclusivamente no monitor 3 em celular, tablet e desktop;
15. parar sem implementar a Fase 19 nem iniciar a Fase 21.

## Testes mínimos

### Autoria e imutabilidade

- Médico finaliza registro próprio e não altera versão finalizada;
- retificação preserva original, motivo, autor e encadeamento;
- Gestor/Admin não criam, finalizam ou retificam conteúdo médico;
- dois rascunhos concorrentes não sobrescrevem silenciosamente;
- conclusão/retry produz uma versão final válida;
- update/delete direto de versão finalizada é recusado.

### Autorização e privacidade

- Médico A não acessa Paciente/registro/documento do Médico B sem vínculo permitido;
- Gestor autenticado acessa o prontuário operacional sem finalidade obrigatória; Admin sem finalidade ou sem step-up recebe recusa;
- URL, ID, filtro ou PDF não amplia escopo;
- eventos restritos não revelam título/trecho/metadado clínico;
- logs, erros, outbox e analytics não recebem conteúdo;
- documento/PDF privado não é servido por CDN pública nem cacheado;
- histórico de acesso registra tentativa permitida e negada.

### Interface e PDF

- resumo, abas e filtros funcionam nos três perfis;
- timeline ordena corretamente eventos operacionais, financeiros e clínicos;
- Financeiro reflete original, reversão e pagamento substituto;
- PDF reproduz versão/recorte e omite seções não autorizadas;
- documento inválido/malicioso é recusado;
- teclado, leitor de tela, foco, contraste, touch e zoom 200%;
- validação visual somente no monitor 3.

## Cenário ponta a ponta obrigatório

1. Médico abre Paciente vinculado e consulta resumo/linha do tempo.
2. Cria rascunho, perde/reconecta a sessão e recupera a versão correta.
3. Finaliza anamnese/evolução e gera PDF clínico autorizado.
4. Retifica com motivo; original e nova versão permanecem consultáveis.
5. Gestor abre o mesmo prontuário e vê agenda, conteúdo clínico permitido e financeiro sem finalidade obrigatória, mantendo auditoria do acesso.
6. Administrador usa MFA/step-up, justifica o acesso e gera seu recorte autorizado.
7. Outro Médico e sessão Admin sem finalidade ou não elevada recebem recusa.
8. Timeline mostra chegada da Fase 16 e pagamento/reversão/substituto da Fase 17 sem duplicar fontes.

## Critérios de saída

- prontuário integrado existe para Gestor, Médico e Administrador;
- visão geral, timeline, clínico, financeiro, documentos e PDF estão completos;
- Médico é o único autor de conteúdo clínico;
- versões finalizadas são imutáveis e retificações preservam histórico;
- acessos do Gestor são auditados sem finalidade obrigatória; acessos do Admin exigem finalidade, elevação recente e auditoria;
- dados/documentos permanecem privados, sem vazamento por cache, log, URL ou CDN;
- migration foi aplicada no MySQL local 8.0.41 e EF regenerado por DB-First;
- build, suíte integral, segurança, privacidade e validação visual estão aprovados;
- decisões das Fases 8/13/14 e matriz de paridade foram atualizadas;
- Fase 19 não foi implementada, Fase 21 não foi iniciada e nenhuma pendência real foi ocultada.

## Implementação realizada

- migrations `0031__electronic_health_record.sql` e `0032__optional_medical_record_content.sql` aplicadas integralmente em `viverappweb` no MySQL 8.0.41 antes das respectivas regenerações DB-First;
- prontuário único por Paciente, rascunhos concorrentes por Médico/atendimento, registros finalizados e retificações encadeadas por versão;
- versões clínicas e trilhas de acesso protegidas no próprio MySQL por bloqueio append-only de `UPDATE` e `DELETE`;
- conteúdo tipado para anamnese, evolução, antecedentes, alergias, medicamentos, hábitos, exame, diagnóstico, conduta, plano, observações e sinais vitais;
- endpoints separados para resumo, atendimentos, timeline, financeiro, registros/versões, rascunho, finalização, retificação, documentos, PDF e auditoria;
- policies granulares para leitura, conteúdo clínico, autoria, documento, exportação e auditoria, além da revalidação de vínculo no banco;
- Gestor acessa o conteúdo clínico permitido sem finalidade obrigatória; Administrador exige finalidade e sessão elevada recente; acessos e recusas entram na trilha;
- documento privado reutiliza validação real de conteúdo/malware e o R2 privado da Fase 15, sem URL pública;
- PDF é gerado no servidor a partir de snapshot autorizado, sem HTML, script ou recurso remoto, com `no-store`, paginação, versão e aviso de confidencialidade;
- interface responsiva única para Médico, Gestor e Administrador, acessível a partir do card do Paciente, com visão geral no resumo recolhível, quatro abas operacionais e auditoria adicional para Administrador;
- Médico possui autosave sinalizado, recuperação de rascunho, confirmação de finalização, retificação sem sobrescrita e anexos privados;
- timeline agrega status dos atendimentos, chegada/início, movimentos do caixa, versões clínicas e documentos sem criar fontes paralelas.

## Evidências automatizadas

- build integral: zero erros e zero avisos;
- suíte integral: 214 testes aprovados nos sete projetos, incluindo regressões do tipo de parâmetro da rota, contenção global de falhas, limpeza de stack trace, conteúdo clínico opcional, seleção de atendimentos elegíveis e os ajustes de ciclo de vida;
- teste transacional no MySQL prova rascunho, rejeição de versão concorrente, finalização, retificação, preservação das duas versões, bloqueios por papel/finalidade/step-up e recusa de alteração direta da versão finalizada;
- contratos verificam autenticação das rotas, policies por capacidade, CORS da finalidade clínica, proteção antes do prerender e PDF sem scripts/recursos remotos;
- verificador confirma MySQL 8.0.41, banco `viverappweb`, migrations até `0033`, a preferência visual por conta, os controles temporários do Gestor, a categoria Procedimento, seis tabelas clínicas, ausência da restrição de conteúdo mínimo e quatro triggers append-only.

## Revalidação normativa

Em 14 de setembro de 2026 foram consultados o texto compilado da LGPD, a Lei nº 13.787/2018 e os documentos vigentes de certificação S-RES publicados pela SBIS. A implementação preserva integridade, confidencialidade, rastreabilidade e retenção configurável, mas não afirma certificação SBIS, NGS2, ICP-Brasil ou validade probatória. Certificação, assinatura digital qualificada, política final de retenção e textos jurídicos continuam dependentes de avaliação formal antes da produção.
