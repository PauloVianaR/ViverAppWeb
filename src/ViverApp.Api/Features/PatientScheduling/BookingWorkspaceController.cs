using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.ManagerExperience;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientScheduling;

public sealed record BookingWorkspaceSpecialty(uint Id, string Name);
public sealed record BookingWorkspaceOptions(uint? DefaultSpecialtyId, IReadOnlyList<BookingWorkspaceSpecialty> Specialties);
public sealed record BookingWorkspaceMetadata(string BillingFormCode, uint? SpecialtyId, ulong RowVersion, string? SpecialtyName = null,
    string StatusCode = "", DateTime StartsAtUtc = default, DateTime EndsAtUtc = default);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BookingWorkspaceUpdate(
    [param: Required, RegularExpression("^(private|insurance)$")] string BillingFormCode,
    [param: Range(1, int.MaxValue)] uint? SpecialtyId,
    [param: Range(1, long.MaxValue)] ulong RowVersion);

[ApiController, Route("api/v1/booking-workspace")]
[Authorize(Roles = "administrator,manager,doctor,psychologist")]
[ServiceFilter(typeof(AdministratorExceptionFilter)), ServiceFilter(typeof(AdministratorStepUpFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class BookingWorkspaceController(ViverAppDbContext database, ManagerExperienceService patients,
    IdentityAuditWriter audit, TimeProvider clock, PatientSchedulingService scheduling) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    private bool Professional => User.IsInRole("doctor") || User.IsInRole("psychologist");

    [HttpGet("options")]
    public async Task<BookingWorkspaceOptions> Options(CancellationToken ct)
    {
        var specialties = await database.Specialties.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name).Select(x => new BookingWorkspaceSpecialty(x.Id, x.Name)).ToArrayAsync(ct);
        var value = await database.ApplicationSettings.AsNoTracking()
            .Where(x => x.SettingKey == "appointments.default_specialty_id" && !x.IsSecret)
            .Select(x => x.ValueJson).SingleOrDefaultAsync(ct);
        return new(uint.TryParse(value, out var id) && specialties.Any(x => x.Id == id) ? id : null, specialties);
    }

    [HttpGet("professionals")]
    public Task<SchedulingPage<BookingProfessionalResponse>> Professionals(uint? specialtyId = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
        => scheduling.SearchProfessionalsAsync(page, pageSize, null, specialtyId, null, null, ct);

    [HttpGet("professionals/{id:long}/specialties")]
    public async Task<object> ProfessionalSpecialties(ulong id, CancellationToken ct)
    {
        if (Professional && id != Actor) throw new AdministratorRuleException(404, "Profissional não encontrado.");
        var primary = await database.ProfessionalSpecialties.AsNoTracking().Where(x => x.ProfessionalAccountId == id && x.Specialty.IsActive)
            .OrderByDescending(x => x.IsPrimary).Select(x => (uint?)x.SpecialtyId).FirstOrDefaultAsync(ct);
        return new { primarySpecialtyId = primary };
    }

    [HttpGet("patients")]
    public Task<ManagerPatientsResponse> Patients(string? search = null, int page = 1, int pageSize = 50,
        CancellationToken ct = default) => patients.PatientsAsync(search, null, null, page, pageSize, ct);

    [HttpGet("patients/{id:long}")]
    public Task<ManagerPatientResponse> Patient(ulong id, CancellationToken ct) => patients.PatientAsync(id, ct);

    [HttpPut("patients/{id:long}"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<ManagerPatientResponse> UpdatePatient(ulong id, ManagerPatientUpdateRequest request, CancellationToken ct)
        => patients.UpdatePatientAsync(Actor, id, request, ct);

    [HttpGet("appointments/{id:long}")]
    public async Task<BookingWorkspaceMetadata> Metadata(ulong id, CancellationToken ct)
    {
        return await database.Appointments.AsNoTracking()
            .Where(x => x.Id == id && (!Professional || x.ProfessionalAccountId == Actor))
            .Select(x => new BookingWorkspaceMetadata(x.BillingFormCode, x.SpecialtyId, x.RowVersion,
                x.Specialty == null ? null : x.Specialty.Name, x.StatusCode, x.StartsAtUtc, x.EndsAtUtc))
            .SingleOrDefaultAsync(ct)
            ?? throw new AdministratorRuleException(404, "Atendimento não encontrado.");
    }

    [HttpPut("appointments/{id:long}"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<BookingWorkspaceMetadata> Update(ulong id, BookingWorkspaceUpdate request, CancellationToken ct)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var item = await database.Appointments.FromSqlInterpolated($"SELECT * FROM appointments WHERE id = {id} FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(ct);
        if (item is null || (Professional && item.ProfessionalAccountId != Actor))
            throw new AdministratorRuleException(404, "Atendimento não encontrado.");
        if (item.RowVersion != request.RowVersion) throw new AdministratorRuleException(409, "O atendimento foi alterado. Atualize antes de salvar.");
        if (item.SpecialtyId != request.SpecialtyId)
            throw new AdministratorRuleException(409, "A especialidade deste atendimento é fixa. Se estiver incorreta, cancele o atendimento e registre outro.");
        if (request.BillingFormCode is not ("private" or "insurance"))
            throw new AdministratorRuleException(400, "Selecione Particular ou Convênio.");
        if (item.StatusCode is "canceled" or "no_show")
            throw new AdministratorRuleException(409, "A forma não pode ser alterada neste estado.");
        var now = clock.GetUtcNow().UtcDateTime;
        if (item.BillingFormCode != request.BillingFormCode)
        {
            if (item.StatusCode is not ("pending" or "confirmed") || await database.Payments.AnyAsync(x => x.AppointmentId == id, ct))
                throw new AdministratorRuleException(409, "A forma só pode ser alterada antes da chegada, sem cobranças registradas. Pagamentos já registrados devem permanecer preservados.");
            var catalogCharge = await database.AppointmentServiceItems.Where(x => x.AppointmentId == id).AnyAsync(x => x.RequiresPayment, ct);
            if (!await database.AppointmentServiceItems.AnyAsync(x => x.AppointmentId == id, ct))
                catalogCharge = await database.AppointmentTypes.Where(x => x.Id == item.AppointmentTypeId).Select(x => x.RequiresPayment).SingleAsync(ct);
            item.RequiresPayment = PatientSchedulingService.ApplyBillingForm(request.BillingFormCode, catalogCharge);
            if (!item.RequiresPayment) item.PaymentDeferred = false;
            if (!item.PaymentDeferred && item.ModalityCode == "online") item.PaymentLocationCode = "web";
            if (item.RequiresPayment)
            {
                item.DiscountPercent = await database.PremiumMemberships.AsNoTracking()
                    .Where(x => x.AccountId == item.PatientAccountId && x.StatusCode == "active" && x.StartsAtUtc <= now
                        && (x.EndsAtUtc == null || x.EndsAtUtc > now) && x.PremiumPlan.IsActive)
                    .Select(x => (decimal?)x.PremiumPlan.AppointmentDiscountPercent).MaxAsync(ct) ?? 0;
            }
            var premiumPrice = decimal.Round((item.BasePriceAmount ?? item.PriceAmount) * (1 - item.DiscountPercent / 100m), 2, MidpointRounding.AwayFromZero);
            if (item.PointDiscountKindCode == "percent")
                item.PointDiscountAmount = decimal.Round(premiumPrice * item.PointDiscountValue / 100m, 2, MidpointRounding.AwayFromZero);
            item.PriceAmount = item.RequiresPayment ? Math.Max(0, premiumPrice - item.PointDiscountAmount) : 0;
            var previous = item.StatusCode;
            item.StatusCode = item.RequiresPayment ? "pending" : "confirmed";
            if (previous != item.StatusCode) database.AppointmentStatusHistories.Add(new AppointmentStatusHistory
            {
                AppointmentId = id,
                ActorAccountId = Actor,
                FromStatusCode = previous,
                ToStatusCode = item.StatusCode,
                Reason = "Alteração da forma de atendimento",
                OccurredAtUtc = now,
                StartsAtUtc = item.StartsAtUtc,
                EndsAtUtc = item.EndsAtUtc,
            });
        }
        item.BillingFormCode = request.BillingFormCode;
        item.UpdatedAtUtc = now;
        item.RowVersion++;
        // RequiresPayment integra a chave alternativa usada pelos pagamentos.
        // O EF não pode mutá-la em uma entidade rastreada; o UPDATE atômico mantém
        // as constraints reais do MySQL e só ocorre sem pagamentos dependentes.
        var updated = await database.Database.ExecuteSqlInterpolatedAsync($@"UPDATE appointments
            SET billing_form_code = {item.BillingFormCode}, specialty_id = {item.SpecialtyId},
                requires_payment = {item.RequiresPayment}, payment_deferred = {item.PaymentDeferred},
                payment_location_code = {item.PaymentLocationCode}, discount_percent = {item.DiscountPercent},
                point_discount_amount = {item.PointDiscountAmount}, price_amount = {item.PriceAmount},
                status_code = {item.StatusCode}, updated_at_utc = {now}, row_version = {item.RowVersion}
            WHERE id = {id} AND row_version = {request.RowVersion}", ct);
        if (updated != 1) throw new AdministratorRuleException(409, "O atendimento foi alterado. Atualize antes de salvar.");
        await database.SaveChangesAsync(ct);
        await audit.WriteAsync("appointment.workspace.updated", Actor, "appointment", id.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, string> { ["billingForm"] = item.BillingFormCode }, ct);
        await transaction.CommitAsync(ct);
        return await Metadata(id, ct);
    }
}
