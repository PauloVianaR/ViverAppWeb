# Fase 11 — Implementação e evidências

## Estado

Implementação concluída e validada localmente na branch `codex/fase-11-experiencia-paciente`. O escopo continua sendo [Experiência completa do Paciente](FASE-11-EXPERIENCIA-PACIENTE.md).

**Aceite externo ainda pendente:** a implementação e a jornada local foram validadas, mas testes com provedores e dispositivos reais continuam necessários. Testes de integração com cliente PagBank simulado não equivalem a um pagamento no sandbox real.

`AGENTS.md` criado na raiz e ignorado pelo `.gitignore` existente. Alterações restritas a `ViverAppWeb`. CodeGraph foi consultado, mas as respostas não localizaram suficientemente os módulos novos; a exploração continuou nos arquivos. O índice não foi recriado. A pasta obsoleta não foi consultada.

## Rastreabilidade do paciente

As sete imagens autorizadas em `ImagensPlayStore/Paciente` foram inspecionadas: `Paciente.png`, `Inicio.png`, `agendar.png`, `agenda.png`, `Pagamento.png`, `perfil.png` e `chamada online.png`. As referências MAUI são da pasta `ViverAppMobileNew`, nunca da pasta obsoleta.

| Referência e capacidade | Implementação Web/API | Evidência automatizada / aceite restante |
| --- | --- | --- |
| PatientMainPage / PatientTabbedPage; navegação azul em cinco áreas | MainLayout, ShellNavigationCatalog, patient.css | WebAccessibilityContractTests; conferência visual integrada em celular, tablet e desktop |
| PatientHomeView / VM; saudação, Premium, próximo atendimento, exames e cirurgias | PatientHome, PatientVisitCard, PatientExperienceService.HomeAsync/ServicesAsync | Login real acessa home; desconto Premium ativo/expirado; conferir vitrine e carrossel visualmente |
| Promoções e CTA externo | Configuração patient.promotions; host HTTPS exato autorizado pela API; sem autoplay | Sem anúncios por padrão; configurar conteúdo real antes do aceite |
| PatientScheduleView / VM; serviço, médico, modalidade, data, horário, observações, resumo | PatientScheduling; API de booking e catálogo enriquecido | PatientSchedulingPolicyTests, PatientSchedulingConcurrencyTests, desconto server-side |
| PatientAgendaView / VM; futuros/histórico, pesquisa e filtros, cards | PatientAgenda, PatientAppointmentDetails | Testes de ownership, data final inclusiva e avaliação; cards, dialog, Escape e retorno de foco conferidos no navegador |
| ScheduleDetailsPopup, CancelSchedulePopup, ReschedulePopup | Card e detalhe; dialog de cancelamento; seleção de novo horário | Cancelamento/histórico/idempotência/conflitos; reagendamento pago preserva cobrança e referência original do gateway |
| Rotas, chamada telefônica e teleconsulta | Links explícitos Maps/tel; PatientVideo | Endereço não configurado não gera rota fictícia; confirmar links com cadastro real da clínica |
| MedicalReportPopup, AppointmentAttachmentsPopup | Laudo publicado existente; listagem paginada e download privado | ClinicalOperationsIntegrationTests e teste de anexo: rascunho/ownership/quarentena/paginação |
| RateSchedulePopup | Avaliação 1–5, comentário opcional, uma por atendimento concluído | Duplicidade e atendimento ainda não concluído rejeitados; nota não altera pagamento |
| PatientPaymentView / PatientWaitPaymentPage / PaymentSuccessfulPage e VMs | PatientPayments, pagamento na clínica ou Checkout PagBank, espera e recibo | PaymentIntegrationTests; assinatura/replay/ordem dos eventos; escolha na clínica não marca pago; sandbox externo pendente |
| PatientProfileView / VM; dados, CEP e preferências | PatientProfile, perfil com versão concorrente, máscara/consulta de CEP e preenchimento manual | Persistência de endereço/CPF/preferências, conflito de versão; máscara e autopreenchimento ViaCEP conferidos no navegador |
| Contatos e ChangePasswordPopup | Novo contato por OTP antes da substituição; senha atual; revogação de sessões | Login real, confirmação de novo e-mail e invalidação da sessão anterior; envio externo de e-mail/SMS não disparado nos testes |
| Google, passkeys e dispositivos | Reautenticação recente para Google/contato; AccountSecurity e revogar outras sessões | Fundamentos da fase 10 preservados; confirmar autenticação Google e passkey física no navegador |
| Premium: comprovante, estados, rejeição e espera, cancelamento | PatientProfile; PrivateDocumentStore; Premium com uma solicitação aberta por conta | Upload falso/ativo/traversal recusado; scanner em falha bloqueia; criptografia, ownership, cooldown de três dias e cancelamento testados |
| OnlinePage / OnlinePageViewModel | WebRTC + SignalR autenticado, câmera/microfone, reconexão, indicadores e encerramento | Sala local elegível e botão de entrada conferidos; elegibilidade por conta/sessão/janela/pagamento testada; áudio/vídeo em duas pontas ainda não homologado |

## Banco — DB-First

- Migrations `0013__patient_experience.sql` e `0014__patient_workflow_invariants.sql` aplicadas integralmente no **MySQL 8.0.41 local / viverappweb**.
- Validação do banco e do histórico executada novamente com `tools/ViverApp.Database verify`.
- Scaffold regenerado após a aplicação; 38 entidades. Nenhuma entidade gerada foi editada manualmente.
- A coluna MySQL gerada `premium_memberships.open_account_id` precisa de `ValueGeneratedOnAddOrUpdate` no contexto parcial: o scaffold do provedor Oracle não reconheceu automaticamente esse comportamento. O banco continua sendo a fonte da estrutura.
- CPF, endereço, preferências, avaliação única, documentos privados e referência de cobrança reagendada preservados no schema Web.
- Rollbacks apenas documentados, **não executados**. Não houve ensaio destrutivo nem escrita no banco legado.
- Testes criam somente dados sintéticos identificados e removem os próprios registros. Auditoria permanece preservada. Três contas de uma fixture interrompida foram identificadas e removidas; nenhum dado real foi apagado.

## Decisões operacionais

1. Uma conta e um papel; a autorização ASP.NET usa agora também a claim de papel padrão. A validação do cookie confere o papel atual no banco. Sessões antigas sem a claim correta exigem novo login.
2. Preço e desconto sempre recalculados na API. Reagendamento conserva preço contratado, pagamento e referência original usada pelo webhook. O link de retorno antigo direciona para o atendimento sucessor.
3. Escolher pagar na clínica não confirma recebimento. Online exige pagamento pelo site. Checkout expirado não gera automaticamente uma segunda cobrança: consultar o estado e contatar a clínica quando necessário.
4. Plano inicial `Viver Premium` sem desconto inventado (0% até configuração). Promoções vazias até aprovação. A prioridade é explicada como benefício; não remove bloqueios ou conflitos da agenda.
5. Comprovantes de desenvolvimento: PDF/PNG/JPEG, até 5 MB, nome seguro, verificação de assinatura/estrutura e conteúdo ativo, CRC PNG, antivírus obrigatório e conteúdo cifrado no banco fora de wwwroot. Download exige sessão e vínculo atual. Não prometer detecção absoluta de polyglots ou malware desconhecido.
6. Scanner Windows Defender com custom scan e `-DisableRemediation`: examina apenas o arquivo recebido, sem remediar ou alterar configurações do sistema; erro/timeout bloqueiam o upload. Um scan real da logo do projeto retornou sem ameaças. O teste de workflow usa um scanner substituto controlado para testar aprovação e recusa, sem enviar documentos reais.
7. Não mover arquivos privados para CDN pública. Cloudflare R2 e armazenamento de produção permanecem na fase 15. Chaves Data Protection locais precisam ser preservadas para ler os documentos cifrados.
8. SignalR utiliza cliente oficial `@microsoft/signalr@10.0.11`, servido localmente, com licença e avisos de terceiros. Mídia WebRTC não é gravada. Sem TURN/STUN público configurado nesta etapa; conectividade por redes distintas depende da fase 17. Interface do médico permanece na fase 12.
9. Preferências de comunicação são persistidas; os envios atuais são de autenticação/segurança e não são desativados por preferências de novidades/lembretes. Workers futuros de comunicação não essencial deverão respeitá-las.
10. O laudo publicado existente permanece imutável pela API. A autoria de retificações e preservação de versões adicionais precisa ser integrada quando o fluxo correspondente do médico for implementado; não foi antecipada uma tela médica nesta fase.

## Validação e pendências de aceite

Build da solution sem erros/avisos; migrations e scaffold conferidos. Suíte de regressão: clínica 10, operações clínicas 10, identidade 37, paciente/pagamentos 41, persistência 6, segurança 6 e Web 35 — **145 testes**. O teste adicional do scanner percorre a preparação real do upload com a logo do repositório e verifica a rejeição de PNG com CRC corrompido. Resultados finais devem permanecer aprovados antes de qualquer merge.

### Validação no navegador integrado

- O bloqueio local foi resolvido sem ignorar alertas HTTPS: o certificado de desenvolvimento quebrado, sem chave privada, foi substituído por um certificado temporário confiável apenas para a homologação local. O site e a API executaram com key rings isolados em `.local`; os certificados, as chaves e os key rings temporários foram removidos ao encerrar a sessão.
- Foi corrigido um defeito real de interatividade: o `MainLayout` fazia a validação de identidade fora da fronteira interativa e deixava áreas protegidas presas em “Validando acesso”. `Routes` e `HeadOutlet` agora definem a interatividade global, sem render modes duplicados nas páginas.
- Login por e-mail/senha, home, agenda futura e histórico, detalhe com laudo/anexo, dialog de avaliação, agendamento, pagamentos, perfil/Premium e disponibilidade da sala online foram percorridos com fixture sintética. Nenhum agendamento, cancelamento, avaliação, pagamento ou upload foi submetido.
- A responsividade foi conferida em 320, 390, 412, 768, 1024, 1366 e 1920 px. Não houve overflow horizontal e a navegação alternou corretamente entre celular e desktop. O viewport de 384 px também foi usado como equivalente de reflow em ampliação de 200%; o navegador integrado não expôs uma confirmação confiável do nível de zoom nativo.
- O dialog de avaliação recebeu foco inicial, fechou por `Escape` e devolveu o foco ao botão acionador. O fluxo de erro de acesso foi ajustado para não exibir stack trace JavaScript ao usuário.
- O perfil aplica a máscara `XXXXX-XXX` durante a digitação. A validação revelou uma rota incorreta no cliente; depois da correção, `01001-000` preencheu automaticamente Praça da Sé, Sé, São Paulo/SP sem botão de busca.
- Permanecem externos: cobrança/reembolso no sandbox PagBank, entrega real de OTP por e-mail/SMS, login Google, passkey física e chamada WebRTC em duas pontas com permissão real de câmera/microfone.

### Checklist antes de encerrar a fase

- [x] Abrir o site local em navegador integrado com HTTPS confiável, sem contornar alertas de segurança.
- [x] Percorrer as telas do paciente nas larguras previstas e revisar reflow, foco de dialog e navegação por teclado.
- [x] Validar localmente login, perfil/CEP, agendamento sem submissão, agenda, pagamentos sem cobrança, laudo/anexo, avaliação sem envio, Premium e acesso elegível à sala.
- [ ] Homologar cadastro/contato/login com entrega externa de e-mail/SMS, Google e passkey física.
- [ ] Homologar checkout/retorno/reembolso no sandbox PagBank sem usar produção.
- [ ] Homologar câmera e microfone, reconexão e encerramento com duas pontas autorizadas, sem gravação.
- [ ] Validar documentos reais de teste não sensíveis (PDF/PNG/JPEG), antivírus em falha e documentos inválidos.
- [ ] Confirmar configuração da clínica, catálogo, horários, desconto e promoções, sem dados fictícios de demonstração em produção.
- [ ] Integrar e testar consumo de versões retificadas quando existir o fluxo médico correspondente; manter explícita essa dependência de paridade.

Não ativar produção automaticamente para resolver essas pendências. O acompanhamento operacional permanece no arquivo local ignorado `.local/PENDENCIAS.md`.
