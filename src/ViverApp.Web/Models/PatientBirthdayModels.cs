namespace ViverApp.Web;

public sealed record PatientBirthday(ulong AccountId, string FullName, int Day, int TurningAge, bool IsToday);
public sealed record PatientBirthdaysData(DateOnly Today, int TotalCount, int TodayCount, int Page, int PageSize,
    IReadOnlyList<PatientBirthday> Items);
public sealed record BirthdayNotice(ulong Id, DateOnly OperationalDate, uint BirthdayCount, bool IsRead);
public sealed record BirthdayDailyCheck(BirthdayNotice Notice, bool IsNew);
