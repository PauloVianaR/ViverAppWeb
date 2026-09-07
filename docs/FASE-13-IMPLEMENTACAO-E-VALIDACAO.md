# Fase 13 — Implementação e validação

## Resultado

A experiência do Gestor foi implementada na branch `codex/fase-13-experiencia-gestor`, sem antecipar a área do Administrador. A API deriva o Gestor exclusivamente da sessão e mantém a fronteira entre operação da clínica e conteúdo clínico reservado ao Médico.

## Entregas funcionais

- início gerencial com identidade laranja, indicadores da clínica, agenda do dia em cards e atalhos operacionais;
- agenda e histórico com período, horário, texto, médico, tipo, modalidade, estado, pagamento, ordenação, contadores e paginação;
- detalhe do atendimento com cancelamento, reagendamento, contato telefônico e confirmação de pagamento presencial;
- financeiro presencial com valor recalculado no servidor, forma permitida, data, final do cartão, autorização, idempotência e ledger;
- pacientes com indicadores, filtros, onboarding por contato, edição operacional segura e agendamento em nome do paciente;
- agendamento com paciente, médico, serviço, modalidade e slots revalidados pela API;
- Premium com lista, comprovante privado, decisão configurável, concorrência, auditoria e notificação durável;
- perfil gerencial com dados pessoais, preferências de e-mail/SMS, contato confirmado, senha, Google, passkeys e sessões;
- navegação exclusiva do Gestor: Início, Agenda, Pacientes, Histórico e Perfil.

## Banco DB-First

As migrations `0017__manager_experience.sql` e `0018__manual_payment_event_source.sql` foram executadas integralmente em `viverappweb`, no MySQL local 8.0.41. O scaffold DB-First foi regenerado depois da aplicação e o verificador confirmou banco, versão e histórico de migrations.

O schema passou a representar preferências do Gestor, autoria da confirmação financeira, metadados mínimos de cartão e autorização, revisão de Premium e a origem manual do evento financeiro.

## Segurança e integridade

- policy exclusiva de Gestor e revalidação dos IDs usados em agenda, pacientes, pagamentos, Premium e documentos;
- DTOs explícitos com rejeição de campos não mapeados e validação no servidor;
- antiforgery, rate limiting, idempotência e concorrência otimista nas operações sensíveis;
- confirmação presencial não aceita valor do browser, não duplica pagamento reconciliado e registra evento financeiro auditável;
- filtros de horário da agenda são avaliados no fuso configurado da clínica, sem comparar a hora local do formulário com o valor UTC persistido;
- download do comprovante Premium possui limite sensível dedicado e a referência de autorização do cartão aceita somente o formato operacional mínimo necessário;
- o Gestor recebe apenas metadados de laudo e anexos, nunca resumo, recomendações ou arquivo clínico;
- onboarding não permite que o Gestor escolha ou conheça a senha do paciente;
- decisão Premium respeita configuração do servidor e gera mensagem durável para processamento posterior;
- logs e auditoria não armazenam conteúdo clínico nem dados completos de cartão.

## Evidências automatizadas

- `dotnet build ViverApp.slnx --no-restore`: aprovado com zero erros e zero avisos;
- `dotnet test ViverApp.slnx --no-build --no-restore`: 166 testes aprovados;
- testes de integração da Fase 13 cobrem leitura de metadados, pagamento manual idempotente, rejeição de duplicidade, autorização de cartão vazia e filtro de horário no fuso da clínica;
- `ViverApp.Database verify`: MySQL 8.0.41, `viverappweb` e migrations aprovados;
- `dotnet format ViverApp.slnx --no-restore --verify-no-changes`: aprovado.
- auditoria NuGet online: nenhum pacote vulnerável conhecido nas dependências diretas ou transitivas.

## Validação visual

A janela do Chrome foi posicionada e confirmada exclusivamente no monitor 3, com `screenX=1920`. Foram validados Início, Agenda, Histórico e detalhe operacional em desktop; Pacientes em tablet; Premium e Perfil em celular; e escala equivalente a 200%.

Os cenários concluíram sem overflow horizontal, erro de console ou resposta HTTP com falha após a autenticação. O diálogo de pagamento apresentou o valor calculado pelo servidor, mas a confirmação não foi enviada. No Perfil móvel, o conteúdo final permaneceu acima da navegação fixa. Após inspeção, os KPIs foram ajustados para aproveitar três colunas no tablet e uma coluna somente em telas de até 640 px.

O certificado HTTPS de desenvolvimento foi preservado. Como o host isolado não acessava o certificado padrão, uma cópia exportada foi mantida em `.local` e suas referências ficaram exclusivamente nos user-secrets separados da API e do Web; nenhum certificado foi limpo ou removido.

## Homologações remanescentes

Dependem de provedores, documentos ou decisões externas e permanecem em `.local/PENDENCIAS.md`: onboarding real por e-mail/SMS, comprovante Premium real não sensível, entrega da notificação de decisão Premium e zoom nativo de 200%.

A conta sintética de Gestor e as capturas visuais permanecem somente em `.local`, fora do Git, até o proprietário confirmar que encerrou os testes manuais.
