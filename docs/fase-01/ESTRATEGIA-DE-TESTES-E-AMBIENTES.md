# Estratégia de testes e ambientes

## 1. Princípios

- testar comportamento e riscos, não detalhes do legado;
- nenhum teste substitui autorização no servidor;
- banco de teste deve ser MySQL 8.0.41 real e isolado; SQLite é proibido como substituto;
- dados são sintéticos, sem cópia de pacientes, documentos ou credenciais;
- testes de integração externa usam sandbox/fakes contratuais até autorização de produção;
- bug corrigido recebe teste de regressão;
- cada fase executa somente seus gates e não antecipa implementação posterior.

## 2. Camadas de teste

| Camada | Escopo | Exemplos |
|---|---|---|
| Unidade | invariantes e funções puras | transição, preço, slot, retry, normalização |
| Componente | módulo com adapters falsos | políticas, casos de uso, templates |
| Integração MySQL | queries, constraints, transações, outbox e migrations | double booking, claim concorrente, idempotência |
| Contrato HTTP | schema/status/headers/Problem Details/OpenAPI | paginação, validação, compatibilidade |
| Contrato externo | requests/responses/webhooks | PagBank sandbox, R2 S3, Google OIDC metadata |
| End-to-end | browser + API + MySQL + fakes | login, agendar, pagar, concluir, MFA admin |
| Segurança | abuso e controles negativos | IDOR, CSRF, XSS, SSRF, replay, upload, rate limit |
| Acessibilidade/visual | componentes e páginas em breakpoints | WCAG 2.2 AA, teclado, zoom 200% |
| Performance | carga, soak e concorrência | slots, agenda, checkout, hub e workers |
| Resiliência | falhas e recuperação | crash de worker, timeout externo, restart, restore |

## 3. Matriz mínima por capacidade

| Capacidade | Testes indispensáveis |
|---|---|
| Identidade | hash/rehash, brute force, enumeração, vínculo Google, sessão revogada, MFA/recovery |
| Autorização | cada papel × operação × próprio recurso × recurso alheio × clínica alheia |
| Agenda | limite/feriado/timezone/sobreposição, requisições simultâneas e idempotência |
| PagBank | assinatura inválida, replay, duplicidade, fora de ordem, valor/status divergente e reconciliação |
| Documentos | MIME falso, arquivo grande/malicioso, path/key, acesso cruzado, expiração e cache |
| Workers | claim concorrente, retry/jitter, dead-letter, crash antes/depois do provedor |
| Vídeo | acesso sem grant, sala alheia, flood, payload inválido, queda/reentrada, duas instâncias |
| Admin | MFA ausente, step-up expirado, elevação de privilégio e auditoria íntegra |

## 4. Ambientes

| Ambiente | Banco | Provedores | Dados | Uso |
|---|---|---|---|---|
| Local | `viverappweb` local em MySQL 8.0.41 | fakes/sandbox | sintéticos | desenvolvimento |
| CI | database efêmero exclusivo em MySQL 8.0.41 | fakes e contratos estáveis | seeds sintéticos | pipeline por commit/PR |
| Desenvolvimento compartilhado | `viverappweb` isolado do local/prod | sandbox | sintéticos | integração da equipe |
| Staging | clone estrutural, sem dados reais por padrão | sandbox/homologação | sintéticos representativos | E2E, carga segura e UAT |
| Produção | database exclusivo e protegido | credenciais produção | reais minimizados | tráfego autorizado |

Nunca apontar teste/migration para `viverappmobile`. O nome do database deve ser assertado antes de cada suite destrutiva.

## 5. Pipeline de uma fase

1. confirmar branch e escopo da fase;
2. consultar CodeGraph quando disponível;
3. restaurar com lock de dependências quando adotado;
4. compilar com warnings como erros;
5. executar format/analyzers;
6. executar testes de unidade e componente;
7. iniciar MySQL 8.0.41 isolado;
8. listar migrations pendentes;
9. revisar/aplicar todas as migrations SQL em `viverappweb`;
10. scaffold DB-First e verificação de diff quando a fase alterar schema;
11. executar integração/contrato/E2E relevantes;
12. SAST, SCA, secret scan e testes de segurança da superfície afetada;
13. gerar evidências e parar sem iniciar a próxima fase.

## 6. Gate de migrations DB-First

- backup/rollback definido quando houver risco;
- checksum e ordem das migrations imutáveis;
- credencial de migration separada da credencial de runtime;
- `SELECT VERSION()` compatível com 8.0.41;
- `SELECT DATABASE()` exatamente `viverappweb`;
- histórico antes/depois registrado;
- execução repetida resulta em zero pendências, sem reaplicar;
- scaffold determinístico não contém segredo na connection string;
- integração passa com o schema recém-criado do zero e com upgrade do anterior.

## 7. Navegadores e visual

Matriz inicial: versões estáveis correntes de Chromium, Firefox e Safari/WebKit, além de viewport de telefone, tablet, notebook e desktop largo. Validar teclado, leitor de tela, contraste, modo de alto contraste, zoom 200%, latência/reconexão do Blazor e redução de movimento.

## 8. Observabilidade como teste

Cada fluxo crítico deve emitir correlação e métricas verificáveis sem PII/segredo. Testes devem afirmar:

- ausência de senha/token/documento nos logs;
- eventos de auditoria para ações críticas;
- métricas de erro/latência/fila;
- alertas para falha repetida, backlog, webhook inválido e indisponibilidade;
- traces não carregam bodies sensíveis.

## 9. Saída mínima por fase

O relatório deve listar versão do SDK/MySQL, branch, migrations aplicadas, suites executadas, resultado, exceções autorizadas e riscos remanescentes. “Não executado” nunca pode ser apresentado como sucesso.
