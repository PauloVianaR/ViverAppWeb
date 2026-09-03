# Fase 5 — Cadastros e configuração clínica

## Resultado

A clínica única agora possui APIs versionadas para seus dados institucionais, horários, feriados, usuários, profissionais, especialidades e tipos de atendimento. Os contratos HTTP são próprios e nunca expõem diretamente as classes geradas pelo scaffold do Entity Framework.

O Blazor recebeu a primeira superfície responsiva de manutenção em `/gestao/cadastros`. Esta tela estabelece navegação, hierarquia e estados vazios reais, sem inventar registros. A conexão completa dos formulários à sessão autenticada será amadurecida com o design system e o shell por perfil da Fase 6.

## Persistência DB-First

A migration `0006__clinic_master_data.sql` foi aplicada exclusivamente em `viverappweb` no MySQL 8.0.41 antes do scaffold. Ela acrescenta:

- endereço opcional, porém indivisível, à clínica singleton;
- timestamps e `row_version` aos cadastros editáveis;
- controle de concorrência otimista para clínica, catálogos, horários, feriados e perfil médico;
- histórico `professional_reviews` para aprovação, rejeição, bloqueio e reativação;
- vínculos auditáveis entre profissional e revisor.

O scaffold reproduzível agora gera 28 entidades a partir do banco novo. `viverappmobile` não foi consultado nem alterado nesta fase.

## Regras relevantes

- o produto continua estritamente single-clinic;
- cada conta mantém exatamente um papel;
- pacientes entram pelo cadastro público da identidade;
- médicos e gestores são criados por administrador e confirmam e-mail ou telefone antes da aprovação;
- somente administrador aprova, rejeita, bloqueia ou reativa profissionais;
- gestores administram a clínica e médicos, mas não editam administradores nem outros gestores;
- médicos alteram a própria disponibilidade e não acessam a de outro médico;
- exclusão de especialidades e tipos de atendimento é desativação lógica, preservando referências históricas;
- conflitos de horário, versões desatualizadas e transições de status inválidas retornam conflito sem vazar detalhes internos;
- mutações e decisões administrativas geram eventos na auditoria imutável.

## Google

O login e o registro de pacientes com Google estão conectados às credenciais `GoogleOAuth` mantidas em user-secrets. A API valida as quatro chaves em conjunto, aceita somente callback HTTPS com o caminho exato `/signin-google`, usa Authorization Code com PKCE, state/correlation e exige e-mail verificado pelo provedor.

Uma identidade Google nova cria uma conta de paciente sem senha reversível. A criação da conta e do vínculo externo é transacional, portanto uma falha não deixa uma conta incompleta. Se o e-mail já pertencer a uma conta local, o vínculo explícito autenticado é obrigatório, evitando tomada de conta por mera coincidência de endereço. O Blazor inicia o fluxo na página inicial, apresenta apenas resultados previamente permitidos em `/auth/result` e recebe cancelamentos ou falhas do provedor sem expor detalhes internos.

Nenhuma credencial ou token Google foi versionado.

## Verificação

Consulte [VERIFICACOES.md](VERIFICACOES.md) para os comandos e resultados e [API-E-AUTORIZACAO.md](API-E-AUTORIZACAO.md) para rotas e papéis.
