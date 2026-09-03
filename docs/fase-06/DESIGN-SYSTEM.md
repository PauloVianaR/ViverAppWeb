# Viver Design System

## Inventário do MAUI consultado

Somente `ViverAppMobileNew` foi consultado. `ViverAppMobile[obsolete]` não foi aberto.

O aplicativo novo preserva do XAML:

- separação das jornadas de paciente, médico, gestor e administrador;
- navegação curta e previsível por áreas;
- cartões, status, estados vazios e skeletons;
- alvos de interação com pelo menos 44 px;
- hierarquia simples, adequada a tarefas clínicas recorrentes.

Foram deliberadamente substituídos:

- azul, verde, laranja e vermelho como identidades inteiras de cada papel, pois status semântico e papel não devem competir;
- fontes de ícones e glifos privados, substituídos por SVGs internos;
- barra inferior fixa em qualquer tamanho, substituída por navegação adaptativa;
- cores literais espalhadas pelos XAMLs, substituídas por tokens semânticos;
- telas estreitas ampliadas para desktop, substituídas por grids e densidade responsiva.

## Fundação visual

A marca usa azul-marinho como cor principal, branco como superfície e verde-petróleo como acento clínico. Sucesso, atenção e erro possuem cores próprias e nunca dependem apenas da cor: texto, ícone ou rótulo acompanha o estado.

Os tokens residem em `src/ViverApp.Web/wwwroot/app.css`:

- `--color-*`: marca, texto, superfícies, bordas e estados;
- `--space-*`: escala baseada em múltiplos de 4 px;
- `--text-*`: tipografia fluida sem impedir zoom;
- `--radius-*` e `--shadow-*`: elevação e agrupamento;
- `--motion-*`: transições curtas, anuladas por preferência de movimento reduzido.

## Componentes-base

- `AppIcon`: ícones SVG decorativos, herdando cor do contexto;
- `InterfaceState`: vazio, informação, sucesso, atenção, erro e sessão expirada;
- `LoadingSkeleton`: carregamento anunciado ao leitor de tela, sem animação quando o usuário pede movimento reduzido;
- `.button`: variantes principal, secundária, destrutiva, pequena e desabilitada;
- `.badge`: variantes de marca, sucesso, atenção e erro;
- `.field`: rótulo, campo, ajuda e validação;
- `.data-table`: tabela comparável no desktop e cartões rotulados no celular;
- `.surface`: agrupamento neutro reutilizável.

O catálogo executável está em `/design-system` e não contém dados clínicos nem ações persistentes.
