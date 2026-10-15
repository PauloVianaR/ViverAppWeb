using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientScheduling;

public sealed record PaymentPendingNoticeResponse(ulong Id, DateOnly OperationalDate, uint PendingCount, bool IsRead);
public sealed record PaymentPendingDailyCheckResponse(PaymentPendingNoticeResponse Notice, bool IsNew);

public sealed class PaymentPendingNoticeService(ViverAppDbContext database, TimeProvider clock)
{
    public async Task<PaymentPendingDailyCheckResponse> CheckAsync(ulong actor, CancellationToken ct)
    {
        if (!await database.Accounts.AnyAsync(a => a.Id == actor && a.StatusCode == "active"
            && (a.RoleCode == "manager" || a.RoleCode == "administrator"), ct))
            throw new AdministratorRuleException(403, "Este aviso é restrito à gestão da clínica.");
        var zoneName = await database.Clinics.Select(c => c.TimezoneName).SingleAsync(ct);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(zoneName)).DateTime);
        var day = date.ToDateTime(TimeOnly.MinValue);
        var existing = await database.PaymentPendingDailyNotices.AsNoTracking()
            .SingleOrDefaultAsync(n => n.AccountId == actor && n.OperationalDate == day, ct);
        if (existing is not null) return new(Map(existing), false);
        var count = await database.Appointments.Where(a => a.InverseRescheduledFromAppointment == null)
            .Where(AppointmentPaymentPolicy.Pending).CountAsync(ct);
        var notice = new PaymentPendingDailyNotice
        {
            AccountId = actor,
            OperationalDate = day,
            PendingCount = checked((uint)count),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        database.PaymentPendingDailyNotices.Add(notice);
        try { await database.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is MySql.Data.MySqlClient.MySqlException { Number: 1062 })
        {
            database.Entry(notice).State = EntityState.Detached;
            var winner = await database.PaymentPendingDailyNotices.AsNoTracking()
                .SingleAsync(n => n.AccountId == actor && n.OperationalDate == day, ct);
            return new(Map(winner), false);
        }
        return new(Map(notice), true);
    }

    public async Task<IReadOnlyList<PaymentPendingNoticeResponse>> ListAsync(ulong actor, CancellationToken ct) =>
        (await database.PaymentPendingDailyNotices.AsNoTracking().Where(n => n.AccountId == actor && n.PendingCount > 0)
            .OrderByDescending(n => n.OperationalDate).Take(15).ToArrayAsync(ct)).Select(Map).ToArray();

    public Task<int> ReadAsync(ulong actor, ulong id, CancellationToken ct) =>
        database.PaymentPendingDailyNotices.Where(n => n.AccountId == actor && n.Id == id && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, clock.GetUtcNow().UtcDateTime), ct);

    private static PaymentPendingNoticeResponse Map(PaymentPendingDailyNotice n) =>
        new(n.Id, DateOnly.FromDateTime(n.OperationalDate), n.PendingCount, n.ReadAtUtc != null);
}

[ApiController, Route("api/v1/management/payment-pending-notices")]
[Authorize(Roles = "manager,administrator")]
[ServiceFilter(typeof(AdministratorExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PaymentPendingNoticeController(PaymentPendingNoticeService service) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpPost("daily-check"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<PaymentPendingDailyCheckResponse> Check(CancellationToken ct) => service.CheckAsync(Actor, ct);
    [HttpGet]
    public Task<IReadOnlyList<PaymentPendingNoticeResponse>> List(CancellationToken ct) => service.ListAsync(Actor, ct);
    [HttpPost("{id:long}/read"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<IActionResult> Read(ulong id, CancellationToken ct) { await service.ReadAsync(Actor, id, ct); return NoContent(); }
}
