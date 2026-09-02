# ADR 0005 — SignalR e WebRTC na API

- **Estado:** aceita
- **Data:** 2026-09-02

## Contexto

O hub legado aceita entrada e sinalização sem autenticação, mantém presença em memória estática e não vincula uma sala a um agendamento autorizado.

## Decisão

Incorporar o hub SignalR à API. Cada conexão será autenticada e cada entrada em sala exigirá autorização por participante e agendamento. IDs serão imprevisíveis, mensagens terão schemas e limites, e a presença distribuída terá expiração.

A mídia continuará via WebRTC quando adequado. STUN/TURN usarão credenciais efêmeras. HTTPS/WSS, allowlist de origens, rate limiting e auditoria de eventos de segurança serão obrigatórios. Não haverá gravação por padrão.

## Consequências

- A sinalização compartilha identidade, autorização e observabilidade com a API.
- Escala horizontal exigirá backplane/serviço distribuído e estado de presença externo.
- Gravação futura dependerá de decisão própria de segurança, consentimento e retenção.
