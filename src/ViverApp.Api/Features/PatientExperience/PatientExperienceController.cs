using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientScheduling;
using ViverApp.Security;

namespace ViverApp.Api.Features.PatientExperience;

public sealed class PatientExperienceExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException
            || context.Exception is Microsoft.EntityFrameworkCore.DbUpdateException { InnerException: MySql.Data.MySqlClient.MySqlException { Number: 1062 } })
        {
            context.Result = new ConflictObjectResult(new ProblemDetails { Status = 409, Title = "Não foi possível concluir a alteração. Atualize a página e confira os dados." });
            context.ExceptionHandled = true;
            return;
        }
        if (context.Exception is not PatientExperienceException error) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = error.StatusCode, Title = error.Message }) { StatusCode = error.StatusCode };
        context.ExceptionHandled = true;
    }
}

[ApiController, Route("api/v1/patient/experience"), Authorize(Policy = ViverAppPolicies.Patient)]
[ServiceFilter(typeof(PatientExperienceExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PatientExperienceController(PatientExperienceService service, PrivateDocumentStore documents, IdentityAuditWriter audit) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    [HttpGet("home")]
    public Task<PatientHomeResponse> Home(CancellationToken ct) => service.HomeAsync(Actor, ct);
    [HttpGet("clinic")]
    public Task<PatientClinicResponse> Clinic(CancellationToken ct) => service.ClinicAsync(ct);
    [HttpGet("services")]
    public Task<SchedulingPage<PatientServiceResponse>> Services(int page = 1, int pageSize = 20, string? category = null, string? search = null, CancellationToken ct = default) => service.ServicesAsync(Actor, page, pageSize, category, search, ct);
    [HttpGet("agenda")]
    public Task<SchedulingPage<PatientAppointmentResponse>> Agenda(int page = 1, int pageSize = 12, string view = "future", string? search = null, DateTime? from = null, DateTime? until = null, string? status = null, string? category = null, string? modality = null, CancellationToken ct = default) => service.AgendaAsync(Actor, page, pageSize, view, search, from, until, status, category, modality, ct);
    [HttpGet("services/{id:int}")]
    public Task<PatientServiceResponse> Service(uint id, CancellationToken ct) => service.ServiceAsync(Actor, id, ct);
    [HttpGet("appointments/{id:long}")]
    public Task<PatientAppointmentResponse> Appointment(ulong id, CancellationToken ct) => service.AppointmentAsync(Actor, id, ct);
    [HttpPost("appointments/{id:long}/review"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> Review(ulong id, PatientReviewRequest request, CancellationToken ct) { await service.ReviewAsync(Actor, id, request, ct); return NoContent(); }
    [HttpGet("profile")]
    public Task<PatientProfileResponse> Profile(CancellationToken ct) => service.ProfileAsync(Actor, ct);
    [HttpPut("profile"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> Profile(PatientProfileRequest request, CancellationToken ct) { await service.UpdateProfileAsync(Actor, request, ct); return NoContent(); }
    [HttpGet("premium")]
    public Task<PatientPremiumResponse> Premium(CancellationToken ct) => service.PremiumAsync(Actor, ct);
    [HttpPost("premium"), EnableRateLimiting(SecurityPolicyNames.UploadRateLimit)]
    [RequestSizeLimit(5_300_000), RequestFormLimits(MultipartBodyLengthLimit = 5_300_000)]
    public async Task<IActionResult> RequestPremium([FromForm] uint planId, IFormFile file, CancellationToken ct)
    {
        if (!(await service.PremiumAsync(Actor, ct)).CanRequest) throw PatientExperienceService.Conflict("Solicitação indisponível neste momento.");
        var document = await documents.PrepareAsync(Actor, file, ct);
        await service.RequestPremiumAsync(Actor, planId, document, ct);
        return Ok(await service.PremiumAsync(Actor, ct));
    }
    [HttpPost("premium/{id:long}/cancel"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> CancelPremium(ulong id, PatientPremiumCancelRequest request, CancellationToken ct) { await service.CancelPremiumAsync(Actor, id, request.RowVersion, ct); return NoContent(); }
    [HttpGet("appointments/{id:long}/documents")]
    public Task<SchedulingPage<PatientDocumentResponse>> Documents(ulong id, int page = 1, int pageSize = 20, CancellationToken ct = default) => documents.ListAsync(Actor, id, page, pageSize, ct);
    [HttpGet("documents/{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var result = await documents.DownloadAsync(Actor, id, ct);
        await audit.WriteAsync("patient.document_downloaded", Actor, "document", id.ToString(), null, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(result.Content, result.Mime, result.Name, enableRangeProcessing: false);
    }
    [HttpGet("payments")]
    public Task<SchedulingPage<PatientPaymentItem>> Payments(int page = 1, int pageSize = 12, string view = "pending", DateTime? from = null, DateTime? until = null, decimal? min = null, decimal? max = null, string? method = null, string? location = null, string? status = null, CancellationToken ct = default) => service.PaymentsAsync(Actor, page, pageSize, view, from, until, min, max, method, location, status, ct);
    [HttpPost("appointments/{id:long}/pay-at-clinic"), EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<IActionResult> PayAtClinic(ulong id, CancellationToken ct) { await service.ChooseClinicPaymentAsync(Actor, id, ct); return NoContent(); }
}
