using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ViverApp.Api.Features.PatientScheduling;

namespace ViverApp.Api.Features.CashManagement;

public sealed record CashPersonOptionResponse(ulong AccountId, string Name, string RoleCode);
public sealed record CashFilterOptionsResponse(
    IReadOnlyList<CashPersonOptionResponse> Professionals,
    IReadOnlyList<CashPersonOptionResponse> Responsibles);

public sealed record CashMethodTotalResponse(
    string MethodCode,
    decimal Entries,
    decimal Outflows,
    decimal Net,
    int MovementCount);

public sealed record CashSummaryResponse(
    decimal GrossEntries,
    decimal PaymentReversals,
    decimal Supplies,
    decimal Withdrawals,
    decimal AdjustmentsNet,
    decimal NetTotal,
    int MovementCount,
    IReadOnlyList<CashMethodTotalResponse> ByMethod);

public sealed record CashMovementResponse(
    ulong Id,
    DateOnly OperationalDate,
    string DirectionCode,
    string TypeCode,
    string MethodCode,
    decimal Amount,
    ulong? AppointmentId,
    ulong? AppointmentNumber,
    ulong? PaymentId,
    ulong? RelatedMovementId,
    string? PatientName,
    string? ResponsibleName,
    string? CardLastFour,
    string? AuthorizationReference,
    string Description,
    string? Reason,
    DateTime OccurredAtUtc,
    bool AfterClosure);

public sealed record CashClosureResponse(
    ulong Id,
    DateOnly OperationalDate,
    string ResponsibleName,
    DateTime ClosedAtUtc,
    ulong? LastMovementId,
    CashSummaryResponse Snapshot);

public sealed record CashReopeningResponse(
    ulong Id,
    ulong CashClosureId,
    string ResponsibleName,
    string Reason,
    DateTime ReopenedAtUtc);

public sealed record CashDayResponse(
    DateOnly OperationalDate,
    string TimezoneName,
    CashClosureResponse? Closure,
    bool IsClosed,
    bool CanClose,
    bool CanReopen,
    CashSummaryResponse Summary,
    CashSummaryResponse? CumulativeSummary,
    ulong? LastMovementId,
    SchedulingPage<CashMovementResponse> Page);

public sealed record CashPrintResponse(
    string ClinicName,
    DateOnly OperationalDate,
    string TimezoneName,
    string IssuedBy,
    DateTime IssuedAtUtc,
    string FilterDescription,
    CashClosureResponse? Closure,
    bool IsClosed,
    CashSummaryResponse Summary,
    IReadOnlyList<CashMovementResponse> Movements,
    bool TotalsOnly);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CashManualMovementRequest(
    [param: Required] string TypeCode,
    [param: Required] string DirectionCode,
    [param: Required] string MethodCode,
    [param: Range(typeof(decimal), "0.01", "9999999.00",
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] decimal Amount,
    ulong? RelatedMovementId,
    [param: Required, StringLength(500, MinimumLength = 5)] string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CashCloseRequest(ulong? ExpectedLastMovementId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CashReopenRequest(
    [param: Required, StringLength(500, MinimumLength = 5)] string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PaymentReversalRequest(
    [param: Range(1, long.MaxValue)] ulong RowVersion,
    [param: Required, StringLength(500, MinimumLength = 5)] string Reason);

public sealed record PaymentReversalResponse(
    ulong ReversalId,
    ulong PaymentId,
    ulong AppointmentId,
    string StatusCode,
    string PaymentStatusCode,
    DateTime RequestedAtUtc,
    DateTime? CompletedAtUtc,
    bool CanCreateReplacementPayment,
    ulong PaymentRowVersion);

public sealed class CashRuleException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
