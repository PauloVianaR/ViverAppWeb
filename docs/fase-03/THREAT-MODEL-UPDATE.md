# Atualização do threat model após a Fase 3

Esta atualização complementa o [threat model da Fase 1](../fase-01/THREAT-MODEL-E-LGPD.md).

| Risco | Controle entregue | Risco residual / próxima fase |
|---|---|---|
| Endpoint publicado anonimamente | fallback policy autenticada e esquema temporário deny-all | Identity e matriz por papel na Fase 4 |
| CSRF | antiforgery global em comandos MVC e cookie host-only seguro | integrar tokens aos fluxos reais na Fase 4+ |
| XSS e carregamento indevido | CSP com nonce, sem `unsafe-inline`, headers e encoding do Razor | validar qualquer HTML rico futuro |
| Clickjacking/MIME sniffing | `frame-ancestors 'none'`, `DENY`, `nosniff` | reavaliar somente se uma integração legítima exigir frame |
| Abuso/DoS de aplicação | limites Kestrel, rate limit sem fila e timeouts | calibrar com métricas e carga na Fase 21 |
| Enumeração/brute force | política sensível pronta e respostas genéricas exigidas | lockout por conta/dispositivo na Fase 4 |
| Vazamento por erro/log | Problem Details redigido, health mínimo e log próprio sem body/query/PII | revisar logs de cada integração futura |
| Roubo/adulteração de chaves | key rings separados e protegidos; configuração fail-fast em produção | cofre/certificado e rotação operacional na Fase 23 |
| Adulteração da trilha | tabela append-only por triggers | backup/exportação imutável e segregação operacional |
| Supply chain | versões fixadas, warnings como erros e auditoria NuGet no gate | SBOM/proveniência nas Fases 21/23 |
| SSRF/path traversal/upload | padrão obrigatório documentado; superfície ainda inexistente | implementar e testar quando URLs/arquivos forem introduzidos |

## Novas fronteiras

- o coletor OTLP, quando configurado, torna-se uma fronteira externa e deve receber somente telemetria minimizada;
- o diretório/certificado de Data Protection passa a ser material operacional sensível;
- proxies/CDN só poderão fornecer endereço/esquema encaminhado após configuração explícita de redes confiáveis; nenhum header encaminhado é aceito automaticamente nesta fase.

## Privacidade

O identificador de correlação não codifica identidade. Métricas e traces não devem receber e-mail, telefone, parâmetros de busca clínica, conteúdo de documentos ou identificadores externos. Eventos de auditoria devem registrar identificadores internos mínimos e metadados aprovados, jamais segredos ou o conteúdo médico completo.
