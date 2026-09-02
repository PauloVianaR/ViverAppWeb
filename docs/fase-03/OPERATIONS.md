# Operação, retenção e resposta a incidentes

## Saúde e telemetria

- `/health/live` confirma que o processo responde;
- `/health/ready` também confirma a conexão da API com `viverappweb`;
- monitores devem alertar após falhas consecutivas, sem publicar a resposta detalhada do banco;
- OTLP é habilitado por `OpenTelemetry:OtlpEndpoint`; a amostragem usa `OpenTelemetry:TraceSamplingRatio` entre 0 e 1;
- o coletor deve aplicar autenticação por header/secret configurado fora do repositório e retenção mínima compatível com investigação.

Metas iniciais para calibração: alerta de disponibilidade após três falhas consecutivas, taxa sustentada de 5xx acima de 2% por cinco minutos, aumento de 429 acima do comportamento normal e readiness indisponível por dois minutos. Esses números são hipóteses operacionais, não SLOs finais.

## Backup e retenção

- backup do MySQL deve ser criptografado, possuir checksum e ficar fora do host primário;
- a credencial de backup deve ser distinta da credencial da aplicação;
- restauração deve ocorrer somente em banco isolado e nunca sobre `viverappmobile` ou `viverappweb` ativo;
- a frequência e os prazos legais de dados clínicos dependem de validação do controlador/DPO e não podem ser inventados por implementação;
- logs de acesso e traces devem ter retenção curta e acesso restrito; auditoria crítica deve ter retenção definida por categoria e base legal;
- exclusão LGPD precisa preservar somente o que houver obrigação legal de reter, com justificativa registrada.

O ensaio destrutivo de rollback da baseline continua dispensado pelo proprietário. Isso não dispensa testes futuros de restauração em cópia descartável antes da produção.

## Resposta a incidente

1. classificar o evento e preservar horário UTC, correlation IDs e artefatos sem copiar dados desnecessários;
2. conter o vetor: revogar sessão/chave comprometida, restringir origem ou desabilitar a capacidade afetada;
3. não apagar nem editar `audit_events`; exportar evidência para destino de acesso restrito;
4. rotacionar credenciais somente com autorização e registrar dependências atingidas;
5. verificar impacto em confidencialidade, integridade, disponibilidade e obrigações LGPD;
6. restaurar por artefato conhecido, validar migrations/checksums e reconciliar dados;
7. documentar causa, impacto, correção, testes de regressão e ações preventivas.

Nenhum log ou relatório de incidente deve conter senha, token, chave de API, connection string, conteúdo médico ou documento integral.
