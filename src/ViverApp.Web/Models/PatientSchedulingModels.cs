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
    bool SupportsOnline = true,
    string LicenseTypeCode = "CRM");

public sealed record WebAvailableSlot(
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string TimezoneName);

public sealed record BookingDaySelection(DateOnly Date, IReadOnlyList<WebAvailableSlot> Slots);

public sealed record WebAppointmentRescheduleHistory(
    uint SequenceNumber,
    DateTime PreviousStartsAtUtc,
    DateTime PreviousEndsAtUtc,
    DateTime NewStartsAtUtc,
    DateTime NewEndsAtUtc,
    string? Reason,
    DateTime OccurredAtUtc);

public sealed record WebAppointmentService(uint AppointmentTypeId, string Name, string CategoryCode,
    ushort DurationMinutes, decimal BasePriceAmount, bool RequiresPayment);

public sealed record WebAppointment(
    ulong Id,
    ulong AppointmentNumber,
    ulong ProfessionalAccountId,
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
    DateTime? ArrivedAtUtc,
    DateOnly? ArrivalBusinessDate,
    uint? ArrivalQueueNumber,
    IReadOnlyList<WebAppointmentRescheduleHistory> RescheduleHistory,
    bool RequiresPayment,
    ulong RowVersion)
{
    public IReadOnlyList<WebAppointmentService> Services { get; init; } = [];
    public decimal BasePriceAmount { get; init; }
    public decimal PremiumDiscountPercent { get; init; }
    public string? PointDiscountKindCode { get; init; }
    public decimal? PointDiscountValue { get; init; }
    public decimal PointDiscountAmount { get; init; }
}

public sealed record WebAppointmentCreateRequest(
    ulong ProfessionalAccountId,
    uint AppointmentTypeId,
    string ModalityCode,
    DateOnly LocalDate,
    TimeOnly LocalStartsAt,
    string? PatientNotes,
    IReadOnlyList<uint>? AdditionalAppointmentTypeIds = null);

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
