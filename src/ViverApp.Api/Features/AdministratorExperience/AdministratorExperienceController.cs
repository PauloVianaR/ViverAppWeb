using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using ViverApp.Api.Features.ManagerExperience;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Api.Features.ClinicalOperations;
using ViverApp.Api.Features.ArrivalExperience;
using ViverApp.Security;

namespace ViverApp.Api.Features.AdministratorExperience;

public sealed class AdministratorExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var error = context.Exception switch { AdministratorRuleException e => (e.StatusCode, e.Message), ManagerRuleException e => (e.StatusCode, e.Message), PatientExperienceException e => (e.StatusCode, e.Message), ArrivalRuleException e => (e.StatusCode, e.Message), _ => default };
        if (error == default) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = error.Item1, Title = error.Item2 }) { StatusCode = error.Item1 };
        context.ExceptionHandled = true;
    }
}

public sealed class AdministratorStepUpFilter(RecentAuthentication recent) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method)
            || !context.HttpContext.User.IsInRole(ViverAppRoles.Administrator)) { await next(); return; }
        var idValue = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!ulong.TryParse(idValue, CultureInfo.InvariantCulture, out _) || !await recent.IsRecentAsync(context.HttpContext.User, context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(new ProblemDetails { Status = 403, Title = "Confirme novamente sua identidade antes desta ação administrativa.", Extensions = { ["code"] = "administrator_step_up_required" } }) { StatusCode = 403 };
            return;
        }
        await next();
    }
}

[ApiController, Route("api/v1/administrator"), Authorize(Policy = ViverAppPolicies.Administrator)]
[ServiceFilter(typeof(AdministratorExceptionFilter)), ServiceFilter(typeof(AdministratorStepUpFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdministratorExperienceController(AdministratorExperienceService service, PatientSchedulingService scheduling,
    PrivateDocumentStore documents, IClinicalOperationsAuditWriter audit, ArrivalExperienceService arrivals) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    [HttpGet("home")] public Task<AdministratorHomeResponse> Home(CancellationToken ct) => service.HomeAsync(Actor, ct);
    [HttpGet("doctors")] public Task<IReadOnlyList<ManagerDoctorOption>> Doctors(CancellationToken ct) => service.DoctorsAsync(ct);
    [HttpGet("doctor-access")] public Task<IReadOnlyList<AdministratorDoctorAccessResponse>> DoctorAccess(CancellationToken ct) => service.DoctorAccessAsync(ct);
    [HttpGet("agenda")]
    public Task<ManagerAgendaResponse> Agenda(DateOnly from, DateOnly to, string? status = null, string? modality = null, string? category = null,
        ulong? doctorAccountId = null, ulong? appointmentNumber = null, string? payment = null, string? search = null, string sort = "date_asc", int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        service.AgendaAsync(from, to, status, modality, category, doctorAccountId, appointmentNumber, payment, search, sort, page, pageSize, ct);
    [HttpGet("appointments/{id:long}")] public Task<ManagerAppointmentResponse> Appointment(ulong id, CancellationToken ct) => service.AppointmentAsync(id, ct);
    [HttpGet("booking/slots"), EnableRateLimiting(SecurityPolicyNames.SlotRateLimit)] public Task<IReadOnlyList<AvailableSlotResponse>> Slots(ulong patientAccountId, ulong doctorAccountId, uint appointmentTypeId, string modality, DateOnly from, int days = 14, CancellationToken ct = default) => scheduling.GetAvailableSlotsAsync(patientAccountId, doctorAccountId, appointmentTypeId, modality, from, days, ct);
    [HttpGet("booking/available-dates"), EnableRateLimiting(SecurityPolicyNames.SlotRateLimit)] public Task<IReadOnlyList<DateOnly>> AvailableDates(ulong patientAccountId, ulong doctorAccountId, uint appointmentTypeId, string modality, DateOnly from, int days = 31, CancellationToken ct = default) => scheduling.GetAvailableDatesAsync(patientAccountId, doctorAccountId, appointmentTypeId, modality, from, days, ct);
    [HttpPost("appointments/{id:long}/cancel"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)] public Task<AppointmentResponse> Cancel(ulong id, AppointmentCancelRequest request, CancellationToken ct) => scheduling.CancelForManagerAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/reschedule"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)] public async Task<AppointmentResponse> Reschedule(ulong id, [FromHeader(Name = "Idempotency-Key")] string key, AppointmentRescheduleRequest request, CancellationToken ct) { var result = await scheduling.RescheduleForManagerAsync(Actor, id, key, request, ct); if (result.Replayed) Response.Headers["Idempotent-Replayed"] = "true"; return result.Response; }
    [HttpPost("appointments/{id:long}/payment"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public Task<ManagerPaymentResponse> Payment(ulong id, [FromHeader(Name = "Idempotency-Key")] string key, ManagerPaymentConfirmRequest request, CancellationToken ct) => service.ConfirmPaymentAsync(Actor, id, key, request, ct);
    [HttpPost("appointments/{id:long}/arrival"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public Task<ArrivalResponse> RegisterArrival(ulong id, ArrivalRequest request, CancellationToken ct) => arrivals.RegisterAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/arrival/cancel"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public Task<ArrivalResponse> CancelArrival(ulong id, ArrivalCancellationRequest request, CancellationToken ct) => arrivals.CancelAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/complete"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public Task<ArrivalResponse> Complete(ulong id, AppointmentTransitionRequest request, CancellationToken ct) => arrivals.CompleteForManagementAsync(Actor, id, request, ct);
    [HttpPost("appointments/{id:long}/reopen"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public Task<ArrivalResponse> ReopenAppointment(ulong id, AppointmentReopenRequest request, CancellationToken ct) => arrivals.ReopenAsync(Actor, id, request, ct);
    [HttpGet("patients")] public Task<ManagerPatientsResponse> Patients(string? search = null, string? status = null, bool? premium = null, int page = 1, int pageSize = 20, CancellationToken ct = default) => service.PatientsAsync(search, status, premium, page, pageSize, ct);
    [HttpGet("patients/{id:long}")] public Task<ManagerPatientResponse> Patient(ulong id, CancellationToken ct) => service.PatientAsync(id, ct);
    [HttpPost("patients"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public Task<ManagerPatientResponse> CreatePatient(ManagerPatientCreateRequest request, CancellationToken ct) => service.CreatePatientAsync(Actor, request, ct);
    [HttpPut("patients/{id:long}"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public Task<ManagerPatientResponse> UpdatePatient(ulong id, ManagerPatientUpdateRequest request, CancellationToken ct) => service.UpdatePatientAsync(Actor, id, request, ct);
    [HttpPost("patients/{id:long}/premium"), EnableRateLimiting(SecurityPolicyNames.UploadRateLimit), RequestSizeLimit(5_300_000), RequestFormLimits(MultipartBodyLengthLimit = 5_300_000)]
    public async Task<ManagerPatientResponse> ActivatePatientPremium(ulong id, IFormFile file, CancellationToken ct) => await service.ActivatePatientPremiumAsync(Actor, id, await documents.PrepareAsync(id, file, ct), ct);
    [HttpPost("patients/{id:long}/premium/cancel"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<ManagerPatientResponse> DeactivatePatientPremium(ulong id, ManagerPremiumCancelRequest request, CancellationToken ct) => service.DeactivatePatientPremiumAsync(Actor, id, request, ct);
    [HttpGet("analytics")] public Task<AdministratorAnalyticsResponse> Analytics(DateOnly from, DateOnly to, CancellationToken ct) => service.AnalyticsAsync(from, to, ct);
    [HttpGet("premium")] public Task<ViverApp.Api.Features.PatientScheduling.SchedulingPage<ManagerPremiumRequestResponse>> Premium(string? status = null, string? search = null, int page = 1, int pageSize = 20, CancellationToken ct = default) => service.PremiumAsync(status, search, page, pageSize, ct);
    [HttpPost("premium/{id:long}/decision"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public Task<ManagerPremiumRequestResponse> PremiumDecision(ulong id, ManagerPremiumDecisionRequest request, CancellationToken ct) => service.DecidePremiumAsync(Actor, id, request, ct);
    [HttpPost("premium/{id:long}/cancel"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public Task<ManagerPremiumRequestResponse> PremiumCancel(ulong id, AdministratorPremiumCancelRequest request, CancellationToken ct) => service.CancelPremiumAsync(Actor, id, request, ct);
    [HttpGet("premium/{id:long}/proof"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public async Task<IActionResult> PremiumProof(ulong id, CancellationToken ct) { var result = await documents.DownloadPremiumForManagerAsync(Actor, id, ct); await audit.WriteAsync("administrator.premium.proof_viewed", Actor, "private_document", result.DocumentId.ToString("D"), new Dictionary<string, string> { { "membershipId", id.ToString(CultureInfo.InvariantCulture) } }, ct); Response.Headers.XContentTypeOptions = "nosniff"; return File(result.Content, result.Mime, result.Name, false); }
    [HttpGet("settings")] public Task<IReadOnlyList<AdministratorSettingResponse>> Settings(CancellationToken ct) => service.SettingsAsync(ct);
    [HttpPut("settings/{key}"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)] public Task<AdministratorSettingResponse> Setting(string key, AdministratorSettingUpdateRequest request, CancellationToken ct) => service.UpdateSettingAsync(Actor, key, request, ct);
    [HttpGet("premium-plans")] public Task<IReadOnlyList<AdministratorPremiumPlanResponse>> PremiumPlans(CancellationToken ct) => service.PremiumPlansAsync(ct);
    [HttpPut("premium-plans/{id:int}"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)] public Task<AdministratorPremiumPlanResponse> PremiumPlan(uint id, AdministratorPremiumPlanUpdateRequest request, CancellationToken ct) => service.UpdatePremiumPlanAsync(Actor, id, request, ct);
    [HttpPost("users/{id:long}/status"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)] public async Task<IActionResult> Status(ulong id, AdministratorAccountStatusRequest request, CancellationToken ct) { await service.SetAccountStatusAsync(Actor, id, request, ct); return NoContent(); }
    [HttpPost("professionals/{id:long}/reopen"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)] public async Task<IActionResult> Reopen(ulong id, AdministratorAccountStatusRequest request, CancellationToken ct) { await service.ReopenProfessionalAsync(Actor, id, request, ct); return NoContent(); }
    [HttpPost("doctors/{id:long}/online"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)] public async Task<IActionResult> DoctorOnline(ulong id, AdministratorDoctorOnlineRequest request, CancellationToken ct) { await service.SetDoctorOnlineAsync(Actor, id, request, ct); return NoContent(); }
    [HttpGet("notifications")] public Task<AdministratorNotificationsResponse> Notifications(string? type = null, string? severity = null, string? read = null, CancellationToken ct = default) => service.NotificationsAsync(Actor, type, severity, read, ct);
    [HttpPost("notifications/{id:long}/read")] public async Task<IActionResult> ReadNotification(ulong id, AdministratorNotificationUpdateRequest request, CancellationToken ct) { await service.ReadNotificationAsync(Actor, id, request.RowVersion, ct); return NoContent(); }
    [HttpPost("notifications/read-all")] public async Task<IActionResult> ReadAll(CancellationToken ct) { await service.ReadAllNotificationsAsync(Actor, ct); return NoContent(); }
    [HttpPost("notifications/{id:long}/dismiss")] public async Task<IActionResult> Dismiss(ulong id, AdministratorNotificationUpdateRequest request, CancellationToken ct) { await service.DismissNotificationAsync(Actor, id, request.RowVersion, ct); return NoContent(); }
}
