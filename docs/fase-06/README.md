# Fase 6 — Design system e shell web responsivo

## Resultado

O Blazor agora possui uma linguagem visual própria para o Centro Médico Viver, derivada da nova marca oficial e preparada para celular, tablet, notebook e desktop largo. A estrutura deixa de reproduzir literalmente as barras inferiores do MAUI: em telas amplas usa navegação lateral, reduz essa navegação a ícones no tablet e retorna à barra inferior no celular.

Foram criadas páginas-base para paciente, médico, gestor e administrador. A escolha visual do shell nunca concede permissão nem substitui as policies: os dados reais continuam dependendo de sessão válida, MFA quando exigido e autorização da API.

## Entregas

- logo oficial publicada em `wwwroot/images/logo.png`, usada no shell, na home e como favicon;
- tokens semânticos de cor, espaçamento, tipografia, raio, sombra e movimento;
- ícones SVG internos, sem fonte externa ou requisição a terceiro;
- componentes reutilizáveis para feedback e skeleton;
- home pública redesenhada;
- shells adaptativos para paciente, médico, gestor e administrador;
- tabelas que se convertem em cartões rotulados no celular;
- estados de carregamento, vazio, erro, conexão degradada e sessão expirada;
- erro, página inexistente e reconexão localizados e sem exposição de detalhes internos;
- foco visível, skip link, landmarks, redução de movimento e suporte a cores forçadas;
- catálogo navegável em `/design-system` e testes de contrato do HTML e contraste.

## Limite funcional

As páginas-base não inventam prontuários, consultas, valores ou contagens. Elas preparam os recipientes visuais para as jornadas das próximas fases. Navegar diretamente para um shell não autoriza acesso a dados; a API permanece deny-by-default.

Consulte [DESIGN-SYSTEM.md](DESIGN-SYSTEM.md), [ACESSIBILIDADE-E-RESPONSIVIDADE.md](ACESSIBILIDADE-E-RESPONSIVIDADE.md) e [VERIFICACOES.md](VERIFICACOES.md).
