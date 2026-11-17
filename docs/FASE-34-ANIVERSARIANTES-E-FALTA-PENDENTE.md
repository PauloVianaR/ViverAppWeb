# Fase 34 — Aniversariantes e falta com pagamento pendente

Solicitação de 17/11/2026. Main sincronizada com `origin/main` em `918419b`, sem alterações prévias; branch `codex/fase-34-aniversariantes-falta-pagamento`. Implementação restrita a ViverAppWeb. CodeGraph consultado antes da exploração; o índice parcial exigiu leitura direta complementar, sem reindexação. Após as validações, o proprietário autorizou o commit e a integração na main local; push, publicação e avanço de fase permanecem separados.

## Aniversariantes

- Botão **Aniversariantes do mês** na lista de pacientes do Gestor, Administrador, Médico e Psicólogo. Rotas `/{gestao|administracao|medico|psicologo}/pacientes/aniversariantes`, seguindo a autorização de cada área; Administrador mantém MFA obrigatório. Paciente não acessa a funcionalidade.
- Mês e dia corrente calculados pelo fuso da clínica, com fallback America/Sao_Paulo. A fonte é `accounts.birth_date`, usando `patient_profiles.birth_date` quando a primeira estiver vazia. Incluir pacientes internos, mesmo sem portal; excluir ausência de nascimento e datas futuras inválidas. Dia 29/02 pertence a fevereiro, mas só gera aniversário do dia em 29/02, sem conversão arbitrária para 28/02.
- Ordenação por dia do aniversário, nome e ID, paginação de 24 itens e totalizadores independentes da página. Cada card mostra dia, nome e idade que completa no ano, destaca quem faz aniversário hoje e permite abrir seu prontuário. A resposta não inclui CPF, contatos ou nascimento completo. Filtros, ordenação e contagens executados no banco; somente os registros da página são materializados.
- Aviso no sininho dos papéis internos, incluindo os dois solicitados (Gestor e Médico), com a contagem dos aniversariantes de hoje. Clicar marca somente o aviso do próprio usuário como lido e abre a tela mensal. Toast na primeira verificação positiva do dia; registro único por usuário/data impede duplicação entre acessos. Verificação a cada minuto permite acompanhar virada do dia e atualização da contagem sem recarregar. Contagem zero não produz aviso visual. Não há envio externo de e-mail/SMS.
- API autenticada em `api/v1/staff/patient-birthdays`, verificação diária por POST com proteção antiforgery existente e rate limit de operações autenticadas, leitura limitada ao proprietário do aviso e resposta sem cache.

## Não Compareceu com pagamento pendente

- Configuração **Permitir marcar Não Compareceu em atendimentos Aguardando Pagamento**, chave `appointments.allow_pending_no_show`, booleana e **false** por padrão, disponível em Administrador → Clínica → Configurações Web.
- Desativada: conserva a regra anterior. Ativada: permite marcar manualmente falta também em Aguardando Pagamento, depois de alcançado o início do atendimento. Nunca permite falta antecipada, outro profissional operar atendimento alheio ou ignorar versão concorrente/permissões.
- Gestor/Admin e Médico/Psicólogo responsáveis recebem a mesma capacidade calculada pelo servidor. Status, histórico e auditoria são atualizados; não quitar, estornar nem alterar o valor por registrar falta. A configuração não executa cancelamento em massa nem muda o job automático de datas anteriores.

## Persistência e ambiente

- Migration `0064__patient_birthdays_and_pending_no_show.sql` aplicada integralmente no MySQL **8.0.41 / viverappweb**. Adiciona somente a configuração e `patient_birthday_daily_notices`, com FK de conta e unicidade por conta/data. Rollback preparado, não executado. Scaffold atualizado e executado após a migration, sem editar manualmente entidades/contexto gerados.
- A máquina dispõe do SDK 10.0.404; o pin anterior 10.0.403 não estava mais instalado. Pin deliberadamente atualizado para **10.0.404**, mantendo `rollForward: disable`; lock files sincronizados, incluindo assets implícitos Blazor 10.0.15. Nenhum SDK ou certificado instalado/removido.
- Restore identificou vulnerabilidade alta no pacote Redis do SignalR 10.0.11. Atualizado para **10.0.15**, preservando auditoria e demais correções de dependências existentes. Referência primária: [GHSA-wr8v-9x5q-ggcm](https://github.com/advisories/GHSA-wr8v-9x5q-ggcm). Restore travado e build usam saídas isoladas em `.local/phase34-build`.

## Evidências e limites

- Build Release sem avisos/erros e formatação verificados. **216 casos selecionados aprovados**, em execuções específicas: 33 Clínicos/Gestor/Comunicação/Aniversários, 57 Identidade, 109 Web, 10 Segurança e sete Persistência. Não corresponde à suíte integral: o teste preexistente de cadastro com conexão deliberadamente inválida continua excluído e registrado nas pendências anteriores.
- Casos novos cobrem mês/dia no fuso da clínica, nascimento ausente/fallback/futuro, paginação, 29/02, mudança de data/contagem, aviso diário idempotente, leitura exclusiva, paciente sem autorização, política Admin/MFA e os quatro papéis internos. Falta pendente cobre flag desativada/ativada, futuro, ownership, versão concorrente e manutenção da obrigação financeira/histórico.
- Contrato de persistência passa a exigir 64 migrations, 74 entidades e a nova chave. Configurações administrativas mutáveis são verificadas por existência e tipo booleano; não são sobrescritas com defaults para fazer testes passarem.
- Navegador integrado do Codex, HTTP Development, Gestor e Médico: aviso no sininho, bolinha de não lido, clique abrindo a nova tela e botão de acesso na lista de pacientes. Desktop, tablet 768 px e celular 390 px sem transbordamento horizontal. Dois aniversariantes sintéticos do dia conferidos; evidência sem dados reais em `.local/phase34-qa/aviso-medico.jpg`.
- Na interface do Gestor, atendimento sintético Aguardando Pagamento não exibia falta com flag false; após ativar, a ação e sua confirmação mudaram o estado para Não Compareceu, preservando valor em aberto. Flag restaurada para **false** ao terminar.
- Limpeza seletiva confirmada: zero contas, avisos e atendimentos QA34; serviços/vínculos próprios removidos, auditoria append-only preservada. Somente servidores e aba criados para esta validação foram encerrados. Não houve cobrança, envio externo ou modificação do legado.
- Conferência visual individual de Administrador com MFA e Psicólogo permanece no registro local ignorado; políticas/rotas dos papéis estão cobertas por testes e componentes compartilhados. Não contornar MFA nem afirmar inspeção dessas sessões.
