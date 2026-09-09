# Backlog priorizado e decisões pendentes

## 1. Backlog por risco e dependência

| Prioridade | Epic | Resultado | Fase |
|---|---|---|---:|
| P0 | Confirmar bancos e acesso seguro | `viverappmobile` somente leitura; `viverappweb` como único alvo de escrita | 2 |
| P0 | Rotação de credenciais expostas | B2/Firebase/JWT/certificado/licença e demais segredos invalidados conforme aplicável | 2–4/15/16 |
| P0 | Baseline de segurança | deny-by-default, policies, antiforgery, CSP, CORS, rate limit e logs seguros | 3 |
| P0 | Identidade segura | hash, migração, Google, MFA admin e sessões | 4 |
| P0 | Paridade funcional integral | acesso compartilhado e quatro perfis com toda capacidade MAUI rastreada/testada | 10–14 |
| P0 | Proteção de dados sensíveis | classificação, R2 privado, ownership e auditoria | 3/15 |
| P0 | Pagamento íntegro | cálculo servidor, webhook autêntico, idempotência e reconciliação | 9 |
| P0 | Agendamento concorrente | slot autoritativo e constraint/transação anti-double-booking | 7 |
| P1 | Dados mestres | clínica, catálogo, profissionais e disponibilidade | 5 |
| P1 | Design system responsivo | shell e componentes WCAG 2.2 AA | 6/10–14 |
| P1 | Jornada paciente | agenda, histórico, cancelamento/reagendamento e avaliação | 7/11 |
| P1 | Médico e gestor | operação clínica com escopo explícito | 8/12–13 |
| P1 | Workers duráveis | outbox, retry, dead-letter e métricas | 16 |
| P1 | Vídeo seguro | grant de sala, SignalR e TURN | 17 |
| P1 | Premium/financeiro | workflow e documentos protegidos | 18 |
| P1 | Administração | segregação, step-up, analytics e auditoria | 19 |
| P2 | Hardening e performance | load/chaos/DAST/pentest/SLO | 20/22 |
| P2 | Pentest Strix autorizado | white/grey/black-box isolado, correção e re-scan | 22 |
| P2 | Infra e lançamento | Cloudflare, CI/CD, Strix autorizado, rollback e cutover | 21–23 |

## 2. Decisões pendentes do proprietário

| ID | Decisão necessária | Motivo | Prazo limite |
|---|---|---|---|
| DEC-001 | Resolvida: legado `viverappmobile`; novo `viverappweb` | confirmado pelo proprietário | resolvida em 2026-09-02 |
| DEC-002 | Qual será a estratégia de convivência com o MAUI? | define compatibilidade de API e cutover | Fase 1/antes da 5 |
| DEC-003 | Onde estão configuração e comportamento do login Google atual? | não aparecem nas fontes permitidas | antes da Fase 4 |
| DEC-004 | Resolvida: cada conta possui exatamente um papel | confirmado pelo proprietário | resolvida em 2026-09-02 |
| DEC-005 | Resolvida: o sistema atende exatamente uma clínica | confirmado pelo proprietário | resolvida em 2026-09-02 |
| DEC-006 | Resolvida: médicos e gestores pertencem à clínica única | não haverá vínculo multiclínica | resolvida em 2026-09-02 |
| DEC-007 | Resolvida: o paciente cria e mantém a própria identidade; o relatório é criado pelo médico atribuído, fica imutável ao publicar, gestor/admin veem somente metadados e o paciente vê apenas o próprio relatório publicado. | risco de takeover e sigilo profissional | base implementada na Fase 8 |
| DEC-008 | Premium é vitalício, assinatura ou revalidação periódica? | afeta schema, preço e PagBank | antes da Fase 11 |
| DEC-009 | Base adotada: cancelamento e reagendamento pelo paciente com 24h de antecedência; valores configuráveis e sujeitos à validação do proprietário antes da produção. No-show permanece para a agenda operacional. | máquina de estados e financeiro | base implementada na Fase 7 |
| DEC-010 | Base adotada: timezone singleton da clínica, inicialmente `America/Sao_Paulo`; datas persistidas em UTC e horários ambíguos/inexistentes recusados. Confirmar antes da produção se haverá atendimento em outro fuso. | slots, lembretes e auditoria | base implementada na Fase 7 |
| DEC-011 | Haverá gravação de videochamada? | alto impacto LGPD/custo; recomendação inicial é não | antes da Fase 18 |
| DEC-012 | Quais provedores finais de hospedagem, MySQL, e-mail e TURN? | custo, resiliência e contratos | antes das fases correspondentes |
| DEC-013 | Qual domínio/plano Cloudflare será adquirido? | WAF, R2 custom domain e DNS | antes da Fase 15/21 |
| DEC-014 | Quais prazos de retenção para registros clínicos, anexos, financeiro, auditoria e contas? | schema, lifecycle e LGPD | antes da Fase 2/15 |
| DEC-015 | Quem é controlador, operador e encarregado LGPD? | registro de tratamento/incidentes/direitos | antes de staging com dados reais |
| DEC-016 | Qual política para contas cuja senha legada não puder ser migrada? | segurança e suporte | antes da Fase 4 |
| DEC-017 | Quais KPIs administrativos continuam úteis? | evitar migrar analytics incorretos/excessivos | antes da Fase 20 |
| DEC-018 | Qual provedor LLM, orçamento e escopo escrito serão autorizados para o Strix? | custo, privacidade do código e risco de exploração ativa | antes da Fase 24 |

## 3. Recomendações técnicas já adotadas

- monólito modular com Web e API separados;
- hosted services dentro da API, extraíveis;
- SignalR dentro da API e WebRTC no browser;
- Blazor Interactive Server com autenticação de servidor/cookie;
- MySQL 8.0.41 e EF DB-First a partir de `viverappweb`;
- e-mail e SMSBarato como canais externos; Firebase e push serão removidos;
- migrations SQL versionadas antes do scaffold;
- outbox/inbox para efeitos externos;
- Cloudflare R2 privado para documentos e CDN apenas para conteúdo público;
- lançamento incremental com reconciliação, não reescrita big bang.

## 4. Spike obrigatório por integração

Antes de implementar cada integração, a fase correspondente deve confirmar documentação e contratos vigentes:

- Google: projeto OAuth, tela de consentimento, redirect URIs, vínculo e escopos mínimos;
- PagBank: homologação, token produção, SHA-256 do webhook, estados e idempotência;
- Cloudflare: conta, região/jurisdição, R2, custom domain, WAF e cache;
- comunicação: SLA, opt-out, LGPD, sandbox e webhook de entrega;
- TURN: provedor, regiões, credenciais temporárias e custo de relay.

## 5. Critério de priorização

1. impedir perda/exposição/elevação de privilégio;
2. firmar identidade, dados e invariantes;
3. entregar jornada clínica principal;
4. integrar dinheiro, documentos, comunicação e vídeo;
5. expandir backoffice/analytics;
6. otimizar e operar em produção.

Esse backlog não autoriza iniciar a Fase 2. Cada item só entra em execução quando sua fase for solicitada em branch própria.
