# Matriz de autorização da identidade

| Superfície | Acesso | Proteções adicionais |
|---|---|---|
| token antiforgery | anônimo | cookie host-only e HTTPS |
| cadastro, reenvio e confirmação de contato | anônimo | validação estrita, rate limit, resposta sem enumeração e código de uso único |
| login por senha/código e recuperação | anônimo | rate limit sensível, lockout, antiforgery e resposta genérica |
| início/conclusão do Google | anônimo | correlation/state, PKCE, e-mail verificado e retorno fixo no servidor |
| opções/login por passkey | anônimo | WebAuthn com verificação do usuário e rate limit sensível |
| `me`, logout e conclusão/cadastro de MFA | sessão válida, inclusive sessão limitada | nenhuma role presente até concluir o MFA |
| redefinir recovery codes | sessão com MFA satisfeito | rate limit sensível e auditoria |
| vincular Google | sessão com MFA satisfeito | vínculo explícito, provider subject único e auditoria |
| criar/remover passkey | sessão com MFA satisfeito | máximo de dez credenciais e auditoria |
| listar/revogar sessões | sessão com MFA satisfeito | filtro obrigatório pelo ID da própria conta e auditoria |

## Policies

`role:patient`, `role:doctor`, `role:manager` e `role:administrator` representam os quatro papéis possíveis. `role:clinical-staff` agrega médico, gestor e administrador. `identity:mfa-satisfied` exige o claim emitido somente a partir de uma sessão ativa no banco; `identity:mfa-enrollment` permite apenas o pequeno conjunto necessário para concluir MFA ou sair.

A policy padrão e a fallback policy exigem usuário autenticado e MFA satisfeito. Durante um desafio pendente ou o primeiro acesso de um administrador sem TOTP, o cookie recebe uma sessão limitada e todas as claims de role são removidas. Portanto, conhecer ou enviar um papel pelo cliente nunca concede autorização.

## Ownership e clínica única

Uma conta tem uma única coluna `role_code` obrigatória e validada pelo banco. Não existe associação conta-clínica: médico e gestor pertencem implicitamente à única clínica. Operações de sessão sempre filtram simultaneamente pelo ID da sessão e pelo ID da conta autenticada, impedindo revogação horizontal.
