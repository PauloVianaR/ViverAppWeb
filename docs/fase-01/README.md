# Fase 1 — Descoberta funcional, riscos e arquitetura executável

**Status:** concluída em 2 de setembro de 2026  
**Branch:** `codex/fase-01-descoberta`  
**Natureza:** documentação e decisões; nenhum código funcional ou banco foi criado.

## Resultado

A fase transformou o comportamento observável do legado permitido em requisitos, riscos, fronteiras de módulos e decisões verificáveis para a reimplementação. O material não copia classes ou contratos legados: ele descreve o que precisa ser preservado, corrigido, validado ou descartado antes do desenho de `viverappweb`.

## Evidências consultadas

Somente estas fontes foram consultadas:

- `ViverAppMobileNew` para jornadas, comandos, navegação e uso da API no MAUI;
- `ViverAppApi` para endpoints e regras existentes;
- `ViverAppEmailWorker` para inventariar os fluxos legados de e-mail, SMS e push; e-mail e SMSBarato seguirão para o novo produto, sem Firebase/push;
- `ViverAppVideoHub` para sinalização WebRTC/SignalR;
- `ViverApp.Shared` para o scaffold DB-First e contratos legados.

`ViverAppMobile[obsolete]` não foi consultada nesta fase. A raiz ainda não possui `.codegraph/`; por isso CodeGraph não pôde ser usado e a exploração foi feita por busca textual e leitura direcionada. Essa limitação deve ser removida assim que o usuário adicionar o índice.

Não houve conexão ao servidor MySQL: o cliente `mysql` não está instalado no ambiente e a Fase 1 não exige validação viva ou escrita. O inventário de dados foi inferido do `DbContext` DB-First legado e deverá ser confrontado, em modo somente leitura, com o schema real na Fase 2.

## Medidas do legado permitido

| Área | Quantidade observada |
|---|---:|
| Controllers da API | 17 |
| Endpoints HTTP | 111 |
| `DbSet` no contexto legado | 22 |
| Telas/popup XAML | 46 |
| View models MAUI | 33 |
| Workers de servidor | 3 |
| Hub SignalR | 1 |

## Entregas

- [Inventário do legado](INVENTARIO-LEGADO.md)
- [Jornadas e requisitos](JORNADAS-E-REQUISITOS.md)
- [Matriz de rastreabilidade](MATRIZ-DE-RASTREABILIDADE.md)
- [Arquitetura e diagramas](ARQUITETURA-E-DIAGRAMAS.md)
- [Modelo conceitual de dados](MODELO-CONCEITUAL.md)
- [Threat model, segurança e LGPD](THREAT-MODEL-E-LGPD.md)
- [Estratégia de testes e ambientes](ESTRATEGIA-DE-TESTES-E-AMBIENTES.md)
- [Backlog e decisões pendentes](BACKLOG-E-DECISOES.md)
- [Registros de decisão arquitetural](../adr/)

## Descobertas que bloqueiam suposições futuras

1. O proprietário confirmou `viverappmobile` como banco legado somente leitura e `viverappweb` como banco novo e único alvo de escrita.
2. Não foi encontrada implementação de login Google em `ViverAppMobileNew`, API, worker, hub ou shared permitidos. Só foi encontrada integração Google ligada ao Firebase push legado, que não será migrado. A experiência desejada permanece requisito da Fase 4, mas client IDs, redirect URIs, vínculo de contas e comportamento legado precisam ser fornecidos ou redescobertos em configuração externa.
3. Há segredos/licenças no código e nos artefatos locais do legado. Seus valores não foram reproduzidos. Devem ser rotacionados antes de qualquer uso produtivo novo.
4. Diversas regras essenciais vivem no cliente MAUI, especialmente cálculo de slots, descontos e preços. No sistema novo, a API será a única autoridade dessas decisões.
5. O legado autentica requisições, mas praticamente não demonstra autorização por recurso/ownership. IDs fornecidos pelo cliente atravessam várias operações críticas.

## Limite comprovado da fase

- `viverappweb` **não** foi criado;
- nenhuma migration foi criada ou executada;
- nenhum scaffold EF foi gerado;
- nenhum model, contrato, endpoint, autenticação, worker ou integração foi implementado;
- a Fase 2 não foi iniciada.

## Gate para a Fase 2

A Fase 2 só deve começar após ordem expressa do usuário e em nova branch. Antes de qualquer comando de banco, ela deve confirmar MySQL 8.0.41, verificar `SELECT DATABASE()` programaticamente e garantir que qualquer consulta a `viverappmobile` seja somente leitura e toda escrita ocorra exclusivamente em `viverappweb`.
