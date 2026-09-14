namespace ViverApp.Web;

public sealed record AdministratorHomeData(AdministratorCounters Counters, IReadOnlyList<AdministratorPendingProfessional> Pending, AdministratorHomeSources Sources, IReadOnlyList<ManagerAppointment> Today);
public sealed record AdministratorCounters(int ActiveUsers, int TodayAppointments, int ActivePremium, int PendingApprovals, int PendingPayments, int UnreadNotifications);
public sealed record AdministratorHomeSources(IReadOnlyList<string> ActiveUsers, IReadOnlyList<ulong> TodayAppointments,
    IReadOnlyList<string> ActivePremium, IReadOnlyList<string> PendingApprovals, IReadOnlyList<ulong> PendingPayments,
    IReadOnlyList<string> UnreadNotifications);
public sealed record AdministratorPendingProfessional(ulong AccountId, string FullName, string RoleCode, string? Contact, string? License, string? Specialty, ushort? YearsExperience, ulong RowVersion);
public sealed record AdministratorAnalyticsData(DateOnly From, DateOnly To, decimal Revenue, int Appointments, decimal AverageTicket, decimal? Satisfaction,
    decimal PreviousRevenue, int PreviousAppointments, IReadOnlyList<AdministratorMetric> RevenueByMonth, IReadOnlyList<AdministratorMetric> AppointmentsByStatus,
    IReadOnlyList<AdministratorMetric> PaymentsByMethod, IReadOnlyList<AdministratorMetric> DoctorPerformance,
    IReadOnlyList<AdministratorRevenueByUserType> RevenueByUserType,
    IReadOnlyList<AdministratorPaymentMethodEvolution> PaymentsByTypeEvolution,
    IReadOnlyList<AdministratorPaymentLocationTrend> PaymentsByLocationTrend,
    IReadOnlyList<AdministratorMetric> PaymentsByLocation,
    IReadOnlyList<AdministratorMetric> AppointmentsByService,
    IReadOnlyList<AdministratorMetric> AppointmentsByCategory);
public sealed record AdministratorMetric(string Label, decimal Value, int Count);
public sealed record AdministratorRevenueByUserType(string Label, decimal Regular, decimal Premium);
public sealed record AdministratorPaymentMethodEvolution(string Label, decimal Card, decimal Pix, decimal Cash, decimal BankSlip);
public sealed record AdministratorPaymentLocationTrend(string Label, int Online, int InClinic);

public enum AdministratorChartSeriesKind { Bar, Line, Area }
public enum AdministratorChartValueFormat { Number, Money, Percent, Rating }
public sealed record AdministratorChartSeries(string Name, string Color, IReadOnlyList<decimal> Values,
    AdministratorChartSeriesKind Kind = AdministratorChartSeriesKind.Bar,
    AdministratorChartValueFormat Format = AdministratorChartValueFormat.Number,
    bool SecondaryAxis = false);
public sealed record AdministratorSetting(string Key, string ValueJson, string? Description, DateTime UpdatedAtUtc, ulong RowVersion);
public sealed record AdministratorPremiumPlan(uint Id, string Name, decimal AppointmentDiscountPercent, ushort? ValidityDays, bool IsActive, ulong RowVersion);
public sealed record AdministratorUser(ulong Id, string FullName, string RoleCode, string StatusCode, string? Email, bool EmailVerified, string? PhoneE164, bool PhoneVerified, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc, ulong RowVersion);
public sealed record AdministratorClinic(string LegalName, string DisplayName, string? TaxId, string? Email, string? PhoneE164, string? PostalCode, string? Street, string? Number, string? Complement, string? District, string? City, string? StateCode, string TimezoneName, ulong RowVersion);
public sealed record AdministratorService(uint Id, string Name, string? Description, string CategoryCode, string ModalityCode, ushort DurationMinutes, decimal PriceAmount, bool IsActive, ushort DisplayOrder, ulong RowVersion);
public sealed record AdministratorWeeklyHour(ulong Id, byte DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsActive, ulong RowVersion);
public sealed record AdministratorHoliday(uint Id, DateOnly HolidayDate, string Name, TimeSpan? StartTime, TimeSpan? EndTime, ulong RowVersion, bool IsAnnual);
public sealed record AdministratorNotification(ulong Id, string TypeCode, string SeverityCode, string Title, string Message, string? EntityType, string? EntityId, bool IsRead, DateTime CreatedAtUtc, ulong RowVersion);
public sealed record AdministratorNotificationCounters(int PendingPayments, int Unread, int HighSeverity, int PendingApprovals);
public sealed record AdministratorNotificationSources(IReadOnlyList<ulong> PendingPayments, IReadOnlyList<string> Unread,
    IReadOnlyList<string> HighSeverity, IReadOnlyList<string> PendingApprovals);
public sealed record AdministratorNotifications(AdministratorNotificationCounters Counters, AdministratorNotificationSources Sources,
    IReadOnlyList<AdministratorNotification> Items);
public sealed record AdministratorDoctorAccess(ulong AccountId, string FullName, bool OnlineEnabled, ulong RowVersion);

public static class AdministratorLabels
{
    public static string Role(string value) => value switch { "administrator" => "Administrador", "manager" => "Gestor", "doctor" => "Médico", _ => "Paciente" };
    public static string Status(string value) => PatientLabels.Status(value);
    public static string Metric(string value) => value.Replace('_', ' ') switch
    {
        "card" => "Cartão",
        "credit card" => "Cartão de crédito",
        "debit card" => "Cartão de débito",
        "bank slip" => "Boleto",
        "cash" => "Dinheiro",
        "web" => "Online",
        "clinic" => "Presencial",
        "consultation" => "Consulta",
        "examination" => "Exame",
        "surgery" => "Cirurgia",
        "not informed" => "Não informado",
        var label when label.Length > 0 => char.ToUpperInvariant(label[0]) + label[1..],
        _ => "Não informado",
    };
}
