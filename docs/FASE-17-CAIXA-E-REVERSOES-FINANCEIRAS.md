# Fase 17 — Caixa e reversões financeiras

## Estado e objetivo

**Estado:** implementada em 10 de setembro de 2026 na branch `codex/fase-17-caixa-reversoes`; validações automatizadas concluídas. A homologação visual autenticada no monitor 3 e as integrações externas deliberadamente não acionadas permanecem registradas no arquivo local de pendências.

**Branch executada:** `codex/fase-17-caixa-reversoes`, criada a partir da `main` após a integração da Fase 16 pelo merge `f59755c`.

Implementar um livro-caixa diário, histórico e auditável para Gestor e Administrador, incluindo totais por forma de pagamento, impressão, cancelamento/estorno de pagamentos e novo pagamento para o mesmo atendimento sem apagar o histórico.

Esta fase não implementa o prontuário eletrônico nem inicia os workers gerais da Fase 19.

## Regras invariáveis

- alterar somente `ViverAppWeb`;
- usar exclusivamente `viverappweb`; `viverappmobile` permanece somente leitura;
- seguir DB-First e aplicar integralmente a migration no MySQL local 8.0.41 antes do scaffold;
- nunca editar entidades/contexto gerados;
- valores, métodos, estados, totais e elegibilidade são calculados no servidor;
- o caixa e os eventos financeiros são append-only;
- nenhuma operação apaga ou reescreve pagamento/movimento para esconder histórico;
- operações críticas usam autorização, antiforgery, idempotência, concorrência e auditoria;
- PagBank de produção não será acionado nem receberá estorno real sem autorização explícita;
- nenhuma funcionalidade da Fase 18 será antecipada.

## Modelo financeiro

### Livro-razão diário

O caixa representa todas as movimentações financeiras da clínica agrupadas pela data operacional configurada. Cada movimento possui direção, valor, moeda, método, tipo, origem, responsável e vínculo com o atendimento/pagamento quando aplicável.

Movimentos nunca são editados ou excluídos. Uma correção gera um movimento compensatório relacionado ao anterior.

### Tipos mínimos

- pagamento recebido;
- cancelamento/estorno de pagamento;
- suprimento/entrada manual autorizada;
- sangria/saída manual autorizada;
- ajuste corretivo compensatório;
- eventual tarifa/ajuste do provedor somente quando reconciliado e suportado pelo contrato.

Suprimento, sangria e ajuste exigem valor, motivo, confirmação e responsável. O sistema não aceitará valor zero, sinal arbitrário ou método desconhecido.

### Formas de pagamento

Totais separados, no mínimo, por:

- dinheiro;
- Pix;
- cartão de débito;
- cartão de crédito;
- PagBank online;
- outras formas cadastradas e validadas no servidor.

Dados completos de cartão nunca serão armazenados. Últimos quatro dígitos e referência/autorização continuam redigidos e opcionais conforme o método.

### Data operacional

- derivada do instante UTC usando o fuso configurado da clínica;
- nunca confiada ao navegador;
- consulta histórica por dia sem permitir alterar a data de um movimento existente;
- totais calculados sobre todo o filtro do dia, independentemente da paginação;
- fechamento diário não modifica os lançamentos anteriores.

## Tela do caixa

Nova navegação **Caixa** nos perfis de Gestor e Administrador.

### Cabeçalho e filtros

- seletor de data com atalho Hoje e navegação anterior/seguinte;
- estado do dia e responsável pelo fechamento, se houver;
- saldo inicial quando adotado, entradas, saídas/reversões e líquido;
- filtros por método, tipo de movimento, número do atendimento, Paciente e responsável;
- busca limitada/paginada e botão para limpar filtros;
- indicação explícita quando a data não possui movimentos.

### Lista

- tabela no desktop e cards no celular;
- horário, número do atendimento, descrição, método, direção, valor e responsável;
- vínculo navegável com pagamento/atendimento mediante autorização;
- identificação clara de original, reversão e pagamento substituto;
- movimentos posteriores ao fechamento destacados sem alterar o fechamento anterior;
- valores monetários em BRL, com formatação consistente e alternativa acessível.

### Totais

- total bruto de entradas;
- total de estornos/reversões;
- total de suprimentos, sangrias e ajustes;
- líquido do dia;
- decomposição por forma de pagamento;
- contagem de movimentos por grupo;
- totais recalculados pelo servidor com `decimal`, precisão e arredondamento definidos no domínio.

## Fechamento do dia

- Gestor ou Administrador autorizado pode registrar o fechamento;
- resumo imutável com data, totais, contagens, responsável e instante;
- confirmação explícita e comparação com a versão corrente;
- fechamento não impede movimentação legítima posterior;
- correções pós-fechamento ficam destacadas e exigem motivo;
- operação pós-fechamento de alto impacto exige step-up do Administrador;
- reimpressão deixa claro se o relatório corresponde ao fechamento ou ao estado atualizado.

## Impressão

No fim da tela existirão exatamente duas ações principais.

### Imprimir

Imprime:

- identificação aprovada da clínica;
- data operacional, filtros e momento da emissão;
- todas as movimentações que compõem a consulta do dia;
- originais e reversões identificados;
- totais gerais e por forma de pagamento;
- responsável pela emissão e paginação.

### Imprimir Totais

Imprime somente:

- identificação da clínica e data;
- entradas, estornos/saídas e líquido;
- totais separados por forma de pagamento;
- contagens, estado do fechamento e responsável pela emissão.

### Regras de impressão

- layout A4 específico, sem navegação, botões ou controles interativos;
- cabeçalhos repetidos e quebras de página previsíveis;
- diálogo nativo do navegador, permitindo impressão física ou “Salvar como PDF”;
- nenhum arquivo temporário público no R2/CDN;
- CPF, endereço, conteúdo clínico e dados completos de pagamento não aparecem;
- impressão reproduz o filtro confirmado pelo servidor, não uma soma refeita no browser;
- saída acessível também em tela antes de imprimir.

## Cancelamento e reversão de pagamento

Na agenda e no detalhe de atendimento pago, Gestor e Administrador terão **Cancelar pagamento** quando a policy e o estado permitirem.

### Fluxo comum

1. exibir valor, método, data e referência redigida;
2. exigir motivo e confirmação explícita;
3. exigir policy financeira; Administrador usa step-up MFA recente;
4. enviar chave idempotente e `row_version`;
5. criar pedido de reversão e movimento compensatório vinculado;
6. atualizar atendimento/caixa somente conforme o estado confirmado;
7. habilitar novo pagamento quando não existir pagamento ativo/quitado não revertido.

Cancelar pagamento não cancela automaticamente o atendimento. Cancelar atendimento não simula estorno: a interface informa separadamente quando há valor que exige ação financeira.

### Pagamento presencial/manual

- reversão local atômica;
- movimento negativo no caixa ligado ao recebimento original;
- status terminal explícito como `reversed`;
- motivo, responsável e instante obrigatórios;
- novo pagamento permitido após a transação confirmada.

### PagBank

- usar a operação oficial de cancelamento/estorno adequada ao estado do checkout/cobrança;
- estado intermediário `reversal_pending` enquanto o provedor não confirmar;
- não registrar reversão efetiva nem permitir pagamento substituto prematuramente;
- webhook/reconciliação confirma `reversed`/`refunded` de forma idempotente;
- falha/retry não duplica pedido, movimento ou reembolso;
- divergência vai para tratamento operacional, sem esconder o pagamento original;
- sandbox/fake por padrão; produção exige autorização específica.

## Novo pagamento após reversão

- cada tentativa é um novo registro de pagamento;
- original, reversão e substituto permanecem encadeados;
- no máximo um pagamento ativo ou quitado não revertido por atendimento;
- preço elegível é recalculado no servidor conforme a regra vigente/aplicável;
- método pode ser diferente do anterior;
- pagamento manual exige os mesmos dados e auditoria já existentes;
- checkout online cria uma nova referência idempotente;
- o caixa mostra cada entrada e cada compensação, sem apresentar somente o líquido ocultando os fatos.

## Banco DB-First planejado

A migration sequencial seguinte às migrations aplicadas da Fase 16 deve contemplar, conforme análise do schema real:

- livro-razão `cash_movements` ou estrutura equivalente;
- data operacional, tipo, direção, método, moeda e valor;
- vínculo com atendimento, pagamento, responsável e movimento relacionado;
- fechamento/resumo diário imutável;
- reversões e seus estados/transições;
- histórico de múltiplas tentativas de pagamento por atendimento;
- garantia de no máximo um pagamento ativo/quitado não revertido;
- índices por data, método, atendimento, pagamento e responsável;
- chaves idempotentes, timestamps UTC, constraints monetárias e `row_version`;
- proteção contra `UPDATE/DELETE` de movimentos, fechamentos e eventos financeiros.

Não criar tabelas de prontuário nesta fase. Aplicar a migration integralmente em `viverappweb` no MySQL local 8.0.41 e só depois regenerar o EF DB-First.

## API e contratos planejados

- resumo diário do caixa por filtro;
- movimentos paginados e totais globais do filtro;
- registrar suprimento, sangria e ajuste compensatório;
- fechar/reconsultar dia;
- produzir visão de impressão completa e de totais;
- solicitar e consultar reversão;
- registrar novo pagamento após reversão;
- consultar cadeia original → reversão → substituto;
- localizar por número humano do atendimento com autorização revalidada.

DTOs não expõem entidades EF. Totais e relatórios são calculados no servidor; queries possuem limites, cancelamento e índices medidos.

## Segurança obrigatória

- policies distintas para consultar caixa, movimentar, fechar, reverter e imprimir;
- step-up MFA do Administrador para reversões e pós-fechamento;
- confirmação/idempotência para toda mutação financeira;
- antiforgery no BFF e rate limiting por ator/ação;
- concorrência otimista e transações no nível correto;
- proteção contra IDOR, mass assignment, manipulação de valor/método/status e replay;
- nenhum PAN/CVV, segredo PagBank, PII completa ou payload de webhook em log;
- auditoria append-only com ator, motivo, antes/depois, correlação e resultado;
- `no-store` para caixa, impressão e detalhe financeiro;
- falhas externas não são apresentadas como sucesso;
- limpeza de teste preserva auditoria conforme as proteções do projeto.

## Plano de execução

1. consultar CodeGraph, schema, pagamentos, agenda e policies atuais;
2. fechar invariantes de ledger, fechamento, reversão e pagamento substituto;
3. criar/revisar/aplicar migration no `viverappweb` MySQL 8.0.41;
4. regenerar EF DB-First e revisar o diff;
5. implementar ledger, agregações, reversões e testes de concorrência;
6. implementar APIs/policies e testes negativos;
7. integrar cancelamento e novo pagamento às agendas/detalhes de Gestor/Admin;
8. implementar Caixa responsivo, filtros e fechamento;
9. implementar as duas visões de impressão;
10. integrar PagBank apenas em sandbox/fake autorizado;
11. atualizar documentação/matriz e registrar evidências;
12. executar build, suíte integral, testes MySQL, financeiros e de segurança;
13. validar exclusivamente no monitor 3, incluindo impressão A4;
14. parar sem iniciar a Fase 18.

## Testes mínimos

### Invariantes e concorrência

- movimentos e fechamentos recusam alteração/exclusão;
- duas confirmações do mesmo pagamento geram uma entrada;
- duas reversões concorrentes geram uma compensação;
- novo pagamento só é aceito após reversão efetiva;
- dois novos pagamentos concorrentes não ficam ativos ao mesmo tempo;
- retry/webhook PagBank não duplica reembolso ou movimento;
- fechamento e movimento concorrentes permanecem reconciliáveis.

### Totais e impressão

- cada método totaliza corretamente;
- entradas, reversões, suprimentos, sangrias, ajustes e líquido fecham matematicamente;
- paginação não altera totais;
- mudança de data/fuso classifica o movimento corretamente;
- Imprimir contém movimentos e totais;
- Imprimir Totais não contém movimentos individuais;
- A4 não corta valores/linhas essenciais em celular ou desktop.

### Autorização

- Médico/Paciente não acessam o caixa;
- Gestor/Admin sem policy/step-up recebem recusa;
- alterar IDs ou número humano não amplia escopo;
- valor, método, status e responsável forjados são recusados;
- relatórios não vazam CPF/endereço/conteúdo clínico;
- logs e auditoria preservam sigilo e rastreabilidade.

## Cenário ponta a ponta obrigatório

1. Gestor recebe um pagamento presencial e o caixa registra a entrada.
2. Gestor consulta o mesmo movimento pelo dia, método e número do atendimento.
3. O pagamento é cancelado com motivo e gera uma reversão vinculada.
4. O atendimento volta a permitir pagamento; um segundo método é registrado.
5. O caixa preserva os três fatos e apresenta o total líquido correto.
6. Administrador fecha o dia e executa Imprimir e Imprimir Totais.
7. Uma correção pós-fechamento exige step-up e aparece destacada.
8. Cenário equivalente de PagBank em sandbox confirma estados, webhook e retries sem duplicidade.

## Critérios de saída

- Caixa existe para Gestor/Admin com dia atual, histórico, filtros e totais por método;
- Imprimir e Imprimir Totais produzem exatamente os recortes definidos;
- cancelamento de pagamento é compensatório, imutável e auditável;
- novo pagamento é possível somente após reversão efetiva;
- pagamentos presenciais e PagBank respeitam seus fluxos próprios;
- migration foi aplicada no MySQL local 8.0.41 e EF regenerado por DB-First;
- build, suíte integral, testes financeiros/segurança e validação visual estão aprovados;
- prontuário e Fase 19 não foram antecipados;
- nenhuma pendência real foi ocultada.

## Implementação e evidências

- as migrations `0025`, `0026` e `0027` foram executadas integralmente no banco local `viverappweb`, em MySQL 8.0.41, e o scaffold EF foi regenerado por DB-First;
- o schema agora preserva várias tentativas de pagamento por atendimento, aponta o pagamento atual, registra reversões e eventos, mantém livro-caixa e fechamento imutáveis e protege os registros append-only por triggers;
- a API entrega consulta diária/histórica paginada, filtros, totais calculados no servidor, movimentos manuais compensatórios, fechamento, duas visões de impressão e reversão idempotente;
- a interface de Gestor e Administrador possui a nova área **Caixa**, filtros, totais por método, movimentos, fechamento e as ações exatas **Imprimir** e **Imprimir Totais**; os detalhes de atendimento permitem cancelar o pagamento elegível e registrar outro depois da reversão confirmada;
- o fluxo PagBank permanece restrito a ambiente não produtivo e só confirma a reversão local quando o estado retornado pelo provedor corresponde efetivamente a reembolso; produção continua bloqueada sem autorização operacional explícita;
- o cenário de integração em MySQL comprova recebimento, reversão, novo pagamento, cadeia de duas tentativas com uma única ativa, três movimentos preservados, totais, ambos os modos de impressão, fechamento, restrição pós-fechamento do Gestor, operação elevada do Administrador e proteção append-only;
- o fallback de desenvolvimento por HTTP foi validado sem excluir ou reparar certificados: somente loopback em `Development` com `Security:AllowInsecureLocalHttp=true` pode dispensar redirecionamento HTTPS; produção continua exigindo HTTPS;
- build da solution concluído sem avisos ou erros; 187 testes aprovados em 7 projetos e verificação final confirmou MySQL 8.0.41, banco `viverappweb` e migrations aplicadas;
- a abertura pública em HTTP e o redirecionamento de sessão expirada foram conferidos no navegador integrado. A sessão autenticada não estava disponível nesse navegador e a regra do monitor 3 impede substituir essa homologação por uma superfície sem monitor identificável; a pendência foi registrada sem criar bypass de autenticação.
