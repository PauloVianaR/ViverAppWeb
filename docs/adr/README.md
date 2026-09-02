# Registros de decisões arquiteturais

Os ADRs registram decisões da reimplementação web que afetam mais de um módulo ou fase. Uma decisão aceita só pode ser substituída por outro ADR; o histórico não deve ser apagado.

| ADR | Decisão | Estado |
|---|---|---|
| [0001](0001-monolito-modular.md) | Monólito modular com Web e API separadas | Aceita |
| [0002](0002-blazor-server-bff.md) | Blazor Server e padrão BFF para o navegador | Aceita |
| [0003](0003-db-first-mysql.md) | MySQL 8.0.41, SQL versionado e EF DB-First | Aceita com pendência registrada |
| [0004](0004-outbox-e-workers-na-api.md) | Outbox transacional e workers hospedados na API | Aceita |
| [0005](0005-signalr-e-webrtc-na-api.md) | Sinalização de vídeo na API, mídia por WebRTC | Aceita |
| [0006](0006-cloudflare-r2-e-cdn.md) | R2 privado e CDN apenas para conteúdo publicável | Aceita |
| [0007](0007-implantacao-portavel.md) | Implantação portável, sem dependência de Azure | Aceita |
| [0008](0008-migracao-incremental.md) | Reescrita incremental com reconciliação | Aceita |

As decisões que dependem de descoberta futura estão listadas em [Backlog e decisões](../fase-01/BACKLOG-E-DECISOES.md).
