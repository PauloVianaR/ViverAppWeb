using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;

namespace ViverApp.Api.Features.Calendar;

public sealed class CalendarService(ViverAppDbContext database)
{
    private static readonly string[] Views = ["day", "week", "month", "year"];

    public async Task<CalendarResponse> GetAsync(
        ulong actorId,
        string roleCode,
        string view,
        DateOnly anchor,
        ulong? professionalAccountId,
        CancellationToken cancellationToken)
    {
        if (!Views.Contains(view, StringComparer.Ordinal))
        {
            throw new CalendarRuleException(400, "A visualização da agenda é inválida.");
        }
        if (anchor.Year is < 1900 or > 9998)
        {
            throw new CalendarRuleException(400, "A data de referência da agenda é inválida.");
        }
        if (professionalAccountId.HasValue && roleCode is ViverAppRoles.Patient)
        {
            throw new CalendarRuleException(403, "Você não pode selecionar a agenda de outro profissional.");
        }
        if (professionalAccountId.HasValue && roleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist
            && professionalAccountId.Value != actorId)
        {
            throw new CalendarRuleException(403, "Você não pode acessar a agenda de outro profissional.");
        }

        var timezoneName = await database.Clinics.AsNoTracking()
            .Select(item => item.TimezoneName)
            .SingleOrDefaultAsync(cancellationToken) ?? "America/Sao_Paulo";
        TimeZoneInfo timezone;
        try { timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneName); }
        catch (TimeZoneNotFoundException) { throw new CalendarRuleException(503, "O fuso horário da clínica está indisponível."); }

        var (start, endExclusive) = Range(view, anchor);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(start.ToDateTime(TimeOnly.MinValue), timezone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endExclusive.ToDateTime(TimeOnly.MinValue), timezone);
        var query = database.Appointments.AsNoTracking()
            .Where(item => item.StartsAtUtc < endUtc
                && item.EndsAtUtc > startUtc
                && item.StatusCode != "completed"
                && item.StatusCode != "canceled"
                && item.StatusCode != "no_show"
                && item.InverseRescheduledFromAppointment == null);

        query = roleCode switch
        {
            ViverAppRoles.Patient => query.Where(item => item.PatientAccountId == actorId),
            ViverAppRoles.Doctor or ViverAppRoles.Psychologist => query.Where(item => item.ProfessionalAccountId == actorId),
            ViverAppRoles.Manager or ViverAppRoles.Administrator when professionalAccountId.HasValue =>
                query.Where(item => item.ProfessionalAccountId == professionalAccountId.Value),
            ViverAppRoles.Manager or ViverAppRoles.Administrator => query,
            _ => throw new CalendarRuleException(403, "Você não possui acesso a esta agenda."),
        };

        if (view == "year")
        {
            var annualRows = await query.OrderBy(item => item.StartsAtUtc)
                .Select(item => new { item.StartsAtUtc, item.StatusCode })
                .ToArrayAsync(cancellationToken);
            var days = annualRows.GroupBy(item => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.SpecifyKind(item.StartsAtUtc, DateTimeKind.Utc), timezone)))
                .Where(group => group.Key >= start && group.Key < endExclusive)
                .OrderBy(group => group.Key)
                .Select(group => new CalendarDayAggregateResponse(
                    group.Key,
                    group.Count(),
                    group.Select(item => item.StatusCode).Distinct(StringComparer.Ordinal).Order().ToArray()))
                .ToArray();
            return new(view, anchor, start, endExclusive.AddDays(-1), timezoneName, [], days);
        }

        var rows = await query.OrderBy(item => item.StartsAtUtc).ThenBy(item => item.AppointmentNumber)
            .Select(item => new
            {
                item.Id,
                item.AppointmentNumber,
                PatientName = item.PatientAccount.FullName,
                item.ProfessionalAccountId,
                ProfessionalName = item.ProfessionalAccount.Account.FullName,
                item.StatusCode,
                item.AppointmentType.CategoryCode,
                AppointmentTypeName = item.AppointmentType.Name,
                item.ModalityCode,
                item.StartsAtUtc,
                item.EndsAtUtc,
            })
            .ToArrayAsync(cancellationToken);

        var items = rows.Select(item => new CalendarItemResponse(
            item.Id, item.AppointmentNumber, item.PatientName, item.ProfessionalAccountId,
            item.ProfessionalName, item.StatusCode, item.CategoryCode, item.AppointmentTypeName,
            item.ModalityCode, DateTime.SpecifyKind(item.StartsAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(item.EndsAtUtc, DateTimeKind.Utc))).ToArray();
        return new(view, anchor, start, endExclusive.AddDays(-1), timezoneName, items, []);
    }

    public async Task<IReadOnlyList<CalendarProfessionalResponse>> ProfessionalsAsync(CancellationToken cancellationToken) =>
        await database.Accounts.AsNoTracking()
            .Where(item => (item.RoleCode == ViverAppRoles.Doctor || item.RoleCode == ViverAppRoles.Psychologist)
                && item.StatusCode == "active" && item.ProfessionalProfile != null)
            .OrderBy(item => item.FullName)
            .Select(item => new CalendarProfessionalResponse(
                item.Id,
                item.FullName,
                item.RoleCode,
                item.ProfessionalProfile!.LicenseTypeCode + " " + item.ProfessionalProfile.LicenseStateCode + " " + item.ProfessionalProfile.LicenseNumber))
            .ToArrayAsync(cancellationToken);

    private static (DateOnly Start, DateOnly EndExclusive) Range(string view, DateOnly anchor) => view switch
    {
        "day" => (anchor, anchor.AddDays(1)),
        "week" => (anchor.AddDays(-((7 + (int)anchor.DayOfWeek - (int)DayOfWeek.Monday) % 7)),
            anchor.AddDays(-((7 + (int)anchor.DayOfWeek - (int)DayOfWeek.Monday) % 7)).AddDays(7)),
        "month" => (new DateOnly(anchor.Year, anchor.Month, 1), new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(1)),
        _ => (new DateOnly(anchor.Year, 1, 1), new DateOnly(anchor.Year + 1, 1, 1)),
    };
}

public sealed class CalendarRuleException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
