# Fase 10 — Acesso e fundação compartilhada

## Objetivo

Entregar a porta de entrada definitiva do ViverApp Web e a fundação reutilizável das quatro experiências autenticadas. Esta fase corrige o fluxo atual, no qual a interface pública oferece apenas Google, o retorno positivo abre uma rota de Gestor e o shell é inferido da URL em vez do papel autenticado.

Esta fase não implementará antecipadamente os dashboards completos de Paciente, Médico, Gestor ou Administrador. Ela entregará identidade, onboarding, aprovação profissional mínima, roteamento seguro, navegação-base e componentes compartilhados necessários às Fases 11 a 14.

**Branch prevista:** `codex/fase-10-acesso-fundacao-compartilhada`.

**Estado:** implementação concluída em 4 de setembro de 2026; aguardando integração à `main`.

## Fontes obrigatórias

- `ViverAppMobileNew/Views/General/LoginRegisterPage.xaml` e respectivo view model;
- serviços de autenticação do `ViverAppMobileNew`;
- `ViverAppApi/Controllers/AuthController.cs` e contratos relacionados;
- somente `D:\PROJETOS PROG HD\AppPublishKeys\ImagensPlayStore\PlayStore\1.jpeg` dentro da pasta `PlayStore`;
- implementação de Identity, Google, MFA, e-mail e SMS já existente no repositório Web;
- decisões das Fases 1 a 9.

`ViverAppMobile[obsolete]` é fonte proibida.

## Regras de produto

- uma conta possui exatamente um papel: Paciente, Médico, Gestor ou Administrador;
- o cadastro público oferece somente Paciente, Médico e Gestor;
- Médico e Gestor confirmados ficam aguardando aprovação administrativa;
- Paciente confirmado fica ativo;
- Administrador é provisionado por procedimento seguro e nunca por cadastro público;
- o produto atende somente uma clínica;
- Google, e-mail e SMSBarato são permitidos; Firebase/push não são;
- senha existe somente como hash adaptativo e nunca é enviada por mensagem;
- papel, status e destino pós-login são derivados no servidor.

## Experiência pública

### Landing page

- preservar a landing page, logo e apresentação institucional existentes;
- trocar a ação principal por “Entrar” ou equivalente, levando à área de acesso;
- manter uma ação secundária para conhecer os recursos;
- não iniciar OAuth automaticamente ao clicar em entrar.

### Área de acesso

A página terá navegação clara entre:

- **Entrar:** e-mail ou telefone + senha, Google e passkey quando disponível;
- **Cadastrar:** escolha Paciente/Médico/Gestor e formulário progressivo;
- **Confirmar contato:** código por e-mail ou SMS e reenvio controlado;
- **Recuperar acesso:** código por e-mail/SMS e definição de nova senha;
- **MFA:** TOTP ou recovery code para Administrador;
- **Estados da conta:** aguardando confirmação, aguardando aprovação, rejeitada, bloqueada e ativa.

O formulário preservará dados válidos ao alternar etapas, anunciará erros acessivelmente e não revelará se uma conta existe.

## Cadastro

### Escolha de papel

| Papel | Texto de apoio | Estado depois da confirmação |
|---|---|---|
| Paciente | Agendar e acompanhar atendimentos | `active` |
| Médico | Gerenciar consultas e pacientes | `pending_approval` |
| Gestor | Administrar a operação da clínica | `pending_approval` |

A API aceitará somente esses três valores no endpoint público. Alterar HTML, payload ou query string não poderá criar Administrador nem trocar um papel já atribuído.

### Dados comuns

- nome completo;
- e-mail válido quando informado;
- telefone obrigatório e normalizado para E.164;
- CPF normalizado;
- data de nascimento;
- senha e confirmação no cadastro local;
- aceite versionado dos termos e aviso de privacidade;
- escolha do canal de confirmação quando e-mail e telefone estiverem disponíveis.

### Dados específicos

| Papel | Complementos obrigatórios |
|---|---|
| Paciente | CEP, logradouro, número, complemento, bairro, cidade e UF |
| Médico | título `Dr.`/`Dra.`, CRM/UF, especialidade principal e anos de experiência |
| Gestor | dados comuns; nenhuma credencial médica inventada |

CEP terá preenchimento assistido pelo backend, timeout e edição manual. CPF, CRM, contato e idade serão revalidados no servidor.

## Google

- conta já vinculada entra e segue para o shell do papel real;
- coincidência de e-mail sem vínculo exige login existente e vínculo explícito;
- Google novo cria um onboarding incompleto, não uma conta ativa com papel arbitrário;
- o usuário escolhe Paciente, Médico ou Gestor e completa CPF, telefone e dados específicos;
- o e-mail verificado pelo Google conta como contato confirmado;
- Médico/Gestor continuam dependendo de aprovação;
- onboarding abandonado pode ser retomado com expiração e limpeza definidas;
- o retorno nunca terá `/gestao/cadastros` fixo;
- falha/cancelamento do provedor retorna somente estados públicos permitidos.

## Login, confirmação e recuperação

- um campo aceita e-mail normalizado ou telefone formatado;
- senha é verificada pelo ASP.NET Core Identity e recebe rehash quando necessário;
- confirmação e recuperação usam código aleatório, expirável, limitado por tentativas, armazenado como hash e consumido atomicamente;
- recuperação permite definir nova senha; nunca envia senha temporária;
- reenvio possui cooldown e limite por conta, destino, IP e dispositivo;
- status não ativo produz resposta segura e uma orientação compatível, sem enumeração;
- sessões podem ser vistas e revogadas;
- logout revoga/encerra corretamente a sessão atual;
- Google e passkeys podem ser vinculados somente em sessão confirmada;
- Administrador deve concluir MFA e terá sessão curta/step-up conforme policy.

## Aprovação mínima necessária

Para que Médico e Gestor possam ser aceitos nas fases seguintes, esta fase entregará uma superfície administrativa mínima e protegida para:

- listar cadastros profissionais pendentes;
- conferir dados cadastrais e profissionais necessários;
- aprovar ou rejeitar com motivo;
- notificar o resultado por e-mail/SMS;
- impedir decisão sem MFA/step-up;
- registrar autor, antes/depois, motivo, instante e correlação.

A administração completa continuará na Fase 14.

## Shell e roteamento por identidade

- carregar a conta atual no servidor antes de selecionar navegação;
- resolver shell por claim/papel revalidado, nunca pela URL;
- redirecionar login bem-sucedido para o início do papel correto;
- negar rota de outro papel mesmo que digitada diretamente;
- não renderizar conteúdo protegido antes da decisão de autorização;
- lidar com sessão expirada, revogada, MFA pendente e conta bloqueada;
- manter as cores contextuais: Paciente azul, Médico verde, Gestor laranja e Administrador vermelho;
- cor nunca será o único indicador de perfil ou estado.

## Componentes compartilhados

- card canônico de agendamento;
- badges clínicos e financeiros separados;
- filtros, busca, ordenação e paginação;
- dialogs de detalhe, confirmação, cancelamento e reagendamento;
- formulários com validação por campo e resumo de erros;
- upload com progresso e mensagens seguras;
- skeleton/loading, vazio útil, erro, acesso negado e retry;
- navegação responsiva e breadcrumbs no desktop;
- prevenção de duplo clique e comandos idempotentes.

O card de agendamento deve comportar data/hora, serviço, pessoas relevantes ao papel, modalidade, duração, local, situação clínica, situação financeira, indicação de reagendamento, preço autorizado, observações permitidas e ações contextuais.

## Banco e contratos DB-First

1. mapear lacunas no schema atual de `viverappweb`;
2. desenhar onboarding, perfis pendentes, decisão administrativa, aceite e destinos verificados;
3. criar migration SQL e rollback correspondentes sem editar migrations aplicadas;
4. validar MySQL exatamente 8.0.41 e database exatamente `viverappweb`;
5. aplicar todas as migrations pendentes;
6. verificar histórico/checksums/constraints;
7. executar scaffold DB-First;
8. manter extensões manuais fora dos arquivos gerados;
9. criar DTOs explícitos, sem expor entidades EF;
10. provar que `viverappmobile` não recebeu escrita.

## Segurança e privacidade

- antiforgery, CSP, cookies seguros e CORS restrito;
- rate limits distintos para senha, Google, passkey, código, reenvio e recuperação;
- honeypot como sinal complementar e CAPTCHA somente se risco justificar;
- proteção contra enumeração, brute force, fixation, CSRF, open redirect, account linking takeover, mass assignment e privilege escalation;
- identificadores e códigos nunca entram em logs;
- segredos somente em user-secrets/cofre;
- respostas e auditoria sem senha, token, código ou PII desnecessária;
- testes negativos para cada papel, status e combinação de ownership.

## Responsividade e acessibilidade

Validar 320, 390/412, 768, 1024, 1366 e 1920 px, além de zoom a 200%. Todos os fluxos deverão operar por teclado e leitor de tela, com foco visível, labels, mensagens anunciadas, contraste WCAG 2.2 AA, motion reduzido e dialogs com foco controlado.

## Testes obrigatórios

- cadastro local de cada papel por e-mail e por SMS;
- Google novo e existente para cada papel permitido;
- aprovação/rejeição de Médico e Gestor;
- login por e-mail e telefone;
- recuperação por ambos os canais;
- MFA, passkey, vínculo Google, sessões e logout;
- manipulação de papel, rota, IDs, retorno e status;
- concorrência em confirmação, reenvio, vínculo e aprovação;
- testes de componentes e end-to-end nos breakpoints;
- integração no MySQL 8.0.41 real.

## Critério de saída

- todos os fluxos públicos funcionam sem depender exclusivamente de Google;
- nenhuma conta vira Gestor por navegação ou retorno fixo;
- Paciente, Médico e Gestor cadastram-se com seus dados corretos;
- Médico/Gestor não entram antes da aprovação;
- cada conta e sessão possui exatamente um papel;
- shell, destino e autorização vêm da identidade real;
- componentes compartilhados e matriz de testes estão prontos para as quatro fases de perfil;
- migrations aplicadas, scaffold atualizado, build/testes/segurança aprovados;
- nenhuma implementação de perfil da Fase 11 foi antecipada.

## Entrega realizada

- área `/acesso` com senha, Google, código por e-mail/SMS, recuperação, passkey, confirmação de contato, MFA e onboarding por papel;
- cadastro local e Google para Paciente, Médico e Gestor, com validação server-side de CPF, idade, telefone, endereço, CRM, especialidade, aceite e honeypot;
- onboarding Google temporário protegido e expirável, sem criação automática de Paciente ou Gestor;
- estados `pending_confirmation`, `pending_approval` e `active` aplicados de acordo com papel e confirmação;
- superfície `/administracao/aprovacoes`, protegida por papel administrativo e MFA, com aprovação/rejeição, concorrência otimista, auditoria e notificação por outbox;
- shell carregado pelo papel retornado por `/api/v1/auth/me`, bloqueando a renderização de áreas protegidas antes da validação e corrigindo destinos pós-login;
- página `/seguranca` para vínculo Google, cadastro/remoção de passkeys e consulta/revogação de sessões;
- card canônico de agendamento e componentes reutilizáveis para código, desafios, loading e estados de interface;
- busca assistida de CEP pelo backend, com limite de tempo, rate limit e possibilidade de edição manual;
- migration `0011`, rollback simétrico, migration aplicada no MySQL 8.0.41 e scaffold DB-First regenerado;
- build sem avisos, suíte completa executada de forma serial e validação visual em desktop e 390 px.

Nenhuma tela funcional completa da experiência do Paciente prevista para a Fase 11 foi adicionada nesta entrega.
