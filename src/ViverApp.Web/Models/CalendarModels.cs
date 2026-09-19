namespace ViverApp.Web;

public sealed record WebCalendarProfessional(ulong AccountId, string FullName, string RoleCode, string LicenseLabel);
public sealed record WebCalendarItem(ulong Id, ulong AppointmentNumber, string PatientName, ulong ProfessionalAccountId,
    string ProfessionalName, string StatusCode, string CategoryCode, string AppointmentTypeName, string ModalityCode,
    DateTime StartsAtUtc, DateTime EndsAtUtc);
public sealed record WebCalendarDay(DateOnly Date, int Count, IReadOnlyList<string> Statuses);
public sealed record WebCalendar(string View, DateOnly Anchor, DateOnly RangeStart, DateOnly RangeEnd,
    string TimezoneName, IReadOnlyList<WebCalendarItem> Items, IReadOnlyList<WebCalendarDay> Days);
public sealed record WebCalendarViewPreference(string Mode, ulong RowVersion);
