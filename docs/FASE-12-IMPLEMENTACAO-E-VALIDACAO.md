# Fase 12 — Implementação e validação

## Resultado

A experiência do Médico foi implementada na branch `codex/fase-12-experiencia-medico`, sem antecipar funcionalidades de Gestor ou Administrador. A API deriva o médico exclusivamente da sessão e limita agenda, pacientes, laudos, anexos e sala online ao profissional autenticado.

## Entregas funcionais

- início profissional com identidade, CRM, especialidade, experiência, avaliação, indicadores, agenda do dia e atalhos;
- agenda e histórico com busca, período, tipo, modalidade, estado, contadores, paginação, cards e modo de lista compacta no desktop;
- detalhe do atendimento com cancelamento, reagendamento, falta, conclusão, laudo, anexos e feedback somente leitura;
- pacientes derivados de vínculo real ou atendimento próprio, inclusão por contato exato confirmado, convite com confirmação e edição restrita ao nome preferido;
- agendamento em nome do paciente com médico fixo, serviços oferecidos, modalidade, disponibilidade, limites, conflitos, preço e desconto calculados no servidor;
- perfil profissional, especialidades, serviços, preferências de comunicação, disponibilidade semanal, modalidades e exceções por data;
- troca confirmada de contato, senha, Google, passkeys e sessões dentro da área médica;
- videoatendimento autorizado por atendimento, médico, estado, pagamento e janela temporal, sem gravação.

## Banco DB-First

As migrations `0015__doctor_experience.sql` e `0016__doctor_weekly_hour_modality.sql` foram executadas integralmente em `viverappweb`, no MySQL local 8.0.41. O scaffold DB-First foi regenerado depois da aplicação e o verificador confirmou banco, versão e histórico de migrations.

O schema passou a representar ofertas do médico, preferências e limites, exceções de disponibilidade, vínculos explícitos com pacientes, versões imutáveis de laudo, exclusão lógica de anexos e modalidade por faixa semanal.

## Segurança e integridade

- autorização por papel e ownership em todas as rotas médicas;
- teste negativo entre Médico A e recursos do Médico B;
- vínculo de paciente somente por e-mail ou telefone completo e confirmado, sem busca irrestrita;
- idempotência e concorrência otimista em agendamento, transições, disponibilidade, laudos e anexos;
- primeira versão do laudo criada com a conclusão e retificações posteriores preservadas de forma imutável;
- upload clínico limitado a 10 MB, com extensão, MIME real, assinatura e verificação de malware;
- conteúdo clínico não é enviado para logs nem eventos de auditoria.

## Evidências automatizadas

- `dotnet build ViverApp.slnx --no-restore`: aprovado com zero erros e zero avisos;
- `dotnet test ViverApp.slnx --no-build --no-restore`: 154 testes aprovados;
- `dotnet run --project tools/ViverApp.Database --no-build --no-restore -- verify`: MySQL 8.0.41, `viverappweb` e migrations aprovados;
- formatação e verificação de whitespace aprovadas nos arquivos alterados.

## Validação visual

A janela do Chrome foi posicionada e confirmada exclusivamente no monitor 3, com `screenX=1920`. Foram validados Início e Agenda em desktop, Pacientes em tablet, Perfil em celular, detalhe clínico e videoatendimento em desktop, além da alternância Cards/Lista compacta.

Todos os cenários concluíram sem overflow horizontal, erro de console ou resposta 401 após a autenticação. A escala equivalente a 200% do Chromium também não apresentou overflow. O zoom nativo de 200% continua como homologação separada porque o host de automação não informa nem altera esse nível de maneira confiável.

As capturas e a ferramenta de dados sintéticos permanecem em `.local`, fora do Git. Ao final, somente contas e registros marcados pela própria fixture foram removidos; a auditoria append-only foi preservada e anonimizada pelas chaves estrangeiras previstas no schema.

## Homologações remanescentes

Dependem de ambiente, dispositivos, destinatários ou decisões reais e permanecem registradas no arquivo local ignorado `.local/PENDENCIAS.md`: entrega real de e-mail/SMS, Google/passkey, arquivos reais e antivírus, videochamada com duas pontas, parâmetros finais da clínica e zoom nativo de 200%.
