# ADR 0007 — Implantação portável

- **Estado:** aceita
- **Data:** 2026-09-02

## Contexto

As connection strings Azure do legado não serão reutilizadas. O provedor final de hospedagem ainda não foi escolhido, mas a arquitetura não deve ficar bloqueada por essa escolha.

## Decisão

Empacotar Web e API como processos .NET portáveis e configuráveis por ambiente. MySQL, storage, cache/backplane e observabilidade serão acessados por contratos explícitos. Segredos virão de user-secrets apenas no desenvolvimento e de um cofre do ambiente fora do desenvolvimento.

O edge público usará domínio próprio, TLS e recursos Cloudflare adequados. A escolha do host de computação será tomada com base em região, disponibilidade, custo, backup, escala de conexões Blazor/SignalR e suporte operacional, sem assumir Azure.

## Consequências

- O desenvolvimento pode avançar antes da contratação do host.
- Infraestrutura como código, deploy sem indisponibilidade e rollback serão requisitos da fase de entrega.
- Serviços proprietários só serão adotados com ADR que registre portabilidade e custo de saída.
