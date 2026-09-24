# Fase 23 — operação e homologação da videochamada

## Configuração

- `Teleconsultation:WebOrigin`: origem pública da aplicação Web, HTTPS em staging/produção. O convite usa a origem da requisição somente quando ela constar da allowlist CORS da API; caso contrário, usa esta configuração.
- `Teleconsultation:StunUrls`: lista de servidores STUN. O desenvolvimento local já possui uma configuração de exemplo; não a confundir com homologação de redes externas.
- `Teleconsultation:TurnUrls` e `Teleconsultation:TurnSharedSecret`: URLs TURN e segredo REST compartilhado com o coturn, guardado somente em user-secrets/cofre. A API emite credenciais efêmeras por participante; fora de Development, a emissão de ICE recusa operar sem TURN.
- `SignalR:DistributedDeployment=true` e `SignalR:RedisConnectionString`: obrigatórios quando a API tem mais de uma instância. O backplane SignalR distribui mensagens; o MySQL mantém a capacidade e os leases. O Redis deve estar na mesma região/rede privada das instâncias e a conexão deve ser protegida.
- `Security:AllowedCorsOrigins` na API deve incluir somente a origem Web; `Security:AllowedConnectSources` na Web deve incluir a origem da API. O cookie de convidado usa `SameSite=Strict`, `HttpOnly` e `Secure` fora da exceção local HTTP de Development: Web e API precisam permanecer no mesmo *site* (por exemplo, subdomínios do domínio da clínica).
- Compartilhar o anel de ASP.NET Data Protection entre as instâncias da API, com proteção por certificado, para que o cookie temporário do convidado continue válido após balanceamento. Não gerar outro anel durante a reunião.

Não versionar os valores de `TurnSharedSecret` e `RedisConnectionString`, nem registrar URLs de convite em logs. Não publicar esta funcionalidade com `Security:AllowInsecureLocalHttp` fora de Development.

## Fluxo e controles

1. O profissional atribuído entra na sala de um atendimento online confirmado e elegível, então gera/copia o convite. Gerar novamente invalida o anterior; revogar encerra os convidados associados.
2. O convidado abre o link em outro navegador. O segredo fica no fragmento da URL e é removido da barra após a troca por cookie temporário. O cookie só concede acesso àquela reunião, não ao prontuário, agenda ou conta do paciente.
3. O MySQL serializa a entrada na linha do atendimento e recusa a quinta conexão ativa. O profissional deve estar presente para um convidado entrar. Cada comando de sinalização revalida sala e destinatário; a mídia viaja diretamente entre os navegadores ou pelo TURN, nunca pela API.
4. A câmera e o microfone de cada participante dependem da permissão explícita do respectivo navegador. Os seletores trocam a fonte local em andamento; mute e câmera desligada afetam apenas os próprios tracks. Não há gravação.

## Evidências locais e limites

- Migration `0046` aplicada e verificada em `viverappweb`, MySQL 8.0.41; EF regenerado via DB-First.
- Testes de convite sem hospedeiro, token adulterado/expirado/revogado, cookie restrito à sala, rotação, quatro conexões, quinta rejeitada e reconexão passaram no MySQL local. A suíte integral passou com os projetos de teste em sequência (`dotnet test ViverApp.slnx --no-restore -m:1`). A execução paralela expõe disputa já existente entre fixtures da fila de notificações no banco compartilhado.
- A página pública sem convite foi inspecionada no navegador integrado em desktop, tablet e celular: erro legível em português, sem mídia/controles ativos nem transbordamento visível. Isso **não** homologa o fluxo autenticado nem a mídia.
- Permanecem pendentes ensaio em duas ou mais pontas reais, teste do navegador com permissão de câmera/microfone, TURN externo e duas instâncias com Redis. Essas pendências constam de `.local/PENDENCIAS.md` e bloqueiam a afirmação de homologação externa/produção.

Referências técnicas: [backplane Redis do SignalR](https://learn.microsoft.com/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0), [coturn TURN REST API](https://github.com/coturn/coturn/wiki/turnserver) e [WebRTC `replaceTrack`](https://www.w3.org/TR/webrtc/).
