namespace ViverApp.Web;

public sealed record PatientClinic(string Name, string Address, string? Phone, string? RouteUrl, string Timezone);
public sealed record PatientService(uint Id, string Name, string? Description, string CategoryCode, string ModalityCode, ushort DurationMinutes, decimal BasePrice, decimal DiscountPercent, decimal PriceAmount);
public sealed record PatientPromotion(string Title, string Description, string? Url);
public sealed record PatientHomeData(string FullName, bool IsPremium, PatientClinic Clinic, PatientAppointment? NextAppointment, IReadOnlyList<PatientPromotion> Promotions);
public sealed record PatientAppointment(WebAppointment Appointment, string CategoryCode, string Specialties, string PaymentStatus,
    string PaymentLocation, decimal BasePrice, decimal DiscountPercent, bool CanPay, bool CanCancel, bool CanReschedule, bool CanJoinOnline, bool HasReport, byte? Rating, bool CanChooseClinic);
public sealed record PatientPayment(ulong AppointmentId, ulong? PaymentId, string Service, string DoctorName, DateTime StartsAtUtc,
    string StatusCode, decimal Amount, string? Method, string Location, DateTime? PaidAtUtc, bool CanChooseClinic, bool CanPay, string ModalityCode);
public sealed record PatientAddress(string PostalCode, string Street, string Number, string? Complement, string District, string City, string StateCode);
public sealed record PatientProfileData(string FullName, string? Email, string? Phone, bool EmailConfirmed, bool PhoneConfirmed,
    string? TaxId, DateOnly? BirthDate, PatientAddress? Address, bool EmailEnabled, bool SmsEnabled, ulong RowVersion);
public sealed record PatientPremium(bool IsPremium, ulong? MembershipId, string StatusCode, string? RejectionReason,
    DateTime? ReviewedAtUtc, DateTime? NextRequestAtUtc, bool CanRequest, decimal DiscountPercent, ulong RowVersion, Guid? ProofDocumentId, IReadOnlyList<PatientPlan> Plans);
public sealed record PatientPlan(uint Id, string Name, decimal DiscountPercent);
public sealed record PatientDocument(Guid Id, string Name, string ContentType, uint SizeBytes);
public sealed record PatientSecurity(bool HasPassword, bool GoogleLinked, bool RecentAuthentication);
public sealed record PatientReport(ulong AppointmentId, string DoctorName, string AppointmentTypeName, DateTime AppointmentAtUtc, string ClinicalSummary, string? Recommendations, DateTime PublishedAtUtc);
public sealed record PatientApiResult<T>(bool Ok, T? Data, string? Error, int Status);

public static class PatientLabels
{
    public static string Status(string code) => code switch
    {
        "pending" => "Pendente",
        "confirmed" => "Confirmado",
        "arrived" => "Paciente chegou",
        "in_progress" => "Em atendimento",
        "completed" => "Concluído",
        "no_show" => "Não compareceu",
        "canceled" => "Cancelado",
        "rescheduled" => "Reagendado",
        "unpaid" => "A pagar",
        "paid" => "Pago",
        "authorized" => "Em análise",
        "failed" => "Não aprovado",
        "refunded" => "Reembolsado",
        "active" => "Premium aprovado",
        "rejected" => "Solicitação rejeitada",
        "expired" => "Expirado",
        "none" => "Sem solicitação",
        _ => "Em processamento"
    };
    public static string Category(string code) => code switch { "surgery" => "Cirurgia", "examination" => "Exame", _ => "Consulta" };
    public static string StatusFace(string code) => code switch { "pending" => "😞", "confirmed" => "😊", "arrived" => "🙋", "in_progress" => "🩺", "completed" => "😌", "canceled" => "✖", "no_show" => "😶", "rescheduled" => "🔄", _ => "•" };
    public static string Money(decimal value) => value.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
    public static string Method(string? value) => value switch { "PIX" => "Pix", "CREDIT_CARD" => "Cartão de crédito", "DEBIT_CARD" => "Cartão de débito", "BOLETO" => "Boleto", _ => "Não informado" };
}
