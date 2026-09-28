# Fase 25 — robustez, desempenho e segurança ofensiva

## Estado e limites

Esta fase introduz instrumentos de medição e endurecimento verificáveis no ambiente local. **Ainda não satisfaz o critério de saída da fase**: a carga autenticada, os provedores externos, DAST, revisão independente, assinatura de artefatos e SLOs sob tráfego representativo dependem de um ambiente de homologação e/ou pessoas autorizadas. Não inferir aprovação desses itens a partir de testes unitários ou de uma medição em loopback.

O MySQL usado foi exclusivamente o `viverappweb` local 8.0.41. Nenhum banco descartável, cobrança real ou dados clínicos reais foram usados.

Em 28/09/2026, `scripts/security-check.ps1` passou integralmente: build Release com zero avisos/erros, **253 testes** aprovados, `dotnet format --verify-no-changes`, migrations 0001–0048 aplicadas, verificação do MySQL 8.0.41 e auditoria NuGet sem vulnerabilidades reportadas. Treze arquivos com dívida de whitespace preexistente foram formatados mecanicamente para que o portão global passasse; não houve alteração intencional de lógica nesses arquivos. O SBOM contém 122 pacotes em 13 projetos. Gerar novamente depois do commit e em cada release para vincular o artefato ao commit correspondente.

No navegador integrado, a landing pública foi inspecionada em larguras de 1280, 820 e 390 px: cabeçalho, chamada principal e ação de cadastro permaneceram legíveis, com `scrollWidth` não excedendo a largura útil; existe `main` e exatamente um `h1`. Isso é uma amostra visual da superfície pública, não uma validação de todos os papéis, fluxos privados, zoom ou engines de navegador. Durante o início local houve mensagens de chaves antigas Data Protection não decifráveis por DPAPI; nenhuma chave/certificado foi removido, e a página pública respondeu. O anel de chaves da implantação precisará de revisão antes de produção.

## Implementado

| Área | Entrega | Evidência e alcance |
| --- | --- | --- |
| Agenda | Uma projeção resumida substitui as múltiplas leituras completas para indicadores; a página preserva filtros e ordenação. | Testes de operações clínicas e agendamento; medir novamente sob carga representativa. |
| Banco | Migration `0048__appointment_calendar_range_index.sql` cria `(starts_at_utc, id)`; scaffold DB-First regenerado. | `analyze-hot-paths` usa `EXPLAIN` de consultas fixas, sem mutação. Antes: agenda clínica via `ux_appointments_doctor_start`, acesso `index`, estimativa 12 linhas; depois: `ix_appointments_start_id`, acesso `range`, estimativa 1 linha na amostra local. A estimativa não é um benchmark de produção. |
| Checkout | Chave de idempotência do provedor estável para paciente, atendimento e versão do registro; timeout após aceite remoto pode ser repetido com a mesma chave. | Teste de integração no MySQL local simula primeiro timeout e segundo sucesso, sem pagamento duplicado. Não substitui homologação PagBank Sandbox. |
| Observabilidade | Métricas OTel de tentativas/duração de checkout e exportação analítica, somente com rótulo `success`/`failure`. | Sem CPF, nome, token ou identificador de paciente como atributo. Coletor e alertas externos pertencem à infraestrutura futura. |
| Carga local | `tools/ViverApp.LoadProbe` limita alvo a loopback, duração, concorrência e quantidade; cenários `api-ready`, `landing` e `agenda` com cookie sintético fornecido pelo operador. | API pronta: 6.171 respostas/10 s, 8 clientes, nenhuma falha, p95 23,61 ms. Landing com espaçamento para não induzir rate limit: 78 respostas/10 s, 8 clientes, nenhuma falha, p95 78,97 ms. Hardware e dados locais; não extrapolar para produção. Agenda autenticada, login, checkout, SignalR e workers não medidos. |
| Dependências | Lock files NuGet, restore em `--locked-mode`, auditoria de pacotes transitivos e configuração semanal de Dependabot. | A auditoria local não retornou vulnerabilidades conhecidas na data da execução; atualizar a checagem antes de release. Dependabot só operará após o repositório ser hospedado com a funcionalidade habilitada. |
| Inventário | `scripts/generate-sbom.ps1` gera CycloneDX 1.6, hashes SHA-512 de pacotes e digest SHA-256 do documento, ligado a commit/estado Git. | Saída em `artifacts/` ignorada; digest não é assinatura. Gerar novamente após o commit final e em cada release. |
| DAST | `scripts/dast-passive.ps1` prepara ZAP Baseline em alvo local explícito, com imagem fixada por digest e relatório. | Apenas scan passivo; não foi executado porque Docker não está disponível. Scan ativo só em staging isolado, com autorização e escopo próprios. |

## Portões de segurança

O padrão de referência é **OWASP ASVS 5.0.0**, com versão explícita. A revisão deve cobrir, com evidências por requisito aplicável: autenticação/MFA e recuperação; sessão e CSRF; autorização por papel e ownership; validação/saída/CSP; dados clínicos, upload e R2 privado; criptografia/segredos; pagamento/webhook/idempotência; API/SignalR; logs/auditoria; disponibilidade e configuração. Os testes atuais são evidência parcial, **não** uma avaliação ASVS concluída. Uma pessoa independente da implementação deve registrar achados, severidade, reprodução e reteste. Achados críticos/altos abertos bloqueiam o critério de saída.

DAST Baseline é passivo e não prova ausência de falhas. Antes do Strix, executar também DAST autenticado convencional em staging isolado, com contas sintéticas de todos os papéis, regras de exclusão de operações destrutivas e relatório revisado. Não apontar scanner para produção, provedores reais ou endpoints de pagamento sem autorização específica.

## Critérios de desempenho e operação a homologar

Medir separadamente login, agenda/consulta, checkout Sandbox, conexões/reconexões SignalR, processamento de outbox/jobs e downloads privados. Usar cargas por perfil, período de aquecimento, tamanho de base, concorrência, taxa, ambiente e percentis p50/p95/p99 registrados. Definir SLOs formais com o proprietário e a infraestrutura antes de declarar sucesso. A primeira proposta de alerta e resposta está em [OPERACAO.md](OPERACAO.md); os valores são candidatos, não metas aprovadas nem atendidas.

## Conformidade e experiência

Revisar inventário de dados, base legal, retenção, direitos do titular, contratos de operadores e fluxo de incidente com profissional jurídico responsável. A revisão técnica não equivale a conclusão jurídica LGPD. Reexecutar testes automatizados de acessibilidade e conferir jornadas nos tamanhos desktop/tablet/celular no navegador integrado; compatibilidade entre engines requer navegadores adicionais em homologação. Não declarar WCAG ou compatibilidade plena com base em uma inspeção única.

## Fontes normativas e operacionais

- [OWASP ASVS](https://owasp.org/projects/asvs/)
- [ZAP Baseline](https://www.zaproxy.org/docs/docker/baseline-scan/)
- [NuGet lock files](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files)
- [ANPD — guia de cookies](https://www.gov.br/anpd/pt-br/centrais-de-conteudo/materiais-educativos-e-publicacoes/guia_orientativo_cookies_e_protecao_de_dados_pessoais)
