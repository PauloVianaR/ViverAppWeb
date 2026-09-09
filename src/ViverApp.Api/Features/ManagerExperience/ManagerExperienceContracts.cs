using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ViverApp.Api.Features.PatientScheduling;

namespace ViverApp.Api.Features.ManagerExperience;

public sealed record ManagerHomeResponse(ManagerProfileResponse Profile, ManagerHomeCounters Counters,
    ManagerHomeSources Sources, IReadOnlyList<ManagerAppointmentResponse> Today);
public sealed record ManagerHomeCounters(int Today, int Doctors, int Paid, int PendingPayment, int Online, int InPerson);
public sealed record ManagerHomeSources(IReadOnlyList<ulong> Today, IReadOnlyList<string> Doctors,
    IReadOnlyList<ulong> Paid, IReadOnlyList<ulong> PendingPayment, IReadOnlyList<ulong> Online,
    IReadOnlyList<ulong> InPerson);
public sealed record ManagerProfileResponse(ulong AccountId, string FullName, string? Email, string? Phone, string? TaxId,
    bool EmailVerified, bool PhoneVerified, bool EmailEnabled, bool SmsEnabled, ulong AccountRowVersion, ulong PreferenceRowVersion);
public sealed record ManagerDoctorOption(ulong AccountId, string FullName, string LicenseLabel);
public sealed record ManagerServiceOption(uint Id, string Name, string CategoryCode, string ModalityCode,
    ushort DurationMinutes, decimal BasePrice);
public sealed record ManagerReportMetadata(bool Exists, string? StatusCode, uint VersionCount, DateTime? PublishedAtUtc);
public sealed record ManagerPaymentMetadata(ulong? Id, string StatusCode, string? MethodCode, DateTime? PaidAtUtc,
    string? CardLastFour, string? AuthorizationReference, ulong RowVersion);
public sealed record ManagerAppointmentResponse(ulong Id, ulong AppointmentNumber, ulong PatientAccountId, string PatientName, string? PatientPhone,
    ulong DoctorAccountId, string DoctorName, uint AppointmentTypeId, string Service, string CategoryCode, string StatusCode,
    string ModalityCode, DateTime StartsAtUtc, DateTime EndsAtUtc, decimal PriceAmount, decimal DiscountPercent,
    string PaymentLocation, string? PatientNotes, string? CancellationReason, ulong? RescheduledFromAppointmentId,
    ulong? RescheduledToAppointmentId, byte? Rating, string? ReviewComment, ManagerPaymentMetadata Payment,
    ManagerReportMetadata Report, int AttachmentCount, DateTime? ArrivedAtUtc, DateOnly? ArrivalBusinessDate,
    uint? ArrivalQueueNumber, IReadOnlyList<AppointmentRescheduleHistoryResponse> RescheduleHistory,
    bool CanRegisterArrival, bool CanCancel, bool CanReschedule, bool CanConfirmPayment, ulong RowVersion);
public sealed record ManagerAgendaCounters(int Total, int Online, int InPerson, int Rescheduled, int Paid, int PendingPayment);
public sealed record ManagerAgendaSources(IReadOnlyList<ulong> Total, IReadOnlyList<ulong> Online,
    IReadOnlyList<ulong> InPerson, IReadOnlyList<ulong> Rescheduled, IReadOnlyList<ulong> Paid,
    IReadOnlyList<ulong> PendingPayment);
public sealed record ManagerAgendaResponse(ManagerAgendaCounters Counters, ManagerAgendaSources Sources,
    SchedulingPage<ManagerAppointmentResponse> Page);
public sealed record ManagerPatientAddressResponse(string PostalCode, string Street, string Number, string? Complement,
    string District, string City, string StateCode);
public sealed record ManagerPatientResponse(ulong AccountId, string FullName, string? PreferredName, string? TaxId, string? Email,
    string? Phone, bool EmailVerified, bool PhoneVerified, DateOnly? BirthDate, ManagerPatientAddressResponse? Address,
    string StatusCode, bool IsPremium, string PremiumStatus, ulong? PremiumRequestId,
    int AppointmentCount, DateTime? LastAppointmentAtUtc, DateTime? NextAppointmentAtUtc, ulong RowVersion);
public sealed record ManagerPatientCounters(int Total, int Premium, int Active, int Blocked, int PremiumPending);
public sealed record ManagerPatientSources(IReadOnlyList<string> Total, IReadOnlyList<string> Premium,
    IReadOnlyList<string> Active, IReadOnlyList<string> Blocked, IReadOnlyList<string> PremiumPending);
public sealed record ManagerPatientsResponse(ManagerPatientCounters Counters, ManagerPatientSources Sources,
    SchedulingPage<ManagerPatientResponse> Page);
public sealed record ManagerPremiumRequestResponse(ulong Id, ulong PatientAccountId, string PatientName, string PlanName,
    decimal DiscountPercent, string StatusCode, Guid? ProofDocumentId, string? ProofName, uint? ProofSizeBytes,
    string? ReviewNotes, string? RejectionReason, DateTime CreatedAtUtc, DateTime? ReviewedAtUtc, ulong RowVersion);
public sealed record ManagerPaymentResponse(ulong Id, ulong AppointmentId, string StatusCode, decimal Amount,
    string MethodCode, DateTime PaidAtUtc, string? CardLastFour, string? AuthorizationReference, ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagerProfileUpdateRequest([param: Required, StringLength(200, MinimumLength = 3)] string FullName,
    bool EmailEnabled, bool SmsEnabled, [param: Range(1, long.MaxValue)] ulong AccountRowVersion,
    [param: Range(1, long.MaxValue)] ulong PreferenceRowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagerPatientCreateRequest([param: Required, StringLength(200, MinimumLength = 3)] string FullName,
    [param: EmailAddress, StringLength(254)] string? Email, [param: StringLength(20)] string? PhoneE164, DateOnly? BirthDate) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(FullName) || FullName.Trim().Length is < 3 or > 200)
            yield return new ValidationResult("O nome deve ter entre 3 e 200 caracteres.", [nameof(FullName)]);
        if (!string.IsNullOrWhiteSpace(Email) && !new EmailAddressAttribute().IsValid(Email))
            yield return new ValidationResult("O e-mail é inválido.", [nameof(Email)]);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagerPatientUpdateRequest([param: Required, StringLength(200, MinimumLength = 3)] string FullName,
    [param: StringLength(120)] string? PreferredName,
    [param: RegularExpression("^[0-9]{11}$")] string? TaxId,
    [param: EmailAddress, StringLength(254)] string? Email,
    [param: StringLength(20)] string? PhoneE164,
    DateOnly? BirthDate, ManagerPatientAddressRequest? Address,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagerPatientAddressRequest(
    [param: Required, RegularExpression("^[0-9]{8}$")] string PostalCode,
    [param: Required, StringLength(200, MinimumLength = 2)] string Street,
    [param: Required, StringLength(20, MinimumLength = 1)] string Number,
    [param: StringLength(100)] string? Complement,
    [param: Required, StringLength(100, MinimumLength = 2)] string District,
    [param: Required, StringLength(100, MinimumLength = 2)] string City,
    [param: Required, RegularExpression("^[A-Za-z]{2}$")] string StateCode);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagerAppointmentCreateRequest([param: Range(1, long.MaxValue)] ulong PatientAccountId,
    [param: Range(1, long.MaxValue)] ulong DoctorAccountId, [param: Range(1, int.MaxValue)] uint AppointmentTypeId,
    [param: Required, RegularExpression("^(in_person|online)$")] string ModalityCode, DateOnly LocalDate,
    TimeOnly LocalStartsAt, [param: StringLength(1000)] string? PatientNotes);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagerPremiumDecisionRequest(bool Approve, [param: StringLength(1000)] string? Notes,
    [param: StringLength(1000)] string? RejectionReason, [param: Range(1, long.MaxValue)] ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ManagerPaymentConfirmRequest([param: Required, RegularExpression("^(credit_card|debit_card|pix|cash)$")] string MethodCode,
    DateTime PaidAtUtc, [param: RegularExpression("^[0-9]{4}$")] string? CardLastFour,
    [param: StringLength(100), RegularExpression("^[A-Za-z0-9][A-Za-z0-9 ._/-]{0,99}$")]
    string? AuthorizationReference, [param: Range(1, long.MaxValue)] ulong AppointmentRowVersion) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (MethodCode is not ("credit_card" or "debit_card" or "pix" or "cash"))
            yield return new ValidationResult("A forma de pagamento é inválida.", [nameof(MethodCode)]);
        if (CardLastFour is not null && (CardLastFour.Length != 4 || CardLastFour.Any(x => !char.IsAsciiDigit(x))))
            yield return new ValidationResult("Informe exatamente os quatro últimos dígitos.", [nameof(CardLastFour)]);
        if (AppointmentRowVersion == 0)
            yield return new ValidationResult("A versão do atendimento é obrigatória.", [nameof(AppointmentRowVersion)]);
    }
}
