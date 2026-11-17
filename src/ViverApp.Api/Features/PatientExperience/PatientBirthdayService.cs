using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.ManagerExperience;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientExperience;

public sealed record PatientBirthdayResponse(ulong AccountId, string FullName, int Day, int TurningAge, bool IsToday);
public sealed record PatientBirthdaysResponse(DateOnly Today, int TotalCount, int TodayCount, int Page, int PageSize,
    IReadOnlyList<PatientBirthdayResponse> Items);
public sealed record BirthdayNoticeResponse(ulong Id, DateOnly OperationalDate, uint BirthdayCount, bool IsRead);
public sealed record BirthdayDailyCheckResponse(BirthdayNoticeResponse Notice, bool IsNew);

public sealed class PatientBirthdayService(ViverAppDbContext database, TimeProvider clock)
{
    public const string AccessPolicy = "PatientBirthdays";
    private static readonly string[] Roles = ["manager", "doctor", "psychologist", "administrator"];

    private async Task RequireActorAsync(ulong actor, CancellationToken ct)
    {
        if (!await database.Accounts.AsNoTracking().AnyAsync(a => a.Id == actor && a.StatusCode == "active" && Roles.Contains(a.RoleCode), ct))
            throw new PatientExperienceException(403, "Sua conta não pode consultar os aniversariantes da clínica.");
    }

    private async Task<DateOnly> TodayAsync(CancellationToken ct)
    {
        var name = await database.Clinics.AsNoTracking().Select(c => c.TimezoneName).SingleOrDefaultAsync(ct) ?? "America/Sao_Paulo";
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(name)).DateTime);
    }

    private IQueryable<Account> MonthQuery(DateOnly date) => database.Accounts.AsNoTracking()
        .Where(a => a.RoleCode == "patient" && (a.BirthDate ?? a.PatientProfile!.BirthDate).HasValue
            && (a.BirthDate ?? a.PatientProfile!.BirthDate)!.Value <= date.ToDateTime(TimeOnly.MinValue)
            && (a.BirthDate ?? a.PatientProfile!.BirthDate)!.Value.Month == date.Month);

    public async Task<PatientBirthdaysResponse> MonthAsync(ulong actor, int page, int pageSize, CancellationToken ct)
    {
        await RequireActorAsync(actor, ct);
        PatientExperienceService.ValidatePage(page, pageSize);
        var today = await TodayAsync(ct);
        var query = MonthQuery(today);
        var total = await query.CountAsync(ct);
        var daily = await query.CountAsync(a => (a.BirthDate ?? a.PatientProfile!.BirthDate)!.Value.Day == today.Day, ct);
        var rows = await query.OrderBy(a => (a.BirthDate ?? a.PatientProfile!.BirthDate)!.Value.Day)
            .ThenBy(a => a.FullName).ThenBy(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new { a.Id, a.FullName, Birth = a.BirthDate ?? a.PatientProfile!.BirthDate }).ToArrayAsync(ct);
        var items = rows.Select(a => new PatientBirthdayResponse(a.Id, a.FullName, a.Birth!.Value.Day,
            today.Year - a.Birth.Value.Year, a.Birth.Value.Day == today.Day)).ToArray();
        return new(today, total, daily, page, pageSize, items);
    }

    public async Task<BirthdayDailyCheckResponse> CheckAsync(ulong actor, CancellationToken ct)
    {
        await RequireActorAsync(actor, ct);
        var today = await TodayAsync(ct);
        var day = today.ToDateTime(TimeOnly.MinValue);
        var count = checked((uint)await MonthQuery(today)
            .CountAsync(a => (a.BirthDate ?? a.PatientProfile!.BirthDate)!.Value.Day == today.Day, ct));
        var notice = await database.PatientBirthdayDailyNotices.AsNoTracking()
            .SingleOrDefaultAsync(n => n.AccountId == actor && n.OperationalDate == day, ct);
        if (notice is not null)
        {
            var changed = await database.PatientBirthdayDailyNotices
                .Where(n => n.Id == notice.Id && n.BirthdayCount != count)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.BirthdayCount, count).SetProperty(n => n.ReadAtUtc, (DateTime?)null), ct);
            var notify = changed > 0 && notice.BirthdayCount == 0 && count > 0;
            notice = await database.PatientBirthdayDailyNotices.AsNoTracking().SingleAsync(n => n.Id == notice.Id, ct);
            return new(Map(notice), notify);
        }
        notice = new PatientBirthdayDailyNotice { AccountId = actor, OperationalDate = day, BirthdayCount = count, CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
        database.PatientBirthdayDailyNotices.Add(notice);
        try { await database.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is MySql.Data.MySqlClient.MySqlException { Number: 1062 })
        {
            database.Entry(notice).State = EntityState.Detached;
            var winner = await database.PatientBirthdayDailyNotices.AsNoTracking().SingleAsync(n => n.AccountId == actor && n.OperationalDate == day, ct);
            return new(Map(winner), false);
        }
        return new(Map(notice), true);
    }

    public async Task ReadAsync(ulong actor, ulong id, CancellationToken ct)
    {
        await RequireActorAsync(actor, ct);
        if (!await database.PatientBirthdayDailyNotices.AnyAsync(n => n.Id == id && n.AccountId == actor, ct))
            throw new PatientExperienceException(404, "Aviso não encontrado.");
        await database.PatientBirthdayDailyNotices.Where(n => n.Id == id && n.AccountId == actor && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, clock.GetUtcNow().UtcDateTime), ct);
    }

    private static BirthdayNoticeResponse Map(PatientBirthdayDailyNotice n) =>
        new(n.Id, DateOnly.FromDateTime(n.OperationalDate), n.BirthdayCount, n.ReadAtUtc.HasValue);
}

[ApiController, Route("api/v1/staff/patient-birthdays")]
[Authorize(Policy = PatientBirthdayService.AccessPolicy)]
[ServiceFilter(typeof(ManagerExperienceExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PatientBirthdayController(PatientBirthdayService service) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    [HttpGet]
    public Task<PatientBirthdaysResponse> Month(CancellationToken ct, int page = 1, int pageSize = 24) => service.MonthAsync(Actor, page, pageSize, ct);
    [HttpPost("daily-check"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<BirthdayDailyCheckResponse> Check(CancellationToken ct) => service.CheckAsync(Actor, ct);
    [HttpPost("notices/{id:long}/read"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<IActionResult> Read(ulong id, CancellationToken ct) { await service.ReadAsync(Actor, id, ct); return NoContent(); }
}
