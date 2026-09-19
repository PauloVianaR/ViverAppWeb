namespace ViverApp.Api.Features.Calendar;

public sealed record CalendarProfessionalResponse(
    ulong AccountId,
    string FullName,
    string RoleCode,
    string LicenseLabel);

public sealed record CalendarItemResponse(
    ulong Id,
    ulong AppointmentNumber,
    string PatientName,
    ulong ProfessionalAccountId,
    string ProfessionalName,
    string StatusCode,
    string CategoryCode,
    string AppointmentTypeName,
    string ModalityCode,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc);

public sealed record CalendarDayAggregateResponse(DateOnly Date, int Count, IReadOnlyList<string> Statuses);

public sealed record CalendarResponse(
    string View,
    DateOnly Anchor,
    DateOnly RangeStart,
    DateOnly RangeEnd,
    string TimezoneName,
    IReadOnlyList<CalendarItemResponse> Items,
    IReadOnlyList<CalendarDayAggregateResponse> Days);
