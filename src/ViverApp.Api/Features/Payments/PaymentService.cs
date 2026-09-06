using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Payments;

public sealed class PaymentService(
    ViverAppDbContext database,
    IPagBankClient pagBank,
    PagBankOptions options,
    IPaymentAuditWriter auditWriter,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PaymentCheckoutResult> CreateCheckoutAsync(
        ulong patientId,
        ulong appointmentId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        ValidateIdempotencyKey(idempotencyKey);
        var scope = $"payment.checkout:{patientId}";
        var requestHash = SHA256.HashData(Encoding.UTF8.GetBytes(appointmentId.ToString(CultureInfo.InvariantCulture)));
        var stored = await database.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeCode == scope && item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (stored is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(stored.RequestHash, requestHash))
            {
                throw Conflict("A chave de idempotência já foi usada para outra operação.");
            }

            return new(DeserializeStored(stored), true);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var appointment = await database.Appointments
            .FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {appointmentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (appointment is null || appointment.PatientAccountId != patientId)
        {
            throw NotFound("Agendamento não encontrado.");
        }

        var concurrentStored = await database.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeCode == scope && item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (concurrentStored is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(concurrentStored.RequestHash, requestHash))
            {
                throw Conflict("A chave de idempotência já foi usada para outra operação.");
            }

            await transaction.CommitAsync(cancellationToken);
            return new(DeserializeStored(concurrentStored), true);
        }

        if (appointment.StatusCode is not ("pending" or "confirmed"))
        {
            throw Conflict("Este agendamento não aceita um novo checkout.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (appointment.StartsAtUtc <= now || appointment.PriceAmount <= 0 || appointment.CurrencyCode != "BRL")
        {
            throw Conflict("O agendamento não possui uma cobrança válida.");
        }

        var payment = await database.Payments.SingleOrDefaultAsync(item => item.AppointmentId == appointmentId, cancellationToken);
        if (payment?.CheckoutUrl is not null)
        {
            var existing = ToResponse(payment);
            StoreIdempotency(scope, idempotencyKey, requestHash, existing, now);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(existing, true);
        }

        if (payment is null)
        {
            payment = new Payment
            {
                AppointmentId = appointmentId,
                ProviderReferenceAppointmentId = appointmentId,
                ProviderCode = "pagbank",
                StatusCode = "pending",
                Amount = appointment.PriceAmount,
                CurrencyCode = "BRL",
                IdempotencyKey = Guid.NewGuid(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                NextReconciliationAtUtc = now.AddMinutes(options.ReconciliationIntervalMinutes),
                RowVersion = 1,
            };
            database.Payments.Add(payment);
            await database.SaveChangesAsync(cancellationToken);
        }

        var appointmentTypeName = await database.AppointmentTypes.AsNoTracking()
            .Where(item => item.Id == appointment.AppointmentTypeId)
            .Select(item => item.Name)
            .SingleAsync(cancellationToken);
        var reference = $"appointment-{appointment.Id}";
        var expiration = new DateTimeOffset(now, TimeSpan.Zero).AddMinutes(options.CheckoutLifetimeMinutes);
        var returnUrl = new UriBuilder(options.ReturnUrl)
        {
            Query = $"appointmentId={appointment.Id.ToString(CultureInfo.InvariantCulture)}",
        }.Uri;
        var command = new PagBankCheckoutCommand(
            reference,
            expiration,
            reference,
            LimitText(appointmentTypeName, 100),
            ToCents(payment.Amount),
            returnUrl,
            returnUrl,
            options.WebhookUrl);
        var provider = await pagBank.CreateCheckoutAsync(
            command,
            payment.IdempotencyKey.ToString("N", CultureInfo.InvariantCulture),
            cancellationToken);
        ValidateCheckout(provider, reference);

        payment.ProviderCheckoutId = provider.Id;
        appointment.PaymentLocationCode = "web";
        appointment.UpdatedAtUtc = now;
        appointment.RowVersion++;
        payment.CheckoutUrl = provider.PayUrl!.AbsoluteUri;
        payment.CheckoutExpiresAtUtc = provider.ExpiresAtUtc ?? expiration.UtcDateTime;
        payment.ProviderStatusCode = provider.Status;
        payment.ProviderEventAtUtc = provider.OccurredAtUtc;
        payment.UpdatedAtUtc = now;
        payment.RowVersion++;
        AddEvent(payment, null, "checkout", provider, "pending", true, null, now);
        var response = ToResponse(payment);
        StoreIdempotency(scope, idempotencyKey, requestHash, response, now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await auditWriter.WriteAsync(
            "payment.checkout_created",
            patientId,
            payment.Id,
            new Dictionary<string, string> { ["environment"] = options.Environment },
            cancellationToken);
        return new(response, false);
    }

    public async Task<PaymentResponse> GetAsync(
        ulong patientId,
        ulong appointmentId,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var snapshot = await database.Payments.AsNoTracking()
            .Where(item => item.AppointmentId == appointmentId && item.Appointment.PatientAccountId == patientId)
            .Select(item => new { item.Id, item.NextReconciliationAtUtc })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound("Pagamento não encontrado.");
        if (refresh
            && options.Enabled
            && snapshot.NextReconciliationAtUtc is not null
            && snapshot.NextReconciliationAtUtc <= timeProvider.GetUtcNow().UtcDateTime)
        {
            await ReconcileOneAsync(snapshot.Id, cancellationToken);
        }

        var payment = await database.Payments.AsNoTracking()
            .SingleAsync(item => item.Id == snapshot.Id, cancellationToken);
        return ToResponse(payment);
    }

    public async Task<PagBankNotificationResult> ProcessWebhookAsync(
        ReadOnlyMemory<byte> rawPayload,
        string receivedSignature,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (!PagBankWebhookAuthenticator.Verify(options.Token, rawPayload.Span, receivedSignature, out var authenticityHash))
        {
            throw new PaymentRuleException(StatusCodes.Status401Unauthorized, "Notificação PagBank inválida.");
        }

        var payloadHash = SHA256.HashData(rawPayload.Span);
        if (await database.PaymentWebhookReceipts.AsNoTracking()
            .AnyAsync(item => item.ProviderCode == "pagbank" && item.PayloadSha256 == payloadHash, cancellationToken))
        {
            return new(true, "replayed");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var receipt = new PaymentWebhookReceipt
        {
            ProviderCode = "pagbank",
            PayloadSha256 = payloadHash,
            AuthenticitySha256 = authenticityHash,
            ProcessingStatusCode = "received",
            ReceivedAtUtc = now,
        };
        database.PaymentWebhookReceipts.Add(receipt);
        await database.SaveChangesAsync(cancellationToken);

        PagBankResource resource;
        try
        {
            using var document = JsonDocument.Parse(rawPayload);
            resource = PagBankResourceParser.Parse(document.RootElement);
        }
        catch (JsonException)
        {
            FinishReceipt(receipt, "failed", "invalid_json", null, now);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(false, "invalid_json");
        }
        catch (PaymentRuleException)
        {
            FinishReceipt(receipt, "failed", "invalid_payload", null, now);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(false, "invalid_payload");
        }

        receipt.ProviderResourceId = resource.Id;
        var payment = await FindPaymentForUpdateAsync(resource, cancellationToken);
        if (payment is null)
        {
            FinishReceipt(receipt, "ignored", "unknown_reference", resource.Id, now);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(false, "unknown_reference");
        }

        if (resource.TotalCents.HasValue && resource.TotalCents != ToCents(payment.Amount))
        {
            FinishReceipt(receipt, "ignored", "amount_mismatch", resource.Id, now);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await auditWriter.WriteAsync("payment.webhook_amount_mismatch", null, payment.Id, null, cancellationToken);
            return new(false, "amount_mismatch");
        }

        string target;
        try
        {
            target = PaymentStateMachine.Normalize(resource.Status, resource.TotalCents, resource.RefundedCents);
        }
        catch (PaymentRuleException)
        {
            FinishReceipt(receipt, "failed", "unsupported_status", resource.Id, now);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            await auditWriter.WriteAsync("payment.webhook_unsupported_status", null, payment.Id, null, cancellationToken);
            return new(false, "unsupported_status");
        }

        var transition = PaymentStateMachine.Decide(payment.StatusCode, target, payment.ProviderEventAtUtc, resource.OccurredAtUtc);
        ApplyProviderEvent(payment, receipt, "webhook", resource, transition, now);
        FinishReceipt(receipt, "processed", transition.Apply ? "applied" : transition.IgnoredReason!, resource.Id, now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await auditWriter.WriteAsync(
            transition.Apply ? "payment.webhook_applied" : "payment.webhook_ignored",
            null,
            payment.Id,
            new Dictionary<string, string> { ["result"] = receipt.ResultCode! },
            cancellationToken);
        return new(false, receipt.ResultCode!);
    }

    public async Task<PaymentResponse> RefundAsync(
        ulong actorId,
        ulong paymentId,
        string idempotencyKey,
        PaymentRefundRequest request,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        if (!options.RefundsEnabled)
        {
            throw new PaymentRuleException(StatusCodes.Status503ServiceUnavailable, "Reembolsos por API não estão habilitados.");
        }

        ValidateIdempotencyKey(idempotencyKey);
        var scope = $"payment.refund:{actorId}";
        var requestHash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', paymentId, request.RowVersion, request.Reason.Trim())));
        var stored = await database.IdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(item => item.ScopeCode == scope && item.IdempotencyKey == idempotencyKey, cancellationToken);
        if (stored is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(stored.RequestHash, requestHash))
            {
                throw Conflict("A chave de idempotência já foi usada para outra operação.");
            }

            return DeserializeStored(stored);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var payment = await database.Payments
            .FromSqlInterpolated($"SELECT * FROM payments WHERE id = {paymentId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound("Pagamento não encontrado.");
        var appointmentStatus = await database.Appointments.AsNoTracking()
            .Where(item => item.Id == payment.AppointmentId)
            .Select(item => item.StatusCode)
            .SingleAsync(cancellationToken);
        if (payment.RowVersion != request.RowVersion)
        {
            throw Conflict("O pagamento foi alterado por outra operação. Recarregue e tente novamente.");
        }

        if (payment.StatusCode != "paid" || appointmentStatus != "canceled" || string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
        {
            throw Conflict("Somente um pagamento pago de agendamento cancelado pode ser reembolsado.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var provider = await pagBank.RefundChargeAsync(
            payment.ProviderTransactionId,
            ToCents(payment.Amount),
            NormalizeProviderIdempotencyKey(idempotencyKey),
            cancellationToken);
        payment.StatusCode = "refunded";
        payment.ProviderStatusCode = provider.Status;
        payment.ProviderEventAtUtc = provider.OccurredAtUtc ?? now;
        payment.RefundAmount = payment.Amount;
        payment.RefundedAtUtc = now;
        payment.UpdatedAtUtc = now;
        payment.NextReconciliationAtUtc = null;
        payment.RowVersion++;
        AddEvent(payment, null, "refund", provider, "refunded", true, null, now);
        var response = ToResponse(payment);
        StoreIdempotency(scope, idempotencyKey, requestHash, response, now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await auditWriter.WriteAsync(
            "payment.refunded",
            actorId,
            payment.Id,
            new Dictionary<string, string> { ["reason"] = LimitText(request.Reason.Trim(), 200) },
            cancellationToken);
        return response;
    }

    public async Task<int> ReconcileDueAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var ids = await database.Payments.AsNoTracking()
            .Where(item => item.ProviderCode == "pagbank"
                && item.ProviderCheckoutId != null
                && item.NextReconciliationAtUtc != null
                && item.NextReconciliationAtUtc <= now
                && item.StatusCode != "refunded")
            .OrderBy(item => item.NextReconciliationAtUtc)
            .Select(item => item.Id)
            .Take(options.ReconciliationBatchSize)
            .ToArrayAsync(cancellationToken);
        foreach (var id in ids)
        {
            try
            {
                await ReconcileOneAsync(id, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await MarkReconciliationFailureAsync(id, cancellationToken);
            }
        }

        return ids.Length;
    }

    private async Task ReconcileOneAsync(ulong paymentId, CancellationToken cancellationToken)
    {
        var snapshot = await database.Payments.AsNoTracking()
            .Where(item => item.Id == paymentId && item.ProviderCheckoutId != null)
            .Select(item => new { item.ProviderCheckoutId, item.Appointment.StatusCode })
            .SingleOrDefaultAsync(cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        PagBankResource resource;
        if (snapshot.StatusCode == "canceled")
        {
            resource = await pagBank.InactivateCheckoutAsync(snapshot.ProviderCheckoutId!, cancellationToken);
        }
        else
        {
            resource = await pagBank.GetCheckoutAsync(snapshot.ProviderCheckoutId!, cancellationToken);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var payment = await database.Payments
            .FromSqlInterpolated($"SELECT * FROM payments WHERE id = {paymentId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var target = PaymentStateMachine.Normalize(resource.Status, resource.TotalCents, resource.RefundedCents);
        var transition = PaymentStateMachine.Decide(payment.StatusCode, target, payment.ProviderEventAtUtc, resource.OccurredAtUtc);
        ApplyProviderEvent(payment, null, "reconciliation", resource, transition, now);
        payment.LastReconciledAtUtc = now;
        payment.ReconciliationAttemptCount = 0;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task MarkReconciliationFailureAsync(ulong paymentId, CancellationToken cancellationToken)
    {
        var payment = await database.Payments.SingleOrDefaultAsync(item => item.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        payment.ReconciliationAttemptCount = checked((ushort)Math.Min(payment.ReconciliationAttemptCount + 1, ushort.MaxValue));
        var delay = Math.Min(60, Math.Pow(2, Math.Min(payment.ReconciliationAttemptCount, (ushort)5)) * options.ReconciliationIntervalMinutes);
        payment.NextReconciliationAtUtc = now.AddMinutes(delay);
        payment.UpdatedAtUtc = now;
        payment.RowVersion++;
        await database.SaveChangesAsync(cancellationToken);
    }

    private async Task<Payment?> FindPaymentForUpdateAsync(PagBankResource resource, CancellationToken cancellationToken)
    {
        ulong appointmentId = 0;
        if (resource.ReferenceId?.StartsWith("appointment-", StringComparison.Ordinal) == true)
        {
            _ = ulong.TryParse(resource.ReferenceId.AsSpan("appointment-".Length), NumberStyles.None, CultureInfo.InvariantCulture, out appointmentId);
        }

        return await database.Payments
            .FromSqlInterpolated($"SELECT * FROM payments WHERE ((provider_reference_appointment_id = {appointmentId} OR (provider_reference_appointment_id IS NULL AND appointment_id = {appointmentId})) AND {appointmentId} > 0) OR provider_checkout_id = {resource.Id} OR provider_transaction_id = {resource.Id} LIMIT 1 FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    private void ApplyProviderEvent(
        Payment payment,
        PaymentWebhookReceipt? receipt,
        string source,
        PagBankResource resource,
        PaymentTransition transition,
        DateTime now)
    {
        if (receipt is null && !transition.Apply && transition.IgnoredReason == "duplicate_status")
        {
            return;
        }

        AddEvent(payment, receipt, source, resource, transition.TargetStatus, transition.Apply, transition.IgnoredReason, now);
        if (!transition.Apply)
        {
            return;
        }

        payment.StatusCode = transition.TargetStatus;
        if (resource.MethodCode is "PIX" or "CREDIT_CARD" or "DEBIT_CARD" or "BOLETO") payment.MethodCode = resource.MethodCode;
        payment.ProviderStatusCode = resource.Status;
        payment.ProviderEventAtUtc = resource.OccurredAtUtc ?? now;
        if (resource.RawKind == "charge")
        {
            payment.ProviderTransactionId = resource.Id;
        }

        payment.UpdatedAtUtc = now;
        payment.RowVersion++;
        payment.NextReconciliationAtUtc = transition.TargetStatus is "paid" or "canceled" or "refunded"
            ? null
            : now.AddMinutes(options.ReconciliationIntervalMinutes);
        if (transition.TargetStatus == "paid")
        {
            payment.PaidAtUtc ??= resource.OccurredAtUtc ?? now;
            ConfirmAppointment(payment.AppointmentId, now);
        }
        else if (transition.TargetStatus == "canceled")
        {
            payment.CanceledAtUtc ??= now;
        }
        else if (transition.TargetStatus == "refunded")
        {
            payment.RefundAmount = payment.Amount;
            payment.RefundedAtUtc = now;
        }
    }

    private void ConfirmAppointment(ulong appointmentId, DateTime now)
    {
        var appointment = database.Appointments.Local.SingleOrDefault(item => item.Id == appointmentId)
            ?? database.Appointments.Single(item => item.Id == appointmentId);
        if (appointment.StatusCode != "pending")
        {
            return;
        }

        appointment.StatusCode = "confirmed";
        appointment.UpdatedAtUtc = now;
        appointment.RowVersion++;
        database.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            ActorAccountId = null,
            FromStatusCode = "pending",
            ToStatusCode = "confirmed",
            Reason = "payment_confirmed",
            StartsAtUtc = appointment.StartsAtUtc,
            EndsAtUtc = appointment.EndsAtUtc,
            OccurredAtUtc = now,
        });
    }

    private static void AddEvent(
        Payment payment,
        PaymentWebhookReceipt? receipt,
        string source,
        PagBankResource resource,
        string normalizedStatus,
        bool applied,
        string? ignoredReason,
        DateTime now)
    {
        var material = string.Join('|', source, receipt?.Id, resource.Id, resource.Status, resource.OccurredAtUtc?.Ticks, resource.TotalCents, resource.RefundedCents);
        payment.PaymentEvents.Add(new PaymentEvent
        {
            WebhookReceipt = receipt,
            SourceCode = source,
            ProviderResourceId = resource.Id,
            ProviderStatusCode = resource.Status,
            NormalizedStatusCode = normalizedStatus,
            EventFingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(material)),
            ProviderOccurredAtUtc = resource.OccurredAtUtc,
            OccurredAtUtc = now,
            WasApplied = applied,
            IgnoredReasonCode = ignoredReason,
        });
    }

    private static void FinishReceipt(PaymentWebhookReceipt receipt, string status, string result, string? resourceId, DateTime now)
    {
        receipt.ProcessingStatusCode = status;
        receipt.ResultCode = result;
        receipt.ProviderResourceId = resourceId;
        receipt.ProcessedAtUtc = now;
    }

    private void StoreIdempotency(string scope, string key, byte[] hash, PaymentResponse response, DateTime now) =>
        database.IdempotencyRecords.Add(new IdempotencyRecord
        {
            ScopeCode = scope,
            IdempotencyKey = key,
            RequestHash = hash,
            ResponseStatusCode = StatusCodes.Status200OK,
            ResponseBodyJson = JsonSerializer.Serialize(response, JsonOptions),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(48),
        });

    private static PaymentResponse DeserializeStored(IdempotencyRecord record) =>
        JsonSerializer.Deserialize<PaymentResponse>(record.ResponseBodyJson ?? string.Empty, JsonOptions)
        ?? throw Conflict("A resposta idempotente armazenada é inválida.");

    private static PaymentResponse ToResponse(Payment payment) => new(
        payment.Id,
        payment.AppointmentId,
        payment.StatusCode,
        payment.Amount,
        payment.CurrencyCode,
        payment.CheckoutUrl,
        payment.CheckoutExpiresAtUtc,
        payment.ProviderStatusCode,
        payment.UpdatedAtUtc,
        payment.PaidAtUtc,
        payment.RefundedAtUtc,
        payment.RowVersion);

    private static void ValidateCheckout(PagBankResource resource, string reference)
    {
        if (!resource.Id.StartsWith("CHEC_", StringComparison.Ordinal)
            || resource.PayUrl is null
            || resource.PayUrl.AbsoluteUri.Length > 500
            || (resource.ReferenceId is not null && resource.ReferenceId != reference))
        {
            throw new PaymentRuleException(StatusCodes.Status502BadGateway, "O PagBank retornou um checkout inválido.");
        }
    }

    private static long ToCents(decimal amount)
    {
        var cents = decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (cents <= 0 || cents > 999_999_900m || cents != decimal.Truncate(cents))
        {
            throw Conflict("O valor do agendamento não pode ser enviado ao PagBank.");
        }

        return decimal.ToInt64(cents);
    }

    private static string NormalizeProviderIdempotencyKey(string value)
    {
        var normalized = new string(value.Where(char.IsLetterOrDigit).ToArray());
        return normalized.Length >= 16 ? normalized[..Math.Min(normalized.Length, 200)] : Guid.NewGuid().ToString("N");
    }

    private static string LimitText(string value, int maximum) => value.Length <= maximum ? value : value[..maximum];

    private void EnsureEnabled()
    {
        if (!options.Enabled)
        {
            throw new PaymentRuleException(StatusCodes.Status503ServiceUnavailable, "Pagamentos estão temporariamente indisponíveis.");
        }
    }

    private static void ValidateIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || key.Length is < 16 or > 100
            || key.Any(character => character > 127 || char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new PaymentRuleException(StatusCodes.Status400BadRequest, "O cabeçalho Idempotency-Key deve conter de 16 a 100 caracteres ASCII sem espaços.");
        }
    }

    private static PaymentRuleException NotFound(string message) => new(StatusCodes.Status404NotFound, message);
    private static PaymentRuleException Conflict(string message) => new(StatusCodes.Status409Conflict, message);
}
