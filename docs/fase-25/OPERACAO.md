# Fase 25 — observabilidade e resposta

Este runbook é uma preparação para homologação. Não ativar alertas externos, testes ofensivos ou produção sem infraestrutura e responsáveis definidos.

## Sinais e ações

| Sinal | Limiar candidato para validar em staging | Primeira ação | Recuperação/encaminhamento |
| --- | --- | --- | --- |
| `/health/ready` indisponível | 3 falhas consecutivas em 5 min | Conferir API, MySQL e mudança recém-publicada. | Pausar deploy; usar rollback coordenado, sem reverter migration ou apagar dados automaticamente. |
| Checkout PagBank: falhas e duração | Falhas > 2% por 10 min ou p95 > 5 s com amostra mínima de 20 | Verificar status do provedor, rede e logs correlacionados por ID técnico. | Não repetir com chave diferente; preservar a mesma idempotência, conciliar estado remoto e local antes de intervenção manual. |
| Exportações analíticas | Falhas > 5% por 15 min ou fila atrasada > 30 min | Examinar lease, banco e armazenamento privado. | Reprocessar somente pelo fluxo auditado; não apagar auditoria nem disponibilizar exportação sem autorização atual. |
| Outbox/jobs | Pendências mais antigas > 15 min ou dead letters > 0 | Verificar provedor, backoff e saúde da fila. | Reprocessar pelo painel autorizado; SMSBarato não oferece idempotência remota, portanto conferir aceite antes do retry. |
| SignalR | Reconexões/salas malsucedidas acima da linha de base | Conferir origin, backplane, TURN e escalonamento. | Não criar bypass de autorização ou elevar limite de participantes. |
| Banco | p95 de consulta da agenda excede linha de base por 15 min | Rodar `analyze-hot-paths` somente leitura, comparar `EXPLAIN`, cardinalidade e locks. | Ajustar consultas/índices em migration revisada e aplicada ao MySQL local antes do scaffold. |

Os limiares são **provisórios**: ambiente local e dados alfa não produzem linha de base representativa. Confirmar proprietário do alerta, canal, horário de atendimento, janela de manutenção, orçamento de erro e procedimento de escalonamento antes de ativar.

## Comandos locais seguros

1. `dotnet run --project tools/ViverApp.Database -- status` e `-- verify` verificam schema e invariantes.
2. `dotnet run --project tools/ViverApp.Database -- analyze-hot-paths` emite planos estimados de consultas fixas, sem alterar dados.
3. `dotnet run --project tools/ViverApp.LoadProbe -- --help` mostra limites e cenários; somente loopback. Nunca usar cookie de usuário real em testes de carga.
4. `./scripts/security-check.ps1` exige restore travado, build, testes, formatação, banco, auditoria NuGet e SBOM.
5. `./scripts/dast-passive.ps1 -TargetUrl http://host.docker.internal:<porta>/ -PinnedImage 'ghcr.io/zaproxy/zaproxy@sha256:<digest>'` exige Docker e uma imagem aprovada. O host deve expor somente a aplicação local sintética para o contêiner.

Para o contêiner alcançar uma Web local que mantenha validação de Host, adicionar `host.docker.internal` apenas à variável `AllowedHosts` do **processo de homologação**, nunca ao arquivo de produção; conferir primeiro HTTP 200 com esse Host. O script retorna código 2 se o ZAP encontrar avisos, exigindo triagem do relatório antes de avançar. Não tornar esse código verde suprimindo regras sem justificativa.

## Incidente

Preservar evidências técnicas sem exportar prontuários, tokens, credenciais ou dados pessoais para logs. Conter a ação afetada, classificar escopo, conferir auditoria append-only e envolver responsável de privacidade/segurança. Decisões de comunicação ao titular/ANPD seguem o procedimento aprovado e a orientação vigente da [ANPD](https://www.gov.br/anpd/pt-br/canais_atendimento/agente-de-tratamento/comunicado-de-incidente-de-seguranca-cis); este texto não fixa prazo jurídico por conta própria.
