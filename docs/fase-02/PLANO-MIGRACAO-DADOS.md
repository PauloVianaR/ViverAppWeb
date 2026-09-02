# Plano de reconciliação e migração do legado

Este é um plano para fases futuras. A Fase 2 não copia registros de `viverappmobile` para `viverappweb`.

## Princípios

- `viverappmobile` permanece estritamente somente leitura;
- a extração deve ocorrer por ferramenta dedicada, com conexão de leitura e janela consistente;
- dados entram apenas por contratos do novo domínio, nunca por cópia cega de tabelas;
- cada transformação gera contagens, rejeições justificadas e checksum, sem registrar PII ou segredos;
- um ensaio usa dados sintéticos ou cópia anonimizada autorizada antes do corte real;
- arquivos e pagamentos são reconciliados por identificadores externos e hashes, sem repetir cobrança;
- cada lote deve ser idempotente e retomável.

## Mapeamento inicial

| Legado | Destino/condução | Tratamento |
|---|---|---|
| `user` | `accounts`, endereço e perfil correspondente | normalizar e-mail/telefone; mapear para exatamente um papel; rejeitar ambiguidades |
| `clinic` | `clinic` | consolidar para um único cadastro aprovado pelo proprietário |
| `doctor_props` | `doctor_profiles` | migrar apenas para contas com papel médico |
| `specialtys_doctor` | `specialties`, `doctor_specialties` | normalizar nomes e remover duplicidades com relatório |
| `appointment_type` | `appointment_types` | converter modalidade, duração e preço explicitamente |
| `availability_clinic` | `clinic_weekly_hours` | validar dia e intervalos |
| `availability_doctor`, `schedule` | `doctor_weekly_hours`, `appointments` | separar disponibilidade de consulta e converter horários para UTC |
| `holiday` | `holidays` | validar bloqueio total/parcial |
| `schedule_attachments` | `appointment_documents` | migrar metadados agora; objetos serão tratados com R2 na fase própria |
| `payment`, `payment_type` | `payments` | reconciliar provedor, valor e estado; nunca criar checkout/cobrança |
| `premium_plan`, `premium_user` | `premium_plans`, `premium_memberships` | normalizar vigência e estado |
| `email_confirmation`, `user_token` | não migrar diretamente | invalidar tokens antigos e emitir novos desafios quando necessário |
| `email_queue`, `notification` | não copiar filas históricas | reconciliar somente entregas de negócio ainda necessárias e autorizadas |
| `config` | `application_settings` ou configuração segura | migrar apenas valores não secretos; segredos ficam fora do banco |

## Senhas e contatos

Nenhuma senha reversível será gravada no novo schema. Na Fase 4, uma ferramenta transitória poderá validar a credencial legada uma única vez e substituí-la por hash adaptativo, ou exigir redefinição quando a conversão segura não for possível. A chave legada não será incorporada ao runtime permanente.

E-mail e telefone serão normalizados e verificados. Login e recuperação poderão usar e-mail ou SMSBarato; Google continuará como alternativa externa. Tokens e códigos legados não serão reaproveitados.

## Etapas futuras

1. aprovar regras de transformação e resolução de conflitos;
2. registrar contagens de origem sem expor registros;
3. executar dry-run sem escrita e produzir relatório de rejeições;
4. importar em lotes idempotentes no `viverappweb` de ensaio;
5. validar contagens, FKs, estados, horários, valores e checksums;
6. ensaiar rollback e tempo total;
7. executar corte autorizado, reconciliar divergências e manter trilha auditável.

Critério de aceite: cada registro elegível deve estar reconciliado ou aparecer em relatório de exceção aprovado; nenhuma escrita pode ocorrer no legado e nenhuma senha, token ou dado sensível pode aparecer nos relatórios.
