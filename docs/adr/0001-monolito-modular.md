# ADR 0001 — Monólito modular

- **Estado:** aceita
- **Data:** 2026-09-02

## Contexto

O legado distribui responsabilidades entre aplicativo MAUI, API, workers e hub de vídeo. A nova solução precisa reduzir a complexidade operacional sem transformar todo o produto em um único bloco acoplado.

## Decisão

Adotar um monólito modular: `ViverApp.Web` e `ViverApp.Api` são processos implantáveis separados, enquanto casos de uso, domínio e persistência serão organizados por módulos de negócio com dependências explícitas. Os módulos iniciais serão Identidade, Cadastros, Agenda, Pagamentos, Arquivos, Comunicação, Vídeo e Administração.

Contratos HTTP não exporão entidades do EF. Um módulo não acessará diretamente tabelas internas de outro; integrações internas ocorrerão por interfaces/casos de uso ou eventos persistidos.

## Consequências

- Uma única API simplifica transações, implantação e observabilidade no início.
- Limites modulares permitem extrair um processo somente quando métricas justificarem.
- Testes de arquitetura deverão impedir dependências cíclicas e acesso indevido entre módulos.
