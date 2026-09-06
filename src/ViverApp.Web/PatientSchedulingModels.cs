namespace ViverApp.Web;

public sealed record WebPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record WebSpecialty(uint Id, string Name, bool IsActive, ulong RowVersion);

public sealed record WebAppointmentType(
    uint Id,
    string Name,
    string? Description,
    string ModalityCode,
    ushort DurationMinutes,
    decimal PriceAmount,
    bool IsActive,
    ushort DisplayOrder,
    ulong RowVersion);

public sealed record WebBookingSpecialty(uint Id, string Name, bool IsPrimary);

public sealed record WebBookingProfessional(
    ulong AccountId,
    string FullName,
    string? Biography,
    string LicenseStateCode,
    string LicenseNumber,
    ushort DefaultAppointmentDurationMinutes,
    IReadOnlyList<WebBookingSpecialty> Specialties,
    ushort YearsExperience = 0,
    double? AverageRating = null,
    int ReviewCount = 0,
    bool SupportsOnline = true);

public sealed record WebAvailableSlot(
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string TimezoneName);

public sealed record WebAppointment(
    ulong Id,
    ulong DoctorAccountId,
    string DoctorName,
    uint AppointmentTypeId,
    string AppointmentTypeName,
    string StatusCode,
    string ModalityCode,
    DateOnly LocalDate,
    TimeOnly LocalStartsAt,
    TimeOnly LocalEndsAt,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string TimezoneName,
    decimal PriceAmount,
    string CurrencyCode,
    string? PatientNotes,
    string? CancellationReason,
    ulong? RescheduledFromAppointmentId,
    ulong? RescheduledToAppointmentId,
    ulong RowVersion);

public sealed record WebAppointmentCreateRequest(
    ulong DoctorAccountId,
    uint AppointmentTypeId,
    string ModalityCode,
    DateOnly LocalDate,
    TimeOnly LocalStartsAt,
    string? PatientNotes);

public sealed record WebAppointmentCancelRequest(string Reason, ulong RowVersion);

public sealed record WebAppointmentRescheduleRequest(
    DateOnly LocalDate,
    TimeOnly LocalStartsAt,
    string? Reason,
    ulong RowVersion);

public sealed record WebPayment(
    ulong Id,
    ulong AppointmentId,
    string StatusCode,
    decimal Amount,
    string CurrencyCode,
    string? CheckoutUrl,
    DateTime? CheckoutExpiresAtUtc,
    string? ProviderStatusCode,
    DateTime UpdatedAtUtc,
    DateTime? PaidAtUtc,
    DateTime? RefundedAtUtc,
    ulong RowVersion);
