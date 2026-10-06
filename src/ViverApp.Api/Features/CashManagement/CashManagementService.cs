using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Features.Payments;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.CashManagement;

public sealed class CashManagementService(
    ViverAppDbContext database,
    IPagBankClient pagBank,
    PagBankOptions pagBankOptions,
    IClinicalOperationsAuditWriter audit,
    TimeProvider clock)
{
    private static readonly string[] Methods = ["cash", "pix", "debit_card", "credit_card", "pagbank_online", "other"];
    private static readonly string[] Types = ["payment_received", "payment_reversal", "supply", "withdrawal", "adjustment", "provider_fee"];

    public async Task<CashFilterOptionsResponse> FilterOptionsAsync(CancellationToken cancellationToken)
    {
        var professionals = await database.ProfessionalProfiles.AsNoTracking()
            .Where(item => item.Account.RoleCode == ViverAppRoles.Doctor || item.Account.RoleCode == ViverAppRoles.Psychologist)
            .OrderBy(item => item.Account.FullName).ThenBy(item => item.AccountId)
            .Select(item => new CashPersonOptionResponse(item.AccountId, item.Account.FullName, item.Account.RoleCode))
            .ToArrayAsync(cancellationToken);
        var responsibles = await database.Accounts.AsNoTracking()
            .Where(item => database.CashMovements.Any(movement => movement.ResponsibleAccountId == item.Id))
            .OrderBy(item => item.FullName).ThenBy(item => item.Id)
            .Select(item => new CashPersonOptionResponse(item.Id, item.FullName, item.RoleCode))
            .ToArrayAsync(cancellationToken);
        return new(professionals, responsibles);
    }

    public async Task<CashDayResponse> DayAsync(
        DateOnly date,
        string actorRole,
        string? method,
        string? type,
        ulong? appointmentNumber,
        string? patient,
        string? responsible,
        string? cardLastFour,
        string? authorizationReference,
        int page,
        int pageSize,
        CancellationToken cancellationToken,
        ulong? professionalAccountId = null,
        ulong? responsibleAccountId = null)
    {
        var today = await OperationalDateAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
        ValidateQuery(date, today, method, type, page, pageSize);
        var normalizedCardLastFour = NormalizeCardLastFour(cardLastFour);
        var normalizedAuthorization = NormalizeAuthorization(authorizationReference);
        var query = Filtered(date, method, type, appointmentNumber, patient, responsible,
            normalizedCardLastFour, normalizedAuthorization, professionalAccountId, responsibleAccountId);
        var total = await query.CountAsync(cancellationToken);
        var lastMovementId = await query.MaxAsync(item => (ulong?)item.Id, cancellationToken);
        var rows = await query.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var summary = await SummaryAsync(query, cancellationToken);
        var closure = await ClosureAsync(date, cancellationToken);
        var canViewCumulative = actorRole == ViverAppRoles.Administrator
            || actorRole == ViverAppRoles.Manager && await SettingAsync("cash.manager_can_view_cumulative_totals", true, cancellationToken);
        var cumulative = canViewCumulative
            ? await SummaryAsync(database.CashMovements.AsNoTracking().Where(item => item.OperationalDate <= today.ToDateTime(TimeOnly.MinValue)), cancellationToken)
            : null;
        var isClosed = date < today || closure is not null;
        var canReopen = date == today && closure is not null && (actorRole == ViverAppRoles.Administrator
            || actorRole == ViverAppRoles.Manager && await SettingAsync("cash.manager_can_reopen", false, cancellationToken));
        return new(date, (await ClinicAsync(cancellationToken)).TimezoneName, closure, isClosed,
            date == today && closure is null, canReopen, summary, cumulative, lastMovementId,
            new(rows.Select(Map).ToArray(), page, pageSize, total));
    }

    public async Task<CashPrintResponse> PrintAsync(
        ulong actor,
        DateOnly date,
        string? method,
        string? type,
        ulong? appointmentNumber,
        string? patient,
        string? responsible,
        string? cardLastFour,
        string? authorizationReference,
        bool totalsOnly,
        CancellationToken cancellationToken,
        ulong? professionalAccountId = null,
        ulong? responsibleAccountId = null)
    {
        var today = await OperationalDateAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
        ValidateQuery(date, today, method, type, 1, 100);
        var normalizedCardLastFour = NormalizeCardLastFour(cardLastFour);
        var normalizedAuthorization = NormalizeAuthorization(authorizationReference);
        var query = Filtered(date, method, type, appointmentNumber, patient, responsible,
            normalizedCardLastFour, normalizedAuthorization, professionalAccountId, responsibleAccountId);
        var clinic = await ClinicAsync(cancellationToken);
        var actorName = await database.Accounts.AsNoTracking().Where(item => item.Id == actor)
            .Select(item => item.FullName).SingleAsync(cancellationToken);
        var movements = totalsOnly
            ? []
            : (await query.OrderBy(item => item.OccurredAtUtc).ThenBy(item => item.Id).ToArrayAsync(cancellationToken)).Select(Map).ToArray();
        var professionalName = professionalAccountId.HasValue
            ? await database.Accounts.AsNoTracking().Where(item => item.Id == professionalAccountId.Value)
                .Select(item => item.FullName).SingleOrDefaultAsync(cancellationToken)
            : null;
        var responsibleName = responsibleAccountId.HasValue
            ? await database.Accounts.AsNoTracking().Where(item => item.Id == responsibleAccountId.Value)
                .Select(item => item.FullName).SingleOrDefaultAsync(cancellationToken)
            : null;
        var filters = string.Join(" · ", new[]
        {
            method is null ? null : $"Forma: {method}",
            type is null ? null : $"Tipo: {type}",
            appointmentNumber is null ? null : $"Atendimento: {appointmentNumber}",
            string.IsNullOrWhiteSpace(patient) ? null : $"Paciente: {patient.Trim()}",
            string.IsNullOrWhiteSpace(responsible) ? null : $"Responsável: {responsible.Trim()}",
            professionalAccountId.HasValue ? $"Profissional: {professionalName ?? $"#{professionalAccountId.Value}"}" : null,
            responsibleAccountId.HasValue ? $"Responsável: {responsibleName ?? $"#{responsibleAccountId.Value}"}" : null,
            normalizedCardLastFour is null ? null : $"Final do cartão: {normalizedCardLastFour}",
            normalizedAuthorization is null ? null : $"Autorização: {normalizedAuthorization}",
        }.Where(item => item is not null));
        await audit.WriteAsync(totalsOnly ? "cash.totals_printed" : "cash.movements_printed", actor, "cash_day",
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, cancellationToken);
        var closure = await ClosureAsync(date, cancellationToken);
        return new(clinic.LegalName, date, clinic.TimezoneName, actorName, clock.GetUtcNow().UtcDateTime,
            string.IsNullOrWhiteSpace(filters) ? "Sem filtros adicionais" : filters,
            closure, date < today || closure is not null,
            await SummaryAsync(query, cancellationToken), movements, totalsOnly);
    }

    public async Task<CashMovementResponse> AddManualAsync(
        ulong actor,
        string actorRole,
        string idempotencyKey,
        CashManualMovementRequest request,
        CancellationToken cancellationToken)
    {
        ValidateIdempotency(idempotencyKey);
        if (request.TypeCode is not ("supply" or "withdrawal" or "adjustment")) throw Invalid("Tipo de movimentação manual inválido.");
        if (!Methods.Contains(request.MethodCode, StringComparer.Ordinal)) throw Invalid("Forma de pagamento inválida.");
        var expectedDirection = request.TypeCode switch { "supply" => "entry", "withdrawal" => "outflow", _ => request.DirectionCode };
        if (expectedDirection is not ("entry" or "outflow") || request.DirectionCode != expectedDirection)
            throw Invalid("A direção da movimentação não corresponde ao tipo informado.");
        var reason = Useful(request.Reason, 5, "Informe um motivo com pelo menos 5 caracteres.");
        var description = ManualDescription(request.TypeCode, expectedDirection);
        if (request.TypeCode == "adjustment" && request.RelatedMovementId is null)
            throw Invalid("Um ajuste corretivo deve indicar o movimento compensado.");
        if (request.TypeCode != "adjustment" && request.RelatedMovementId is not null)
            throw Invalid("Somente ajustes corretivos podem indicar um movimento relacionado.");
        var now = clock.GetUtcNow().UtcDateTime;
        var date = await OperationalDateAsync(now, cancellationToken);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var replay = await database.CashMovements.AsNoTracking().Include(item => item.Payment).Include(item => item.ResponsibleAccount)
            .SingleOrDefaultAsync(item => item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return Map(replay);
        }

        if (await IsClosedAsync(date, cancellationToken))
            throw Conflict("O caixa de hoje está fechado. Reabra-o antes de registrar uma nova movimentação.");
        if (request.RelatedMovementId is not null && !await database.CashMovements.AnyAsync(
                item => item.Id == request.RelatedMovementId && item.OperationalDate == date.ToDateTime(TimeOnly.MinValue), cancellationToken))
            throw Missing("O movimento a compensar não existe neste caixa.");
        var movement = new CashMovement
        {
            OperationalDate = date.ToDateTime(TimeOnly.MinValue),
            DirectionCode = expectedDirection,
            TypeCode = request.TypeCode,
            MethodCode = request.MethodCode,
            Amount = request.Amount,
            CurrencyCode = "BRL",
            ResponsibleAccountId = actor,
            Description = description,
            Reason = reason,
            RelatedMovementId = request.RelatedMovementId,
            IdempotencyKey = idempotencyKey,
            OccurredAtUtc = now,
            AfterClosure = false,
        };
        database.CashMovements.Add(movement);
        await database.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("cash.manual_movement.created", actor, "cash_movement", movement.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["type"] = movement.TypeCode, ["direction"] = movement.DirectionCode }, cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        var recorded = await database.CashMovements.AsNoTracking().Include(item => item.Payment).Include(item => item.ResponsibleAccount)
            .SingleAsync(item => item.Id == movement.Id, cancellationToken);
        return Map(recorded);
    }

    public async Task<CashClosureResponse> CloseAsync(
        ulong actor,
        DateOnly date,
        CashCloseRequest request,
        CancellationToken cancellationToken)
    {
        var today = await OperationalDateAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (date != today) throw Conflict("Somente o caixa de hoje pode ser fechado manualmente.");
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var existing = await ActiveClosureEntityAsync(date, cancellationToken);
        if (existing is not null)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return await MapClosureAsync(existing, cancellationToken);
        }

        var query = Filtered(date, null, null, null, null, null, null, null);
        var lastMovementId = await query.MaxAsync(item => (ulong?)item.Id, cancellationToken);
        if (lastMovementId != request.ExpectedLastMovementId)
            throw Conflict("O caixa recebeu novas movimentações. Revise os totais antes de fechar.");
        var summary = await SummaryAsync(query, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var closure = new CashClosure
        {
            OperationalDate = date.ToDateTime(TimeOnly.MinValue),
            ClosedByAccountId = actor,
            LastMovementId = lastMovementId,
            GrossEntries = summary.GrossEntries,
            PaymentReversals = summary.PaymentReversals,
            Supplies = summary.Supplies,
            Withdrawals = summary.Withdrawals,
            AdjustmentsNet = summary.AdjustmentsNet,
            NetTotal = summary.NetTotal,
            MovementCount = checked((uint)summary.MovementCount),
            TotalsByMethodJson = JsonSerializer.Serialize(summary.ByMethod),
            ClosedAtUtc = now,
        };
        database.CashClosures.Add(closure);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await audit.WriteAsync("cash.day.closed", actor, "cash_closure", closure.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["operationalDate"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, cancellationToken);
        return await ClosureAsync(date, cancellationToken) ?? throw Conflict("O fechamento não pôde ser consultado.");
    }

    public async Task<CashReopeningResponse> ReopenAsync(ulong actor, string actorRole, DateOnly date,
        CashReopenRequest request, CancellationToken cancellationToken)
    {
        var today = await OperationalDateAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (date != today) throw Conflict("Somente o caixa de hoje pode ser reaberto.");
        if (actorRole != ViverAppRoles.Administrator
            && (actorRole != ViverAppRoles.Manager || !await SettingAsync("cash.manager_can_reopen", false, cancellationToken)))
            throw Forbidden("Seu perfil não tem permissão para reabrir o caixa.");
        var reason = Useful(request.Reason, 5, "Informe o motivo da reabertura com pelo menos 5 caracteres.");
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var closure = await ActiveClosureEntityAsync(date, cancellationToken)
            ?? throw Conflict("O caixa de hoje já está aberto.");
        var now = clock.GetUtcNow().UtcDateTime;
        var reopening = new CashReopening
        {
            CashClosureId = closure.Id,
            ReopenedByAccountId = actor,
            Reason = reason,
            ReopenedAtUtc = now,
        };
        database.CashReopenings.Add(reopening);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await audit.WriteAsync("cash.day.reopened", actor, "cash_closure", closure.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["operationalDate"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["reason"] = reason }, cancellationToken);
        var actorName = await database.Accounts.AsNoTracking().Where(item => item.Id == actor).Select(item => item.FullName).SingleAsync(cancellationToken);
        return new(reopening.Id, closure.Id, actorName, reason, now);
    }

    public async Task<PaymentReversalResponse> ReverseAsync(
        ulong actor,
        string idempotencyKey,
        ulong paymentId,
        PaymentReversalRequest request,
        CancellationToken cancellationToken)
    {
        ValidateIdempotency(idempotencyKey);
        var reason = Useful(request.Reason, 5, "Informe um motivo com pelo menos 5 caracteres.");
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var replay = await database.PaymentReversals.AsNoTracking().Include(item => item.Payment)
            .SingleOrDefaultAsync(item => item.RequestedByAccountId == actor && item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null)
        {
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            var replayAppointmentStatus = await database.Appointments.AsNoTracking()
                .Where(item => item.Id == replay.Payment.AppointmentId)
                .Select(item => item.StatusCode).SingleAsync(cancellationToken);
            return MapReversal(replay, replayAppointmentStatus);
        }

        var payment = await database.Payments.FromSqlInterpolated($"SELECT * FROM payments WHERE id={paymentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw Missing("Pagamento não encontrado.");
        var appointment = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id={payment.AppointmentId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        if (payment.RowVersion != request.RowVersion) throw Conflict("O pagamento foi alterado por outra sessão.");
        if (appointment.CurrentPaymentId != payment.Id || payment.StatusCode != "paid")
            throw Conflict("Somente o pagamento quitado atual pode ser cancelado.");
        if (appointment.StatusCode is not ("confirmed" or "arrived" or "no_show"))
            throw Conflict("O pagamento só pode ser cancelado em atendimento confirmado, com chegada registrada ou marcado como não compareceu.");
        var operationalDate = await OperationalDateAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (await IsClosedAsync(operationalDate, cancellationToken))
            throw Conflict("O caixa de hoje está fechado. Reabra-o antes de cancelar um pagamento.");
        var originals = await database.CashMovements
            .Where(item => item.PaymentId == payment.Id && item.TypeCode == "payment_received")
            .OrderBy(item => item.Id).ToArrayAsync(cancellationToken);
        if (originals.Length == 0 || originals.Sum(item => item.Amount) != payment.Amount)
            throw Conflict("As parcelas do recebimento original não foram localizadas integralmente no caixa.");
        var now = clock.GetUtcNow().UtcDateTime;
        var reversal = new PaymentReversal
        {
            PaymentId = payment.Id,
            RequestedByAccountId = actor,
            StatusCode = payment.ProviderCode == "internal" ? "confirmed" : "pending",
            Reason = reason,
            IdempotencyKey = idempotencyKey,
            RequestedAtUtc = now,
            CompletedAtUtc = payment.ProviderCode == "internal" ? now : null,
            RowVersion = 1,
        };
        database.PaymentReversals.Add(reversal);
        payment.ReversalReason = reason; payment.ReversalRequestedAtUtc = now; payment.ReversedByAccountId = actor;
        payment.StatusCode = payment.ProviderCode == "internal" ? "reversed" : "reversal_pending";
        payment.UpdatedAtUtc = now; payment.RowVersion++;
        await database.SaveChangesAsync(cancellationToken);
        database.PaymentReversalEvents.Add(new PaymentReversalEvent
        {
            PaymentReversalId = reversal.Id,
            FromStatusCode = null,
            ToStatusCode = reversal.StatusCode,
            SourceCode = payment.ProviderCode == "internal" ? "manual" : "pagbank",
            OccurredAtUtc = now,
        });

        if (payment.ProviderCode == "pagbank")
        {
            if (!pagBankOptions.Enabled || !pagBankOptions.RefundsEnabled)
                throw new CashRuleException(503, "O estorno PagBank não está habilitado neste ambiente.");
            if (pagBankOptions.IsProduction)
                throw new CashRuleException(503, "Estornos PagBank de produção exigem autorização operacional explícita.");
            if (string.IsNullOrWhiteSpace(payment.ProviderTransactionId)) throw Conflict("A referência da cobrança PagBank não está disponível.");
            var provider = await pagBank.RefundChargeAsync(payment.ProviderTransactionId, ToCents(payment.Amount), NormalizeProviderKey(idempotencyKey), cancellationToken);
            if (PaymentStateMachine.Normalize(provider.Status, provider.TotalCents, provider.RefundedCents) != "refunded")
                throw new CashRuleException(502, "O PagBank não confirmou o estorno. Nenhuma reversão foi registrada no caixa.");
            payment.StatusCode = "refunded"; payment.RefundAmount = payment.Amount; payment.RefundedAtUtc = provider.OccurredAtUtc ?? now;
            payment.ProviderStatusCode = provider.Status; payment.ProviderEventAtUtc = provider.OccurredAtUtc ?? now; payment.RowVersion++;
            reversal.StatusCode = "confirmed"; reversal.ProviderReference = provider.Id; reversal.CompletedAtUtc = now; reversal.RowVersion++;
            database.PaymentReversalEvents.Add(new PaymentReversalEvent { PaymentReversalId = reversal.Id, FromStatusCode = "pending", ToStatusCode = "confirmed", SourceCode = "pagbank", OccurredAtUtc = now });
        }

        var previousAppointmentStatus = appointment.StatusCode;
        if (previousAppointmentStatus is "confirmed" or "arrived")
        {
            appointment.StatusCode = "pending";
            appointment.ArrivedAtUtc = null;
            appointment.ArrivalBusinessDate = null;
            appointment.ArrivalQueueNumber = null;
            appointment.ArrivalRecordedByAccountId = null;
            appointment.UpdatedAtUtc = now;
            appointment.RowVersion++;
            database.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = appointment.Id,
                ActorAccountId = actor,
                FromStatusCode = previousAppointmentStatus,
                ToStatusCode = "pending",
                Reason = $"Pagamento cancelado: {reason}",
                StartsAtUtc = appointment.StartsAtUtc,
                EndsAtUtc = appointment.EndsAtUtc,
                OccurredAtUtc = now
            });
            await database.ProfessionalNotifications
                .Where(item => item.AppointmentId == appointment.Id && item.ReadAtUtc == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(item => item.ReadAtUtc, now)
                    .SetProperty(item => item.RowVersion, item => item.RowVersion + 1), cancellationToken);
        }

        var date = await OperationalDateAsync(now, cancellationToken);
        foreach (var original in originals) database.CashMovements.Add(new CashMovement
        {
            OperationalDate = date.ToDateTime(TimeOnly.MinValue),
            DirectionCode = "outflow",
            TypeCode = "payment_reversal",
            MethodCode = original.MethodCode,
            Amount = original.Amount,
            CurrencyCode = "BRL",
            AppointmentId = payment.AppointmentId,
            PaymentId = payment.Id,
            RelatedMovementId = original.Id,
            CardLastFour = original.CardLastFour,
            AuthorizationReference = original.AuthorizationReference,
            ResponsibleAccountId = actor,
            Description = previousAppointmentStatus == "no_show"
                ? $"Estorno ao paciente do atendimento {appointment.AppointmentNumber} por não comparecimento"
                : $"Cancelamento do pagamento do atendimento {appointment.AppointmentNumber}",
            Reason = reason,
            IdempotencyKey = $"payment-reversal-{reversal.Id}-{original.Id}",
            OccurredAtUtc = now,
            AfterClosure = false,
        });
        database.PaymentEvents.Add(new PaymentEvent
        {
            PaymentId = payment.Id,
            SourceCode = "reversal",
            ProviderStatusCode = payment.ProviderStatusCode,
            NormalizedStatusCode = payment.StatusCode,
            EventFingerprint = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"reversal:{reversal.Id}")),
            ProviderOccurredAtUtc = payment.ProviderEventAtUtc,
            OccurredAtUtc = now,
            WasApplied = true,
        });
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        await audit.WriteAsync("payment.reversed", actor, "payment", payment.Id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string>
            {
                ["appointmentId"] = appointment.Id.ToString(CultureInfo.InvariantCulture),
                ["provider"] = payment.ProviderCode,
                ["previousAppointmentStatus"] = previousAppointmentStatus,
                ["newAppointmentStatus"] = appointment.StatusCode
            }, cancellationToken);
        return MapReversal(reversal, appointment.StatusCode);
    }

    public async Task RecordPaymentReceivedAsync(Payment payment, ulong? actor, DateTime occurredAtUtc, CancellationToken cancellationToken,
        IReadOnlyList<(string MethodCode, decimal Amount, string? CardLastFour, string? AuthorizationReference)>? allocations = null)
    {
        var existing = await database.CashMovements
            .Where(item => item.PaymentId == payment.Id && item.TypeCode == "payment_received")
            .Select(item => item.Amount).ToArrayAsync(cancellationToken);
        if (existing.Length > 0)
        {
            if (existing.Sum() != payment.Amount)
                throw Conflict("As parcelas do pagamento no caixa estão incompletas.");
            return;
        }
        var appointmentNumber = await database.Appointments.Where(item => item.Id == payment.AppointmentId)
            .Select(item => item.AppointmentNumber).SingleAsync(cancellationToken);
        var date = await OperationalDateAsync(occurredAtUtc, cancellationToken);
        if (await IsClosedAsync(date, cancellationToken))
            throw Conflict("O caixa de hoje está fechado. Reabra-o antes de confirmar um pagamento.");
        var portions = allocations ?? [(LedgerMethod(payment), payment.Amount, payment.CardLastFour, payment.AuthorizationReference)];
        if (portions.Count == 0 || portions.Sum(item => item.Amount) != payment.Amount)
            throw Conflict("A soma das formas de pagamento não corresponde ao total do atendimento.");
        for (var index = 0; index < portions.Count; index++) database.CashMovements.Add(new CashMovement
        {
            OperationalDate = date.ToDateTime(TimeOnly.MinValue),
            DirectionCode = "entry",
            TypeCode = "payment_received",
            MethodCode = portions[index].MethodCode,
            Amount = portions[index].Amount,
            CardLastFour = portions[index].CardLastFour,
            AuthorizationReference = portions[index].AuthorizationReference,
            CurrencyCode = "BRL",
            AppointmentId = payment.AppointmentId,
            PaymentId = payment.Id,
            ResponsibleAccountId = actor,
            Description = $"Pagamento do atendimento {appointmentNumber}",
            IdempotencyKey = $"payment-received-{payment.Id}-{index + 1}",
            OccurredAtUtc = occurredAtUtc,
            AfterClosure = false,
        });
    }

    private IQueryable<CashMovement> Filtered(DateOnly date, string? method, string? type, ulong? appointmentNumber,
        string? patient, string? responsible, string? cardLastFour, string? authorizationReference,
        ulong? professionalAccountId = null, ulong? responsibleAccountId = null)
    {
        var day = date.ToDateTime(TimeOnly.MinValue);
        IQueryable<CashMovement> query = database.CashMovements.AsNoTracking().Where(item => item.OperationalDate == day)
            .Include(item => item.Appointment!).ThenInclude(item => item.PatientAccount)
            .Include(item => item.Payment)
            .Include(item => item.ResponsibleAccount);
        if (method is not null) query = query.Where(item => item.MethodCode == method);
        if (type is not null) query = query.Where(item => item.TypeCode == type);
        if (appointmentNumber.HasValue) query = query.Where(item => item.Appointment != null && item.Appointment.AppointmentNumber == appointmentNumber);
        var patientTerm = Text(patient); if (patientTerm is not null) query = query.Where(item => item.Appointment != null && item.Appointment.PatientAccount.FullName.Contains(patientTerm));
        var responsibleTerm = Text(responsible); if (responsibleTerm is not null) query = query.Where(item => item.ResponsibleAccount != null && item.ResponsibleAccount.FullName.Contains(responsibleTerm));
        if (professionalAccountId.HasValue) query = query.Where(item => item.Appointment != null && item.Appointment.ProfessionalAccountId == professionalAccountId.Value);
        if (responsibleAccountId.HasValue) query = query.Where(item => item.ResponsibleAccountId == responsibleAccountId.Value);
        if (cardLastFour is not null) query = query.Where(item => item.CardLastFour == cardLastFour
            || item.CardLastFour == null && item.Payment != null && item.Payment.CardLastFour == cardLastFour);
        if (authorizationReference is not null) query = query.Where(item =>
            item.AuthorizationReference != null && item.AuthorizationReference.Contains(authorizationReference)
            || item.AuthorizationReference == null && item.Payment != null
                && item.Payment.AuthorizationReference != null && item.Payment.AuthorizationReference.Contains(authorizationReference));
        return query;
    }

    private static async Task<CashSummaryResponse> SummaryAsync(IQueryable<CashMovement> query, CancellationToken cancellationToken)
    {
        var rows = await query.Select(item => new { item.DirectionCode, item.TypeCode, item.MethodCode, item.Amount }).ToArrayAsync(cancellationToken);
        var gross = rows.Where(item => item.TypeCode == "payment_received").Sum(item => item.Amount);
        var reversals = rows.Where(item => item.TypeCode == "payment_reversal").Sum(item => item.Amount);
        var supplies = rows.Where(item => item.TypeCode == "supply").Sum(item => item.Amount);
        var withdrawals = rows.Where(item => item.TypeCode == "withdrawal").Sum(item => item.Amount);
        var adjustments = rows.Where(item => item.TypeCode is "adjustment" or "provider_fee")
            .Sum(item => item.DirectionCode == "entry" ? item.Amount : -item.Amount);
        var methods = rows.GroupBy(item => item.MethodCode).OrderBy(item => item.Key).Select(group =>
        {
            var entries = group.Where(item => item.DirectionCode == "entry").Sum(item => item.Amount);
            var outflows = group.Where(item => item.DirectionCode == "outflow").Sum(item => item.Amount);
            return new CashMethodTotalResponse(group.Key, entries, outflows, entries - outflows, group.Count());
        }).ToArray();
        var net = rows.Sum(item => item.DirectionCode == "entry" ? item.Amount : -item.Amount);
        return new(gross, reversals, supplies, withdrawals, adjustments, net, rows.Length, methods);
    }

    private async Task<CashClosureResponse?> ClosureAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var closure = await ActiveClosureEntityAsync(date, cancellationToken);
        return closure is null ? null : await MapClosureAsync(closure, cancellationToken);
    }

    private Task<CashClosure?> ActiveClosureEntityAsync(DateOnly date, CancellationToken cancellationToken) =>
        database.CashClosures.AsNoTracking().Include(item => item.ClosedByAccount)
            .Where(item => item.OperationalDate == date.ToDateTime(TimeOnly.MinValue) && item.CashReopening == null)
            .OrderByDescending(item => item.ClosedAtUtc).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<bool> IsClosedAsync(DateOnly date, CancellationToken cancellationToken) =>
        await ActiveClosureEntityAsync(date, cancellationToken) is not null;

    private async Task<bool> SettingAsync(string key, bool defaultValue, CancellationToken cancellationToken)
    {
        var value = await database.ApplicationSettings.AsNoTracking().Where(item => item.SettingKey == key)
            .Select(item => item.ValueJson).SingleOrDefaultAsync(cancellationToken);
        return value is null ? defaultValue : bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static Task<CashClosureResponse> MapClosureAsync(CashClosure item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var methods = JsonSerializer.Deserialize<CashMethodTotalResponse[]>(item.TotalsByMethodJson) ?? [];
        CashSummaryResponse summary = new(item.GrossEntries, item.PaymentReversals, item.Supplies, item.Withdrawals,
            item.AdjustmentsNet, item.NetTotal, checked((int)item.MovementCount), methods);
        return Task.FromResult(new CashClosureResponse(item.Id, DateOnly.FromDateTime(item.OperationalDate), item.ClosedByAccount.FullName,
            DateTime.SpecifyKind(item.ClosedAtUtc, DateTimeKind.Utc), item.LastMovementId, summary));
    }

    private static CashMovementResponse Map(CashMovement item) => new(item.Id, DateOnly.FromDateTime(item.OperationalDate), item.DirectionCode,
        item.TypeCode, item.MethodCode, item.Amount, item.AppointmentId, item.Appointment?.AppointmentNumber, item.PaymentId,
        item.RelatedMovementId, item.Appointment?.PatientAccount.FullName, item.ResponsibleAccount?.FullName,
        item.CardLastFour ?? item.Payment?.CardLastFour, item.AuthorizationReference ?? item.Payment?.AuthorizationReference, item.Description,
        item.Reason, DateTime.SpecifyKind(item.OccurredAtUtc, DateTimeKind.Utc), item.AfterClosure);

    private static PaymentReversalResponse MapReversal(PaymentReversal item, string appointmentStatus) => new(item.Id, item.PaymentId, item.Payment.AppointmentId,
        item.StatusCode, item.Payment.StatusCode, item.RequestedAtUtc, item.CompletedAtUtc,
        item.StatusCode == "confirmed" && appointmentStatus == "pending", item.Payment.RowVersion);

    private async Task<(string LegalName, string TimezoneName)> ClinicAsync(CancellationToken cancellationToken) =>
        await database.Clinics.AsNoTracking().Select(item => new ValueTuple<string, string>(item.LegalName, item.TimezoneName)).SingleAsync(cancellationToken);

    private async Task<DateOnly> OperationalDateAsync(DateTime utc, CancellationToken cancellationToken)
    {
        var timezoneName = (await ClinicAsync(cancellationToken)).TimezoneName;
        try
        {
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneName);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timezone));
        }
        catch (TimeZoneNotFoundException) { throw new CashRuleException(503, "O fuso da clínica está indisponível."); }
    }

    private static string LedgerMethod(Payment payment) => payment.ProviderCode == "pagbank" ? "pagbank_online" :
        Methods.Contains(payment.MethodCode, StringComparer.Ordinal) ? payment.MethodCode! : "other";
    private static string ManualDescription(string type, string direction) => type switch
    {
        "supply" => "Suprimento manual",
        "withdrawal" => "Sangria manual",
        _ => direction == "entry" ? "Ajuste manual de entrada" : "Ajuste manual de saída",
    };
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeCardLastFour(string? value)
    {
        var normalized = Text(value);
        if (normalized is not null && (normalized.Length != 4 || normalized.Any(character => !char.IsAsciiDigit(character))))
            throw Invalid("Informe exatamente os quatro últimos dígitos do cartão.");
        return normalized;
    }
    private static string? NormalizeAuthorization(string? value)
    {
        var normalized = Text(value);
        if (normalized?.Length > 100) throw Invalid("O número de autorização deve ter no máximo 100 caracteres.");
        return normalized;
    }
    private static string Useful(string value, int minimum, string message) => string.IsNullOrWhiteSpace(value) || value.Trim().Length < minimum ? throw Invalid(message) : value.Trim();
    private static void ValidateQuery(DateOnly date, DateOnly today, string? method, string? type, int page, int pageSize)
    {
        if (date.Year is < 2000 or > 2200) throw Invalid("Data operacional inválida.");
        if (date > today) throw Invalid("A data do caixa não pode ser posterior a hoje.");
        if (method is not null && !Methods.Contains(method, StringComparer.Ordinal)) throw Invalid("Forma de pagamento inválida.");
        if (type is not null && !Types.Contains(type, StringComparer.Ordinal)) throw Invalid("Tipo de movimento inválido.");
        if (page < 1 || pageSize is < 1 or > 100) throw Invalid("Paginação inválida.");
    }
    private static void ValidateIdempotency(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length is < 16 or > 100 || key.Any(character => character > 127 || char.IsWhiteSpace(character) || char.IsControl(character))) throw Invalid("Idempotency-Key inválida."); }
    private static long ToCents(decimal amount) => decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    private static string NormalizeProviderKey(string key) { var value = new string(key.Where(char.IsAsciiLetterOrDigit).ToArray()); return value.Length >= 16 ? value : Guid.NewGuid().ToString("N"); }
    private Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        database.Database.CurrentTransaction is not null
            ? Task.FromResult<IDbContextTransaction?>(null)
            : BeginOwnedTransactionAsync(cancellationToken);
    private async Task<IDbContextTransaction?> BeginOwnedTransactionAsync(CancellationToken cancellationToken) =>
        await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    private static CashRuleException Invalid(string message) => new(400, message);
    private static CashRuleException Forbidden(string message) => new(403, message);
    private static CashRuleException Missing(string message) => new(404, message);
    private static CashRuleException Conflict(string message) => new(409, message);
}
