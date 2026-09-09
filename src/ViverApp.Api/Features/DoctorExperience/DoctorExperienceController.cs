using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.ArrivalExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.DoctorExperience;

public sealed class DoctorExperienceExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var result = context.Exception switch
        {
            DoctorRuleException e => (e.StatusCode, e.Message),
            ArrivalRuleException e => (e.StatusCode, e.Message),
            SchedulingRuleException e => (e.StatusCode, e.Message),
            ClinicalRuleException e => (e.StatusCode, e.Message),
            PatientExperienceException e => (e.StatusCode, e.Message),
            DbUpdateConcurrencyException => (409, "Os dados foram alterados por outra sessão. Atualize a página."),
            _ => default,
        };
        if (result == default) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = result.Item1, Title = result.Item2 }) { StatusCode = result.Item1 };
        context.ExceptionHandled = true;
    }
}

[ApiController, Route("api/v1/doctor"), Authorize(Policy = ViverAppPolicies.Doctor)]
[ServiceFilter(typeof(DoctorExperienceExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DoctorExperienceController(DoctorExperienceService service, PatientSchedulingService scheduling,
    ClinicalOperationsService clinical, PrivateDocumentStore documents, ViverAppDbContext database,
    IClinicalOperationsAuditWriter audit, ArrivalExperienceService arrivals) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    [HttpGet("home")]
    public Task<DoctorHomeResponse> Home(CancellationToken ct) => service.HomeAsync(Actor, ct);
    [HttpGet("profile")]
    public Task<DoctorProfileResponse> Profile(CancellationToken ct) => service.ProfileAsync(Actor, ct);
    [HttpPut("profile"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<DoctorProfileResponse> Profile(DoctorProfileUpdateRequest request, CancellationToken ct) => service.UpdateProfileAsync(Actor, request, ct);
    [HttpGet("services")]
    public Task<IReadOnlyList<DoctorServiceResponse>> Services(CancellationToken ct) => service.ServicesAsync(Actor, ct);
    [HttpPut("services"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<IReadOnlyList<DoctorServiceResponse>> Services(DoctorServicesUpdateRequest request, CancellationToken ct) => service.UpdateServicesAsync(Actor, request, ct);

    [HttpGet("agenda")]
    public Task<DoctorAgendaResponse> Agenda(DateOnly from, DateOnly to, string? status = null, string? modality = null,
        string? category = null, string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        service.AgendaAsync(Actor, from, to, status, modality, category, search, page, pageSize, ct);
    [HttpGet("appointments/{id:long}")]
    public Task<DoctorAppointmentDetailResponse> Appointment(ulong id, CancellationToken ct) => service.AppointmentAsync(Actor, id, ct);
    [HttpGet("booking/slots"), EnableRateLimiting(SecurityPolicyNames.SlotRateLimit)]
    public Task<IReadOnlyList<AvailableSlotResponse>> Slots(ulong patientAccountId, uint appointmentTypeId, string modality,
        DateOnly from, int days = 14, CancellationToken ct = default) => scheduling.GetAvailableSlotsAsync(patientAccountId, Actor, appointmentTypeId, modality, from, days, ct);
    [HttpPost("appointments"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<ActionResult<AppointmentResponse>> Create([FromHeader(Name = "Idempotency-Key")] string key,
        DoctorAppointmentCreateRequest request, CancellationToken ct)
    {
        var result = await scheduling.CreateForDoctorAsync(Actor, key, request, ct);
        if (result.Replayed) Response.Headers["Idempotent-Replayed"] = "true";
        return result.Replayed ? Ok(result.Response) : CreatedAtAction(nameof(Appointment), new { id = result.Response.Id }, result.Response);
    }
    [HttpPost("appointments/{id:long}/cancel"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<AppointmentResponse> Cancel(ulong id, AppointmentCancelRequest request, CancellationToken ct) => scheduling.CancelForDoctorAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/reschedule"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<AppointmentResponse> Reschedule(ulong id, [FromHeader(Name = "Idempotency-Key")] string key,
        AppointmentRescheduleRequest request, CancellationToken ct)
    {
        var result = await scheduling.RescheduleForDoctorAsync(Actor, id, key, request, ct);
        if (result.Replayed) Response.Headers["Idempotent-Replayed"] = "true";
        return result.Response;
    }
    [HttpPut("appointments/{id:long}/report"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<ClinicalReportResponse> Draft(ulong id, MedicalReportWriteRequest request, CancellationToken ct) => clinical.SaveDraftAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/complete"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<ClinicalAppointmentResponse> Complete(ulong id, CompleteAppointmentRequest request, CancellationToken ct) => clinical.CompleteAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/start"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<ArrivalResponse> Start(ulong id, StartAppointmentRequest request, CancellationToken ct) => arrivals.StartAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/no-show"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<ClinicalAppointmentResponse> NoShow(ulong id, RecordNoShowRequest request, CancellationToken ct) => clinical.RecordNoShowAsync(Actor, ViverAppRoles.Doctor, id, request, ct);
    [HttpPost("appointments/{id:long}/report/rectify"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<IReadOnlyList<DoctorReportVersionResponse>> Rectify(ulong id, DoctorReportRectificationRequest request, CancellationToken ct) => service.RectifyAsync(Actor, id, request, ct);

    [HttpGet("patients")]
    public Task<DoctorPatientsResponse> Patients(string? search = null, string? status = null, bool? premium = null,
        int page = 1, int pageSize = 20, CancellationToken ct = default) => service.PatientsAsync(Actor, search, status, premium, page, pageSize, ct);
    [HttpGet("patients/{id:long}")]
    public Task<DoctorPatientResponse> Patient(ulong id, CancellationToken ct) => service.PatientAsync(Actor, id, ct);
    [HttpPost("patients/link"), EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    public Task<DoctorPatientResponse> Link(DoctorPatientLinkRequest request, CancellationToken ct) => service.LinkPatientAsync(Actor, request, ct);
    [HttpPost("patients/invite"), EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    public Task<DoctorPatientResponse> Invite(DoctorPatientInviteRequest request, CancellationToken ct) => service.InvitePatientAsync(Actor, request, ct);
    [HttpPut("patients/{id:long}"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<DoctorPatientResponse> UpdatePatient(ulong id, DoctorPatientUpdateRequest request, CancellationToken ct) => service.UpdatePatientAsync(Actor, id, request, ct);

    [HttpGet("notifications")]
    public Task<DoctorNotificationsResponse> Notifications(int page = 1, int pageSize = 20, CancellationToken ct = default) => arrivals.NotificationsAsync(Actor, page, pageSize, ct);
    [HttpPost("notifications/{id:long}/read"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> ReadNotification(ulong id, ArrivalRequest request, CancellationToken ct) { await arrivals.ReadAsync(Actor, id, request.RowVersion, ct); return NoContent(); }
    [HttpPost("notifications/read-all"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> ReadAllNotifications(CancellationToken ct) { await arrivals.ReadAllAsync(Actor, ct); return NoContent(); }

    [HttpGet("availability")]
    public Task<DoctorAvailabilityResponse> Availability(DateOnly from, DateOnly to, CancellationToken ct) => service.AvailabilityAsync(Actor, from, to, ct);
    [HttpPut("availability"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<DoctorAvailabilityResponse> Availability(DoctorAvailabilitySettingsRequest request, CancellationToken ct) => service.UpdateAvailabilityAsync(Actor, request, ct);
    [HttpPost("availability/exceptions"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<DoctorAvailabilityExceptionResponse> AddException(DoctorAvailabilityExceptionRequest request, CancellationToken ct) => service.SaveExceptionAsync(Actor, null, request, ct);
    [HttpPut("availability/exceptions/{id:long}"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<DoctorAvailabilityExceptionResponse> UpdateException(ulong id, DoctorAvailabilityExceptionRequest request, CancellationToken ct) => service.SaveExceptionAsync(Actor, id, request, ct);
    [HttpDelete("availability/exceptions/{id:long}"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> DeleteException(ulong id, ulong rowVersion, CancellationToken ct) { await service.DeleteExceptionAsync(Actor, id, rowVersion, ct); return NoContent(); }

    [HttpPost("appointments/{id:long}/documents"), EnableRateLimiting(SecurityPolicyNames.UploadRateLimit)]
    [RequestSizeLimit(10_600_000), RequestFormLimits(MultipartBodyLengthLimit = 10_600_000)]
    public async Task<ActionResult<DoctorDocumentResponse>> Upload(ulong id, IFormFile file, CancellationToken ct)
    {
        var appointment = await service.AppointmentAsync(Actor, id, ct);
        if (appointment.Appointment.StatusCode is not ("confirmed" or "completed")) throw DoctorExperienceService.Conflict("Anexos só podem ser enviados em atendimentos confirmados ou concluídos.");
        var document = await documents.PrepareClinicalAsync(appointment.Appointment.PatientAccountId, file, ct);
        await using var transaction = await database.Database.BeginTransactionAsync(ct);
        database.PrivateDocuments.Add(document);
        var link = new AppointmentDocument
        {
            AppointmentId = id,
            UploadedByAccountId = Actor,
            CategoryCode = "attachment",
            ObjectKey = document.Id.ToString("D"),
            OriginalFileName = document.OriginalFileName,
            ContentType = document.ContentType,
            SizeBytes = document.SizeBytes,
            Sha256 = document.Sha256,
            StatusCode = "available",
            CreatedAtUtc = DateTime.UtcNow,
            AvailableAtUtc = DateTime.UtcNow,
            RowVersion = 1
        };
        database.AppointmentDocuments.Add(link); await database.SaveChangesAsync(ct);
        await audit.WriteAsync("doctor.appointment_document.uploaded", Actor, "appointment_document", link.Id.ToString(),
            new Dictionary<string, string> { ["appointmentId"] = id.ToString(), ["sizeBytes"] = link.SizeBytes.ToString() }, ct);
        await transaction.CommitAsync(ct);
        return CreatedAtAction(nameof(Download), new { id = link.Id }, new DoctorDocumentResponse(link.Id, link.OriginalFileName, link.ContentType, link.SizeBytes, link.CreatedAtUtc, link.RowVersion));
    }
    [HttpGet("documents/{id:long}")]
    public async Task<IActionResult> Download(ulong id, CancellationToken ct)
    {
        var result = await documents.DownloadForDoctorAsync(Actor, id, ct);
        await audit.WriteAsync("doctor.appointment_document.downloaded", Actor, "appointment_document", id.ToString(), null, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Content, result.Mime, result.Name, enableRangeProcessing: false);
    }
    [HttpDelete("documents/{id:long}"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> DeleteDocument(ulong id, ulong rowVersion, CancellationToken ct)
    {
        var link = await database.AppointmentDocuments.SingleOrDefaultAsync(x => x.Id == id && x.Appointment.DoctorAccountId == Actor && x.StatusCode == "available", ct)
            ?? throw DoctorExperienceService.Missing();
        if (link.RowVersion != rowVersion) throw DoctorExperienceService.Conflict("O anexo foi alterado por outra sessão.");
        if (link.UploadedByAccountId != Actor) throw DoctorExperienceService.Conflict("Somente o autor pode remover este anexo.");
        link.StatusCode = "deleted"; link.DeletedAtUtc = DateTime.UtcNow; link.DeletedByAccountId = Actor; link.RowVersion++;
        if (Guid.TryParse(link.ObjectKey, out var privateId))
        {
            var document = await database.PrivateDocuments.SingleOrDefaultAsync(x => x.Id == privateId, ct);
            if (document is not null) document.StatusCode = "deleted";
        }
        await database.SaveChangesAsync(ct); await audit.WriteAsync("doctor.appointment_document.deleted", Actor, "appointment_document", id.ToString(), null, ct);
        return NoContent();
    }
}
