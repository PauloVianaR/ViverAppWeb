# Entrega de produção vS1 — transferência para Windows Server

O arquivo externo `viverappweb-producao-vs1-<commit>.zip` reúne a Release da `main` corrigida, três ZIPs internos (API, Web e migrations), manifesto com SHA-256, guia operacional, relatório de correção e um Git bundle da `main`. É um pacote para **instalação assistida**, não um instalador automático nem autorização de publicação pública. Não contém o banco alfa, senhas, certificados ou user-secrets; esses recursos não são sincronizados pela conta Codex.

## Conferência na outra máquina

1. Transfira o ZIP por canal autenticado, compare seu SHA-256 com o valor comunicado separadamente, extraia em pasta restrita e confira que a `main` do bundle corresponde ao commit do manifesto.
2. Execute `verify-windows-release.ps1 -ReleaseDirectory .` na pasta extraída. O script confere tamanho e SHA-256 dos três ZIPs internos. Não instale artefatos cuja verificação falhe.
3. Para continuar o projeto no Codex, clone `viverappweb-main.bundle` em uma pasta de trabalho **separada** dos sites IIS. Restaure `AGENTS-LOCAL.md` como `AGENTS.md` e `PENDENCIAS-LOCAL.md` como `.local/PENDENCIAS.md`, ambos ignorados no repositório. Preserve versões locais mais recentes após comparação.
4. Leia `WINDOWS-SERVER-IIS.md` e execute o pré-voo somente leitura. A nova máquina precisa de Windows Server, IIS/WebSocket, Hosting Bundle .NET 10, MySQL **8.0.41** local, proteção de segredos, certificados, backup/restauração testada, Defender e rede configurados. Nenhum desses itens foi configurado neste pacote.

## Banco e ativação

O ZIP de banco contém migrations **0001–0049** e rollbacks para revisão, **não** um dump nem um migrador de produção. A migration 0049 preserva a auditoria append-only e amplia a restrição para o papel Psicólogo. No servidor novo, verificar `VERSION()`, `DATABASE()`, permissões, histórico e checksums; fazer backup; aplicar somente migrations pendentes em ordem e uma única vez, com credencial separada e revisão humana. O runner de desenvolvimento não deve ser apontado para produção.

Instale API e Web em diretórios versionados diferentes, sem sobrepor a versão ativa; mantenha dados, key rings e segredos fora dessas pastas. Deixe DNS público, indexação, entregas externas e PagBank produção desligados até homologação e autorização específicas. Valide health checks, login/MFA, arquivos, pagamentos em Sandbox, teleconsulta, proteção TLS/Cloudflare e inspeção visual no navegador integrado do Codex.

## Atualizações futuras

Para cada atualização: branch própria → revisão/testes → merge na `main` → novo pacote imutável com commit/hash → backup e migrations compatíveis → instalação lado a lado → canário e monitoramento → troca coordenada dos sites. Em falha, retire tráfego e volte os binários anteriores **somente se** o novo schema continuar compatível; nunca restaure banco ou execute rollback destrutivo por reflexo. Registre operador, horário e evidências de cada etapa.

F27-01/02/03 foram corrigidos e testados localmente, mas a cobertura ofensiva ampla, revisão independente e infraestrutura real ainda estão pendentes. Consulte `FASE-27-CORRECOES-PRODUCAO-VS1.md` e `PENDENCIAS-LOCAL.md`; o pacote não representa certificação de segurança.
