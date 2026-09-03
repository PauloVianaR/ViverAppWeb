# Acessibilidade e responsividade

## Contrato WCAG 2.2 AA

- idioma `pt-BR`, títulos de página e landmarks semânticos;
- link “Ir para o conteúdo principal” como primeiro controle focável;
- foco visível de 3 px e sem remoção global de outline;
- controles com altura mínima de 44 px;
- campos sempre associados a rótulos e ajuda textual;
- tabelas com cabeçalhos e rótulos preservados quando viram cartões;
- estados dinâmicos com `role`, `aria-live` e texto explícito;
- ícones decorativos ocultos da árvore de acessibilidade;
- contraste mínimo automatizado de 4,5:1 para combinações de texto normal;
- `prefers-reduced-motion` e `forced-colors` respeitados;
- nenhum fluxo depende somente de hover, cor ou gesto de toque.

## Breakpoints

| Faixa | Comportamento |
|---|---|
| acima de 1024 px | barra lateral completa e áreas densas em grid/tabela |
| 737–1024 px | barra lateral compacta com ícones e conteúdo fluido |
| até 736 px | barra inferior, cabeçalho reduzido e cartões em coluna |
| até 672 px | tabelas transformadas em cartões com `data-label` |
| até 352 px | rótulos móveis truncados sem causar rolagem horizontal |

O layout utiliza `rem`, `clamp`, grids com `minmax(0, 1fr)` e larguras fluidas. Uma janela de 640 CSS px cobre o cenário equivalente a 200% de zoom em um viewport de 1280 px sem perda de função ou rolagem horizontal da página.

## Shells por perfil

- paciente: início, agendamento, agenda, pagamentos e perfil;
- médico: início, agenda, pacientes, histórico e perfil;
- gestor: visão geral, agenda, pacientes, cadastros e perfil;
- administrador: visão geral, clínica, consultas, indicadores e usuários.

A rota seleciona somente a composição visual. Toda operação continua protegida no servidor; esconder ou mostrar item de menu não é usado como controle de autorização.
