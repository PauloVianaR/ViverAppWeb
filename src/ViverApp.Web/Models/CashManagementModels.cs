namespace ViverApp.Web;

public sealed record CashPersonOption(ulong AccountId, string Name, string RoleCode);
public sealed record CashFilterOptions(IReadOnlyList<CashPersonOption> Professionals,
    IReadOnlyList<CashPersonOption> Responsibles);

public sealed record CashMethodTotal(string MethodCode, decimal Entries, decimal Outflows, decimal Net, int MovementCount);
public sealed record CashSummary(decimal GrossEntries, decimal PaymentReversals, decimal Supplies, decimal Withdrawals,
    decimal AdjustmentsNet, decimal NetTotal, int MovementCount, IReadOnlyList<CashMethodTotal> ByMethod);
public sealed record CashMovement(ulong Id, DateOnly OperationalDate, string DirectionCode, string TypeCode, string MethodCode,
    decimal Amount, ulong? AppointmentId, ulong? AppointmentNumber, ulong? PaymentId, ulong? RelatedMovementId,
    string? PatientName, string? ResponsibleName, string? CardLastFour, string? AuthorizationReference,
    string Description, string? Reason, DateTime OccurredAtUtc, bool AfterClosure);
public sealed record CashClosure(ulong Id, DateOnly OperationalDate, string ResponsibleName, DateTime ClosedAtUtc,
    ulong? LastMovementId, CashSummary Snapshot);
public sealed record CashReopening(ulong Id, ulong CashClosureId, string ResponsibleName, string Reason, DateTime ReopenedAtUtc);
public sealed record CashDay(DateOnly OperationalDate, string TimezoneName, CashClosure? Closure, bool IsClosed,
    bool CanClose, bool CanReopen, CashSummary Summary, CashSummary? CumulativeSummary, ulong? LastMovementId,
    WebPage<CashMovement> Page);
public sealed record CashPrint(string ClinicName, DateOnly OperationalDate, string TimezoneName, string IssuedBy,
    DateTime IssuedAtUtc, string FilterDescription, CashClosure? Closure, bool IsClosed, CashSummary Summary,
    IReadOnlyList<CashMovement> Movements, bool TotalsOnly);
public sealed record PaymentReversalResult(ulong ReversalId, ulong PaymentId, ulong AppointmentId, string StatusCode,
    string PaymentStatusCode, DateTime RequestedAtUtc, DateTime? CompletedAtUtc, bool CanCreateReplacementPayment,
    ulong PaymentRowVersion);

public static class CashLabels
{
    public static string Method(string code) => code switch
    {
        "cash" => "Dinheiro",
        "pix" => "Pix",
        "debit_card" => "Cartão de débito",
        "credit_card" => "Cartão de crédito",
        "pagbank_online" => "PagBank online",
        _ => "Outras formas",
    };
    public static string Type(string code) => code switch
    {
        "payment_received" => "Pagamento recebido",
        "payment_reversal" => "Cancelamento de pagamento",
        "supply" => "Suprimento",
        "withdrawal" => "Sangria",
        "adjustment" => "Ajuste corretivo",
        "provider_fee" => "Tarifa do provedor",
        _ => code,
    };
}
