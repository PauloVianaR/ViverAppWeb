namespace ViverApp.Web;

public sealed record DoctorHomeData(ProfessionalProfileData Profile, DoctorHomeCounters Counters, DoctorHomeSources Sources, IReadOnlyList<DoctorAppointment> Today);
public sealed record DoctorCapabilities(bool PatientSchedulingEnabled);
public sealed record DoctorHomeCounters(int Today, int Week, int Online, int InPerson);
public sealed record DoctorHomeSources(IReadOnlyList<ulong> Today, IReadOnlyList<ulong> Week, IReadOnlyList<ulong> Online, IReadOnlyList<ulong> InPerson);
public sealed record ProfessionalSpecialty(uint Id, string Name, bool IsPrimary);
public sealed record ProfessionalProfileData(ulong AccountId, string FullName, string? Email, string? Phone, string? TaxId,
    string ProfessionalTitle, string LicenseStateCode, string LicenseNumber, string? Biography, ushort YearsExperience,
    ushort DefaultAppointmentDurationMinutes, IReadOnlyList<ProfessionalSpecialty> Specialties, bool EmailEnabled, bool SmsEnabled,
    bool OnlineEnabled, ushort MaxOnlineDaily, ushort MaxInPersonDaily, double? AverageRating, int ReviewCount,
    ulong AccountRowVersion, ulong ProfileRowVersion, ulong PreferenceRowVersion, string LicenseTypeCode = "CRM");
public sealed record ProfessionalService(uint Id, string Name, string? Description, string CategoryCode, string ModalityCode,
    ushort DurationMinutes, decimal PriceAmount, bool RequiresPayment, bool IsActive, bool Offered, ulong RowVersion);
public sealed record DoctorAppointment(ulong Id, ulong AppointmentNumber, ulong PatientAccountId, string PatientName, int? PatientAge, uint AppointmentTypeId, string Service,
    string CategoryCode, string StatusCode, string ModalityCode, DateTime StartsAtUtc, DateTime EndsAtUtc, decimal PriceAmount, decimal BasePriceAmount,
    decimal DiscountPercent, bool RequiresPayment, string PaymentStatus, string PaymentLocation, string? PatientNotes, string? CancellationReason,
    ulong? RescheduledFromAppointmentId, ulong? RescheduledToAppointmentId, byte? Rating, string? ReviewComment,
    DateTime? ArrivedAtUtc, uint? ArrivalQueueNumber, IReadOnlyList<WebAppointmentRescheduleHistory> RescheduleHistory,
    bool CanJoinOnline, bool CanCancel, bool CanReschedule,
    bool CanStart, bool CanUndoStart, bool CanComplete, ulong RowVersion)
{
    public IReadOnlyList<WebAppointmentService> Services { get; init; } = [];
    public string? PointDiscountKindCode { get; init; }
    public decimal? PointDiscountValue { get; init; }
    public decimal PointDiscountAmount { get; init; }
}
public sealed record ProfessionalNotification(ulong Id, ulong AppointmentId, ulong AppointmentNumber, uint? QueueNumber,
    DateTime? StartsAtUtc, bool IsRead, DateTime CreatedAtUtc, ulong RowVersion);
public sealed record DoctorRealtimeNotification(ulong Id, ulong AppointmentId, ulong AppointmentNumber, uint? QueueNumber,
    DateTime? StartsAtUtc, bool IsRead, DateTime CreatedAtUtc, ulong RowVersion, bool PopupEnabled,
    bool SoundEnabled, int SoundVolume, string SoundKey);
public sealed record ProfessionalNotificationsData(int UnreadCount, bool PopupEnabled, bool SoundEnabled, int SoundVolume,
    string SoundKey, bool MarkReadOnOpen, IReadOnlyList<ProfessionalNotification> Items);
public sealed record DoctorAgendaSources(IReadOnlyList<ulong> Total, IReadOnlyList<ulong> Online,
    IReadOnlyList<ulong> InPerson, IReadOnlyList<ulong> Rescheduled);
public sealed record DoctorAgendaData(DoctorAgendaCounters Counters, DoctorAgendaSources Sources, WebPage<DoctorAppointment> Page);
public sealed record DoctorAgendaCounters(int Total, int Online, int InPerson, int Rescheduled);
public sealed record DoctorPatient(ulong AccountId, string FullName, string? PreferredName, string? Email, string? Phone,
    DateOnly? BirthDate, string StatusCode, bool IsPremium, decimal PremiumDiscountPercent, int AppointmentCount, DateTime? LastAppointmentAtUtc,
    DateTime? NextAppointmentAtUtc, ulong RowVersion);
public sealed record DoctorPatientCounters(int Total, int Premium, int Active, int Blocked);
public sealed record DoctorPatientSources(IReadOnlyList<string> Total, IReadOnlyList<string> Premium,
    IReadOnlyList<string> Active, IReadOnlyList<string> Blocked);
public sealed record DoctorPatientsData(DoctorPatientCounters Counters, DoctorPatientSources Sources, WebPage<DoctorPatient> Page);
public sealed record DoctorReportVersion(uint VersionNumber, string ClinicalSummary, string? Recommendations,
    string? ChangeReason, DateTime CreatedAtUtc, ulong AuthorProfessionalAccountId);
public sealed record DoctorDocument(ulong Id, string Name, string ContentType, ulong SizeBytes, DateTime CreatedAtUtc, ulong RowVersion);
public sealed record DoctorAppointmentDetail(DoctorAppointment Appointment, IReadOnlyList<DoctorReportVersion> ReportVersions,
    IReadOnlyList<DoctorDocument> Documents);
public sealed record ProfessionalWeeklyHour(ulong Id, byte DayOfWeek, TimeOnly StartsAt, TimeOnly EndsAt,
    DateOnly? ValidFrom, DateOnly? ValidUntil, bool IsActive, ulong RowVersion, string ModalityCode);
public sealed record ProfessionalAvailabilityException(ulong Id, DateOnly Date, string ModalityCode, bool IsAvailable,
    TimeOnly? StartsAt, TimeOnly? EndsAt, ulong RowVersion);
public sealed record DoctorAvailability(bool OnlineEnabled, ushort MaxOnlineDaily, ushort MaxInPersonDaily,
    ulong PreferenceRowVersion, IReadOnlyList<ProfessionalWeeklyHour> WeeklyHours, IReadOnlyList<ProfessionalAvailabilityException> Exceptions);

public static class DoctorLabels
{
    public static string Status(string code) => PatientLabels.Status(code);
    public static string Category(string code) => PatientLabels.Category(code);
    public static string Modality(string code) => code switch { "online" => "Online", "both" => "Online e presencial", _ => "Presencial" };
    public static string Money(decimal value) => PatientLabels.Money(value);
}
