using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ViverApp.Api.Features.ManagerExperience;

namespace ViverApp.Api.Features.AdministratorExperience;

public sealed record AdministratorHomeResponse(AdministratorCounters Counters, IReadOnlyList<AdministratorPendingProfessional> Pending,
    AdministratorHomeSources Sources, IReadOnlyList<ManagerAppointmentResponse> Today);
public sealed record AdministratorCounters(int ActiveUsers, int TodayAppointments, int ActivePremium, int PendingApprovals,
    int PendingPayments, int UnreadNotifications);
public sealed record AdministratorHomeSources(IReadOnlyList<string> ActiveUsers, IReadOnlyList<ulong> TodayAppointments,
    IReadOnlyList<string> ActivePremium, IReadOnlyList<string> PendingApprovals,
    IReadOnlyList<ulong> PendingPayments, IReadOnlyList<string> UnreadNotifications);
public sealed record AdministratorPendingProfessional(ulong AccountId, string FullName, string RoleCode, string? Contact,
    string? License, string? Specialty, ushort? YearsExperience, ulong RowVersion);
public sealed record AdministratorAnalyticsResponse(DateOnly From, DateOnly To, decimal Revenue, int Appointments,
    decimal AverageTicket, decimal? Satisfaction, decimal PreviousRevenue, int PreviousAppointments,
    IReadOnlyList<AdministratorMetricPoint> RevenueByMonth, IReadOnlyList<AdministratorMetricPoint> AppointmentsByStatus,
    IReadOnlyList<AdministratorMetricPoint> PaymentsByMethod, IReadOnlyList<AdministratorMetricPoint> DoctorPerformance,
    IReadOnlyList<AdministratorRevenueByUserTypePoint> RevenueByUserType,
    IReadOnlyList<AdministratorPaymentMethodEvolutionPoint> PaymentsByTypeEvolution,
    IReadOnlyList<AdministratorPaymentLocationTrendPoint> PaymentsByLocationTrend,
    IReadOnlyList<AdministratorMetricPoint> PaymentsByLocation,
    IReadOnlyList<AdministratorMetricPoint> AppointmentsByService,
    IReadOnlyList<AdministratorMetricPoint> AppointmentsByCategory);
public sealed record AdministratorMetricPoint(string Label, decimal Value, int Count);
public sealed record AdministratorRevenueByUserTypePoint(string Label, decimal Regular, decimal Premium);
public sealed record AdministratorPaymentMethodEvolutionPoint(string Label, decimal Card, decimal Pix, decimal Cash, decimal BankSlip);
public sealed record AdministratorPaymentLocationTrendPoint(string Label, int Online, int InClinic);
public sealed record AdministratorSettingResponse(string Key, string ValueJson, string? Description, DateTime UpdatedAtUtc, ulong RowVersion);
public sealed record AdministratorPremiumPlanResponse(uint Id, string Name, decimal AppointmentDiscountPercent, ushort? ValidityDays, bool IsActive, ulong RowVersion);
public sealed record AdministratorNotificationResponse(ulong Id, string TypeCode, string SeverityCode, string Title, string Message,
    string? EntityType, string? EntityId, bool IsRead, DateTime CreatedAtUtc, ulong RowVersion);
public sealed record AdministratorNotificationCounters(int PendingPayments, int Unread, int HighSeverity, int PendingApprovals);
public sealed record AdministratorNotificationSources(IReadOnlyList<ulong> PendingPayments, IReadOnlyList<string> Unread,
    IReadOnlyList<string> HighSeverity, IReadOnlyList<string> PendingApprovals);
public sealed record AdministratorNotificationsResponse(AdministratorNotificationCounters Counters,
    AdministratorNotificationSources Sources, IReadOnlyList<AdministratorNotificationResponse> Items);
public sealed record AdministratorDoctorAccessResponse(ulong AccountId, string FullName, bool OnlineEnabled, ulong RowVersion);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorSettingUpdateRequest([param: Required, StringLength(2000)] string ValueJson,
    [param: Range(1, long.MaxValue)] ulong RowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorPremiumPlanUpdateRequest([param: Range(typeof(decimal), "0", "100")] decimal AppointmentDiscountPercent,
    [param: Range(1, 3650)] ushort? ValidityDays, bool IsActive, [param: Range(1, long.MaxValue)] ulong RowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorAccountStatusRequest([param: Required, RegularExpression("^(blocked|reactivated)$")] string DecisionCode,
    [param: StringLength(500)] string? Reason, [param: Range(1, long.MaxValue)] ulong RowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorDoctorOnlineRequest(bool Enabled, [param: Range(1, long.MaxValue)] ulong RowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorNotificationUpdateRequest([param: Range(1, long.MaxValue)] ulong RowVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdministratorPremiumCancelRequest([param: Required, StringLength(500, MinimumLength = 5)] string Reason,
    [param: Range(1, long.MaxValue)] ulong RowVersion);
