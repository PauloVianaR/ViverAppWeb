using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.CashManagement;
using ViverApp.Api.Features.ArrivalExperience;
using ViverApp.Api.Features.DoctorExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Security;

namespace ViverApp.Api.Features.ManagerExperience;

public sealed class ManagerExperienceExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var result = context.Exception switch
        {
            ManagerRuleException e => (e.StatusCode, e.Message),
            ArrivalRuleException e => (e.StatusCode, e.Message),
            SchedulingRuleException e => (e.StatusCode, e.Message),
            CashRuleException e => (e.StatusCode, e.Message),
            PatientExperienceException e => (e.StatusCode, e.Message),
            DbUpdateConcurrencyException => (409, "Os dados foram alterados por outra sessão. Atualize a página."),
            _ => default,
        };
        if (result == default) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = result.Item1, Title = result.Item2 }) { StatusCode = result.Item1 };
        context.ExceptionHandled = true;
    }
}

[ApiController, Route("api/v1/manager"), Authorize(Policy = ViverAppPolicies.Manager)]
[ServiceFilter(typeof(ManagerExperienceExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ManagerExperienceController(ManagerExperienceService service, PatientSchedulingService scheduling,
    PrivateDocumentStore documents, IClinicalOperationsAuditWriter audit, ArrivalExperienceService arrivals) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    [HttpGet("home")] public Task<ManagerHomeResponse> Home(CancellationToken ct) => service.HomeAsync(Actor, ct);
    [HttpGet("profile")] public Task<ManagerProfileResponse> Profile(CancellationToken ct) => service.ProfileAsync(Actor, ct);
    [HttpPut("profile"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<ManagerProfileResponse> Profile(ManagerProfileUpdateRequest request, CancellationToken ct) => service.UpdateProfileAsync(Actor, request, ct);
    [HttpGet("doctors")] public Task<IReadOnlyList<ManagerDoctorOption>> Doctors(CancellationToken ct) => service.DoctorsAsync(ct);
    [HttpGet("services")] public Task<IReadOnlyList<ManagerServiceOption>> Services(CancellationToken ct) => service.ServicesAsync(ct);
    [HttpGet("capabilities")] public Task<ManagerCapabilitiesResponse> Capabilities(CancellationToken ct) => service.CapabilitiesAsync(ct);

    [HttpGet("agenda")]
    public Task<ManagerAgendaResponse> Agenda(DateOnly from, DateOnly to, string? status = null, string? modality = null,
        string? category = null, ulong? doctorAccountId = null, ulong? appointmentNumber = null, string? payment = null, TimeOnly? startTime = null,
        TimeOnly? endTime = null, string? search = null, string sort = "date_asc", int page = 1, int pageSize = 20,
        CancellationToken ct = default) => service.AgendaAsync(from, to, status, modality, category, doctorAccountId, appointmentNumber,
            payment, startTime, endTime, search, sort, page, pageSize, ct);
    [HttpGet("appointments/{id:long}")] public Task<ManagerAppointmentResponse> Appointment(ulong id, CancellationToken ct) => service.AppointmentAsync(id, ct);
    [HttpGet("booking/slots"), EnableRateLimiting(SecurityPolicyNames.SlotRateLimit)]
    public Task<IReadOnlyList<AvailableSlotResponse>> Slots(ulong patientAccountId, ulong doctorAccountId, uint appointmentTypeId,
        string modality, DateOnly from, int days = 14, [FromQuery] uint[]? additionalAppointmentTypeIds = null,
        int? durationMinutes = null, CancellationToken ct = default) =>
        scheduling.GetAvailableSlotsAsync(patientAccountId, doctorAccountId, appointmentTypeId, modality, from, days, ct, additionalAppointmentTypeIds, durationMinutes);
    [HttpGet("booking/available-dates"), EnableRateLimiting(SecurityPolicyNames.SlotRateLimit)]
    public Task<IReadOnlyList<DateOnly>> AvailableDates(ulong patientAccountId, ulong doctorAccountId,
        uint appointmentTypeId, string modality, DateOnly from, int days = 31, [FromQuery] uint[]? additionalAppointmentTypeIds = null,
        int? durationMinutes = null, CancellationToken ct = default) =>
        scheduling.GetAvailableDatesAsync(patientAccountId, doctorAccountId, appointmentTypeId, modality, from, days, ct, additionalAppointmentTypeIds, durationMinutes);
    [HttpGet("booking/professionals"), EnableRateLimiting(SecurityPolicyNames.SlotRateLimit)]
    public Task<SchedulingPage<BookingProfessionalResponse>> Professionals(uint appointmentTypeId, string modality,
        string? search = null, int page = 1, int pageSize = 50, [FromQuery] uint[]? additionalAppointmentTypeIds = null, CancellationToken ct = default) =>
        scheduling.SearchProfessionalsAsync(page, pageSize, search, null, appointmentTypeId, modality, ct, additionalAppointmentTypeIds);
    [HttpPost("appointments"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<ActionResult<AppointmentResponse>> Create([FromHeader(Name = "Idempotency-Key")] string key,
        ManagerAppointmentCreateRequest request, CancellationToken ct)
    {
        var result = await scheduling.CreateForManagerAsync(Actor, key, request, ct);
        if (result.Replayed) Response.Headers["Idempotent-Replayed"] = "true";
        return result.Replayed ? Ok(result.Response) : CreatedAtAction(nameof(Appointment), new { id = result.Response.Id }, result.Response);
    }
    [HttpPost("appointments/{id:long}/cancel"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<AppointmentResponse> Cancel(ulong id, AppointmentCancelRequest request, CancellationToken ct) => scheduling.CancelForManagerAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/reschedule"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<AppointmentResponse> Reschedule(ulong id, [FromHeader(Name = "Idempotency-Key")] string key,
        AppointmentRescheduleRequest request, CancellationToken ct)
    {
        var result = await scheduling.RescheduleForManagerAsync(Actor, id, key, request, ct);
        if (result.Replayed) Response.Headers["Idempotent-Replayed"] = "true";
        return result.Response;
    }
    [HttpPost("appointments/{id:long}/payment"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<ManagerPaymentResponse> ConfirmPayment(ulong id, [FromHeader(Name = "Idempotency-Key")] string key,
        ManagerPaymentConfirmRequest request, CancellationToken ct) => service.ConfirmPaymentAsync(Actor, id, key, request, ct);
    [HttpPost("appointments/{id:long}/point-discount"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<AppointmentResponse> ApplyPointDiscount(ulong id, AppointmentPointDiscountRequest request, CancellationToken ct) =>
        scheduling.ApplyPointDiscountForManagerAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/arrival"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<ArrivalResponse> RegisterArrival(ulong id, ArrivalRequest request, CancellationToken ct) => arrivals.RegisterAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/arrival/cancel"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<ArrivalResponse> CancelArrival(ulong id, ArrivalCancellationRequest request, CancellationToken ct) => arrivals.CancelAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/complete"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<ArrivalResponse> Complete(ulong id, AppointmentTransitionRequest request, CancellationToken ct) =>
        arrivals.CompleteForManagementAsync(Actor, id, request, ct);

    [HttpGet("patients")]
    public Task<ManagerPatientsResponse> Patients(string? search = null, string? status = null,
        bool? premium = null, int page = 1, int pageSize = 20, CancellationToken ct = default) => service.PatientsAsync(search, status, premium, page, pageSize, ct);
    [HttpGet("patients/{id:long}")] public Task<ManagerPatientResponse> Patient(ulong id, CancellationToken ct) => service.PatientAsync(id, ct);
    [HttpPost("patients"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<ManagerPatientResponse> CreatePatient(ManagerPatientCreateRequest request, CancellationToken ct) => service.CreatePatientAsync(Actor, request, ct);
    [HttpPut("patients/{id:long}"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<ManagerPatientResponse> UpdatePatient(ulong id, ManagerPatientUpdateRequest request, CancellationToken ct) => service.UpdatePatientAsync(Actor, id, request, ct);
    [HttpPost("patients/{id:long}/premium"), EnableRateLimiting(SecurityPolicyNames.UploadRateLimit)]
    [RequestSizeLimit(5_300_000), RequestFormLimits(MultipartBodyLengthLimit = 5_300_000)]
    public async Task<ManagerPatientResponse> ActivatePremium(ulong id, IFormFile file, CancellationToken ct) =>
        await service.ActivatePremiumAsync(Actor, id, await documents.PrepareAsync(id, file, ct), false, ct);
    [HttpPost("patients/{id:long}/premium/cancel"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<ManagerPatientResponse> DeactivatePremium(ulong id, ManagerPremiumCancelRequest request, CancellationToken ct) =>
        service.DeactivatePremiumAsync(Actor, id, request, false, ct);

    [HttpGet("premium")]
    public Task<SchedulingPage<ManagerPremiumRequestResponse>> Premium(string? status = null,
        string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default) => service.PremiumAsync(status, search, page, pageSize, ct);
    [HttpGet("premium/{id:long}")] public Task<ManagerPremiumRequestResponse> PremiumRequest(ulong id, CancellationToken ct) => service.PremiumRequestAsync(id, ct);
    [HttpPost("premium/{id:long}/decision"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<ManagerPremiumRequestResponse> DecidePremium(ulong id, ManagerPremiumDecisionRequest request, CancellationToken ct) => service.DecidePremiumAsync(Actor, id, request, ct);
    [HttpGet("premium/{id:long}/proof"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<IActionResult> PremiumProof(ulong id, CancellationToken ct)
    {
        var result = await documents.DownloadPremiumForManagerAsync(Actor, id, ct);
        await audit.WriteAsync("manager.premium.proof_viewed", Actor, "private_document", result.DocumentId.ToString("D"), new Dictionary<string, string> { ["membershipId"] = id.ToString(CultureInfo.InvariantCulture) }, ct);
        Response.Headers.XContentTypeOptions = "nosniff"; return File(result.Content, result.Mime, result.Name, enableRangeProcessing: false);
    }
}

internal sealed class ManagerRuleException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
