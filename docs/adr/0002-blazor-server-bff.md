# ADR 0002 — Blazor Server com BFF

- **Estado:** aceita
- **Data:** 2026-09-02

## Contexto

O navegador não deve receber tokens de longa duração, chaves de provedores nem autorização baseada apenas na interface. A aplicação também precisa adaptar jornadas originalmente móveis a desktop, tablet e celular.

## Decisão

Usar Blazor Web App com interatividade Server para a interface e tratar o servidor web como Backend for Frontend. A sessão do navegador usará cookie seguro, `HttpOnly` e `SameSite`, com antiforgery. A API validará identidade, política e ownership em toda operação protegida.

Chamadas que não precisem sair do servidor não exporão credenciais ao browser. Google OpenID Connect será integrado no servidor na Fase 4. O contrato entre Web e API permanecerá explícito para permitir clientes futuros.

## Consequências

- O estado sensível permanece no servidor e a superfície de tokens no browser diminui.
- Circuitos Blazor exigem limites, telemetria, reconexão e planejamento de escala.
- CSP, antiforgery, rate limiting e autorização continuam obrigatórios; BFF não os substitui.
