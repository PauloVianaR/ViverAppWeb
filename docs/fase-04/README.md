# Fase 4 — Identidade e autorização

Esta fase substitui o bloqueio temporário da Fase 3 por uma identidade real baseada em ASP.NET Core Identity, adaptada ao schema DB-First de `viverappweb`. O navegador usa somente cookie de sessão seguro; não recebe token de longa duração nem armazena credenciais no storage do browser.

## Entregas

- cadastro público exclusivamente como paciente, com confirmação por e-mail ou SMS;
- login local por e-mail ou telefone com senha em hash adaptativo, salgado e versionado pelo hasher do ASP.NET Core Identity;
- login e recuperação por código aleatório de seis dígitos, expirável, limitado a cinco tentativas e consumido atomicamente uma única vez;
- entrega durável desses códigos pela outbox, com payload protegido por Data Protection, SMTP e endpoint 2FA oficial do SMSBarato;
- Google OAuth no servidor com state/correlation, PKCE, exigência de e-mail verificado e vínculo explícito quando já existe conta local;
- passkeys nativas do ASP.NET Core 10 como alternativa de login primário;
- MFA TOTP obrigatório para administrador, com recovery codes armazenados em HMAC e consumidos atomicamente;
- exatamente um papel persistido por conta: paciente, médico, gestor ou administrador;
- sessão em cookie host-only, `Secure`, `HttpOnly`, `SameSite=Lax`, com validade no banco, revalidação por requisição, listagem e revogação;
- policies deny-by-default, sessão limitada sem roles enquanto o MFA estiver pendente e auditoria dos eventos críticos;
- migration SQL `0005` aplicada no MySQL 8.0.41 e novo scaffold feito somente depois da aplicação.

O worker desta fase consome apenas templates `identity.*`. Templates gerais, notificações administrativas e operação completa da comunicação continuam reservados à Fase 20.

## Decisão sobre senhas legadas

Nenhuma senha AES/ECB é copiada ou descriptografada pelo runtime Web. Quando a migração de contas for autorizada em uma fase de dados, a conta elegível entrará em `viverappweb` sem senha e deverá definir uma nova senha por código enviado a um contato previamente reconciliado e confirmado. Isso evita levar a chave reversível e evita materializar senhas legadas em texto claro.

Contas novas recebem somente `password_hash`. Contas criadas pelo Google podem permanecer sem senha até o usuário adicionar um método local futuro. O banco novo não possui coluna para senha em texto claro ou ciphertext reversível.

## Limites deliberados

- não houve cópia de conta ou dado pessoal de `viverappmobile`;
- não existe cadastro público de médico, gestor ou administrador;
- criação e alteração administrativa de profissionais entram nas fases de cadastros/administração;
- as páginas Blazor de login e o acabamento responsivo entram na fase visual; esta fase entrega os contratos e fluxos seguros da API;
- por decisão do proprietário, o Client ID/secret Web, o redirect URI e a validação do handshake real do Google foram concluídos na Fase 5; nenhuma credencial foi versionada.

Consulte [matriz de autorização](MATRIZ-AUTORIZACAO.md), [configuração e operação](CONFIGURACAO-E-OPERACAO.md) e [verificações](VERIFICACOES.md).
