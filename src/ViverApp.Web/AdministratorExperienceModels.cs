namespace ViverApp.Web;

public sealed record AdministratorHomeData(AdministratorCounters Counters, IReadOnlyList<AdministratorPendingProfessional> Pending, IReadOnlyList<ManagerAppointment> Today);
public sealed record AdministratorCounters(int ActiveUsers, int TodayAppointments, int ActivePremium, int PendingApprovals, int PendingPayments, int UnreadNotifications);
public sealed record AdministratorPendingProfessional(ulong AccountId, string FullName, string RoleCode, string? Contact, string? License, string? Specialty, ushort? YearsExperience, ulong RowVersion);
public sealed record AdministratorAnalyticsData(DateOnly From, DateOnly To, decimal Revenue, int Appointments, decimal AverageTicket, decimal? Satisfaction,
    decimal PreviousRevenue, int PreviousAppointments, IReadOnlyList<AdministratorMetric> RevenueByMonth, IReadOnlyList<AdministratorMetric> AppointmentsByStatus,
    IReadOnlyList<AdministratorMetric> PaymentsByMethod, IReadOnlyList<AdministratorMetric> DoctorPerformance);
public sealed record AdministratorMetric(string Label, decimal Value, int Count);
public sealed record AdministratorSetting(string Key, string ValueJson, string? Description, DateTime UpdatedAtUtc, ulong RowVersion);
public sealed record AdministratorPremiumPlan(uint Id, string Name, decimal AppointmentDiscountPercent, ushort? ValidityDays, bool IsActive, ulong RowVersion);
public sealed record AdministratorUser(ulong Id, string FullName, string RoleCode, string StatusCode, string? Email, bool EmailVerified, string? PhoneE164, bool PhoneVerified, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc, ulong RowVersion);
public sealed record AdministratorClinic(string LegalName, string DisplayName, string? TaxId, string? Email, string? PhoneE164, string? PostalCode, string? Street, string? Number, string? Complement, string? District, string? City, string? StateCode, string TimezoneName, ulong RowVersion);
public sealed record AdministratorService(uint Id, string Name, string? Description, string CategoryCode, string ModalityCode, ushort DurationMinutes, decimal PriceAmount, bool IsActive, ushort DisplayOrder, ulong RowVersion);
public sealed record AdministratorWeeklyHour(ulong Id, byte DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsActive, ulong RowVersion);
public sealed record AdministratorHoliday(uint Id, DateOnly HolidayDate, string Name, TimeSpan? StartTime, TimeSpan? EndTime, ulong RowVersion, bool IsAnnual);
public sealed record AdministratorNotification(ulong Id, string TypeCode, string SeverityCode, string Title, string Message, string? EntityType, string? EntityId, bool IsRead, DateTime CreatedAtUtc, ulong RowVersion);
public sealed record AdministratorNotificationCounters(int PendingPayments, int Unread, int HighSeverity, int PendingApprovals);
public sealed record AdministratorNotifications(AdministratorNotificationCounters Counters, IReadOnlyList<AdministratorNotification> Items);
public sealed record AdministratorDoctorAccess(ulong AccountId, string FullName, bool OnlineEnabled, ulong RowVersion);

public static class AdministratorLabels
{
    public static string Role(string value) => value switch { "administrator" => "Administrador", "manager" => "Gestor", "doctor" => "Médico", _ => "Paciente" };
    public static string Status(string value) => PatientLabels.Status(value);
    public static string Metric(string value) => value.Replace('_', ' ') switch { "credit card" => "Cartão de crédito", "debit card" => "Cartão de débito", "not informed" => "Não informado", var label => char.ToUpperInvariant(label[0]) + label[1..] };
}
