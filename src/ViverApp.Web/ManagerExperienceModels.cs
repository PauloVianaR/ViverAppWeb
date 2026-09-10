namespace ViverApp.Web;

public sealed record ManagerHomeData(ManagerProfileData Profile, ManagerHomeCounters Counters, ManagerHomeSources Sources, IReadOnlyList<ManagerAppointment> Today);
public sealed record ManagerHomeCounters(int Today, int Doctors, int Paid, int PendingPayment, int Online, int InPerson);
public sealed record ManagerHomeSources(IReadOnlyList<ulong> Today, IReadOnlyList<string> Doctors, IReadOnlyList<ulong> Paid,
    IReadOnlyList<ulong> PendingPayment, IReadOnlyList<ulong> Online, IReadOnlyList<ulong> InPerson);
public sealed record ManagerProfileData(ulong AccountId, string FullName, string? Email, string? Phone, string? TaxId,
    bool EmailVerified, bool PhoneVerified, bool EmailEnabled, bool SmsEnabled, ulong AccountRowVersion, ulong PreferenceRowVersion);
public sealed record ManagerDoctor(ulong AccountId, string FullName, string LicenseLabel);
public sealed record ManagerService(uint Id, string Name, string CategoryCode, string ModalityCode, ushort DurationMinutes, decimal BasePrice);
public sealed record ManagerReportMetadata(bool Exists, string? StatusCode, uint VersionCount, DateTime? PublishedAtUtc);
public sealed record ManagerPaymentMetadata(ulong? Id, string StatusCode, string? MethodCode, DateTime? PaidAtUtc,
    string? CardLastFour, string? AuthorizationReference, ulong RowVersion);
public sealed record ManagerAppointment(ulong Id, ulong AppointmentNumber, ulong PatientAccountId, string PatientName, string? PatientPhone,
    ulong DoctorAccountId, string DoctorName, uint AppointmentTypeId, string Service, string CategoryCode, string StatusCode,
    string ModalityCode, DateTime StartsAtUtc, DateTime EndsAtUtc, decimal PriceAmount, decimal DiscountPercent,
    string PaymentLocation, string? PatientNotes, string? CancellationReason, ulong? RescheduledFromAppointmentId,
    ulong? RescheduledToAppointmentId, byte? Rating, string? ReviewComment, ManagerPaymentMetadata Payment,
    ManagerReportMetadata Report, int AttachmentCount, DateTime? ArrivedAtUtc, DateOnly? ArrivalBusinessDate,
    uint? ArrivalQueueNumber, IReadOnlyList<WebAppointmentRescheduleHistory> RescheduleHistory,
    bool CanRegisterArrival, bool CanCancelArrival, bool CanCancel, bool CanReschedule, bool CanConfirmPayment, ulong RowVersion);
public sealed record ManagerAgendaCounters(int Total, int Online, int InPerson, int Rescheduled, int Paid, int PendingPayment);
public sealed record ManagerAgendaSources(IReadOnlyList<ulong> Total, IReadOnlyList<ulong> Online,
    IReadOnlyList<ulong> InPerson, IReadOnlyList<ulong> Rescheduled, IReadOnlyList<ulong> Paid,
    IReadOnlyList<ulong> PendingPayment);
public sealed record ManagerAgendaData(ManagerAgendaCounters Counters, ManagerAgendaSources Sources, WebPage<ManagerAppointment> Page);
public sealed record ManagerPatientAddress(string? PostalCode, string? Street, string? Number, string? Complement, string? District, string? City, string? StateCode);
public sealed record ManagerPatient(ulong AccountId, string FullName, string? PreferredName, string? TaxId, string? Email,
    string? Phone, bool EmailVerified, bool PhoneVerified, DateOnly? BirthDate, ManagerPatientAddress? Address,
    string StatusCode, bool PortalAccessEnabled, bool IsPremium, string PremiumStatus, ulong? PremiumRequestId,
    int AppointmentCount, DateTime? LastAppointmentAtUtc, DateTime? NextAppointmentAtUtc, ulong RowVersion);
public sealed record ManagerPatientCounters(int Total, int Premium, int Active, int Blocked, int PremiumPending);
public sealed record ManagerPatientSources(IReadOnlyList<string> Total, IReadOnlyList<string> Premium,
    IReadOnlyList<string> Active, IReadOnlyList<string> Blocked, IReadOnlyList<string> PremiumPending);
public sealed record ManagerPatientsData(ManagerPatientCounters Counters, ManagerPatientSources Sources, WebPage<ManagerPatient> Page);
public sealed record ManagerPremiumRequest(ulong Id, ulong PatientAccountId, string PatientName, string PlanName,
    decimal DiscountPercent, string StatusCode, Guid? ProofDocumentId, string? ProofName, uint? ProofSizeBytes,
    string? ReviewNotes, string? RejectionReason, DateTime CreatedAtUtc, DateTime? ReviewedAtUtc, ulong RowVersion);
public sealed record ManagerPayment(ulong Id, ulong AppointmentId, string StatusCode, decimal Amount, string MethodCode,
    DateTime PaidAtUtc, string? CardLastFour, string? AuthorizationReference, ulong RowVersion);

public static class ManagerLabels
{
    public static string Status(string code) => PatientLabels.Status(code);
    public static string Category(string code) => PatientLabels.Category(code);
    public static string Modality(string code) => code == "online" ? "Online" : "Presencial";
    public static string Money(decimal value) => PatientLabels.Money(value);
    public static string Method(string? code) => code switch { "credit_card" => "Cartão de crédito", "debit_card" => "Cartão de débito", "pix" => "Pix", "cash" => "Dinheiro", "pagbank_online" => "PagBank online", _ => "Não informado" };
}
