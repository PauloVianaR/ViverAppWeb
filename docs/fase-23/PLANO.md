# Fase 23 — videochamada segura

## Resultado esperado

Em um atendimento online confirmado e elegível, o Médico ou Psicólogo atribuído abre a sala e copia um convite temporário. Até três outras pessoas entram pelo navegador com esse link — inclusive pessoas sem conta. O paciente autenticado também pode entrar e conta no mesmo limite de quatro participantes, incluindo o hospedeiro. Cada pessoa controla apenas seu próprio microfone/câmera e escolhe os próprios dispositivos. Não há gravação.

## Etapas, segurança e critérios

1. **Convite e entrada.** Criar token aleatório de alta entropia, persistir somente seu hash e expiração, oferecer revogação e rotação pelo hospedeiro. O token viaja no fragmento `#` da URL para não aparecer em logs HTTP/referrer. A página de convidado troca o token por sessão restrita de reunião em cookie HttpOnly; nunca autentica como paciente. Não exibir CPF, prontuário nem detalhes da agenda. A sala somente aceita convidados enquanto o atendimento está elegível e o hospedeiro está presente. A capacidade é quatro conexões ativas distintas, com ocupação transacional e expiração de leases.
2. **Sinalização multiusuário.** Evoluir `teleconsultation_peers` por migration DB-First, mantendo presença/expiração no MySQL. SignalR valida sessão/convite, agendamento, origem e antiforgery, destinatário na mesma sala, tipos e tamanhos de mensagens e rate limiting. Cada par usa uma conexão WebRTC própria (malha com máximo de quatro pessoas); `offer`, `answer` e candidatos ICE são endereçados, sem difusão a outras salas. Reconexão renova o lease e descarta presença antiga.
3. **Mídia e interface.** Permissão explícita do navegador; seletores de microfone/câmera alimentados por `enumerateDevices`; troca em andamento por `replaceTrack`, sem controlar mídia de terceiros. Mute/câmera off independentes por participante. Interface responsiva, estados de conectando/reconectando/lotado/expirado/permissão negada, botões acessíveis e opção de copiar link somente no hospedeiro.
4. **Rede e operação.** ICE com STUN configurável e TURN com credenciais efêmeras emitidas pelo servidor; segredo TURN somente em user-secrets/secret manager. HTTPS/WSS e CORS/origem permitida em produção. Para mais de uma instância da API, usar backplane SignalR configurado e obrigatório no deployment distribuído; presença permanece no MySQL. Sem TURN configurado não declarar homologação externa concluída. Nenhum fluxo grava áudio/vídeo.
5. **Verificação.** Migration aplicada em `viverappweb` MySQL 8.0.41 antes de regenerar EF. Testes: convidado sem conta, token inválido/expirado/revogado, anfitrião ausente, quinto participante, IDOR de sala/destinatário, troca de mídia, reconexão, multi-instância/backplane e regressão paciente/profissional. Validar no navegador integrado desktop/tablet/celular. Teste real em duas ou mais pontas e TURN externo fica como pendência de homologação até infraestrutura autorizada.

## Fora de escopo

Gravação, transcrição, chat, compartilhamento de tela, contas de convidado persistentes e envio automático do link por e-mail/SMS. O hospedeiro compartilha manualmente o link pelo canal que escolher. Não ativar serviços externos ou credenciais de produção apenas para testes.

## Estado

Implementação local e verificação automatizada concluídas na branch da Fase 23; [operação e evidências](OPERACAO.md). Homologação com mídia real, TURN e múltiplas instâncias depende da infraestrutura e autorização correspondentes; não foi simulada como conclusão de produção.
