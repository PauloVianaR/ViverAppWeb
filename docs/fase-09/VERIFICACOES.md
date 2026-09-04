# Verificações da Fase 9

- [x] documentação oficial atual do PagBank revisada;
- [x] migrations `0009` e `0010` aplicadas em `viverappweb` no MySQL 8.0.41;
- [x] scaffold DB-First regenerado depois de todas as migrations;
- [x] preço e moeda obtidos exclusivamente do agendamento persistido;
- [x] autenticação SHA-256 usa o corpo bruto e comparação em tempo constante;
- [x] replay, assinatura falsa, valor divergente e eventos fora de ordem tratados;
- [x] estados `paid` e `refunded` protegidos contra regressão;
- [x] retorno consulta a API e não confia nos parâmetros do PagBank;
- [x] reconciliação e inativação de checkout executadas dentro da API;
- [x] reembolso integral protegido e desabilitado por padrão;
- [x] nenhuma chamada externa ou cobrança real realizada nos testes;
- [x] tela de pagamentos inspecionada visualmente em desktop e viewport móvel;
- [x] solução compilada em `Release` sem avisos ou erros;
- [x] 112 testes automatizados aprovados em execução sequencial, evitando concorrência entre projetos sobre o MySQL local compartilhado;
- [x] formatação, integridade do diff e auditoria de pacotes vulneráveis aprovadas;
- [ ] ativação Sandbox com URLs públicas — depende de ambiente publicado;
- [ ] homologação e transação controlada em produção — dependem de autorização explícita futura.
