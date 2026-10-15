namespace ViverApp.Web;

public sealed record StaffContact(ulong Id, string FullName, string Role, bool Online, int Unread);
public sealed record StaffMessageView(ulong Id, ulong SenderId, ulong RecipientId, string Body, DateTime CreatedAtUtc, bool IsRead);
public sealed record ManagerAlertView(ulong Id, ulong AppointmentId, ulong AppointmentNumber, string Type, DateTime CreatedAtUtc, bool IsRead, string PatientName = "");
public sealed record PaymentPendingNotice(ulong Id, DateOnly OperationalDate, uint PendingCount, bool IsRead);
public sealed record PaymentPendingDailyCheck(PaymentPendingNotice Notice, bool IsNew);
