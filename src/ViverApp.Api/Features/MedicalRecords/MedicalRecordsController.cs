using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Security;

namespace ViverApp.Api.Features.MedicalRecords;

public sealed class MedicalRecordExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not MedicalRecordRuleException error) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = error.StatusCode, Title = error.Message }) { StatusCode = error.StatusCode };
        context.ExceptionHandled = true;
    }
}

[ApiController]
[Route("api/v1/medical-records")]
[Authorize(Policy = ViverAppPolicies.MedicalRecordRead)]
[ServiceFilter(typeof(MedicalRecordExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MedicalRecordsController(
    MedicalRecordService records,
    ClinicalPdfRenderer pdf,
    RecentAuthentication recent) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    private string Role => User.FindFirstValue(ClaimTypes.Role) ?? throw new InvalidOperationException("authenticated_role_missing");

    [HttpGet("patients/{patientId:long}/summary")]
    public async Task<MedicalRecordPatientSummary> Summary(ulong patientId, CancellationToken ct) =>
        await records.SummaryAsync(Actor, Role, await IsRecentAsync(ct), patientId, ct);

    [HttpGet("patients/{patientId:long}/timeline")]
    public async Task<MedicalRecordPage<MedicalRecordTimelineEvent>> Timeline(ulong patientId, DateOnly from, DateOnly to,
        string? type = null, string order = "desc", int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        await records.TimelineAsync(Actor, Role, await IsRecentAsync(ct), patientId, from, to, type, order, page, pageSize, ct);

    [HttpGet("patients/{patientId:long}/financial")]
    public async Task<MedicalRecordFinancialSummary> Financial(ulong patientId, DateOnly from, DateOnly to, CancellationToken ct) =>
        await records.FinancialAsync(Actor, Role, await IsRecentAsync(ct), patientId, from, to, ct);

    [HttpGet("patients/{patientId:long}/appointments")]
    public async Task<IReadOnlyList<MedicalRecordAppointmentOption>> Appointments(ulong patientId, CancellationToken ct) =>
        await records.AppointmentsAsync(Actor, Role, await IsRecentAsync(ct), patientId, ct);

    [HttpGet("patients/{patientId:long}/entries")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordClinicalRead)]
    public async Task<IReadOnlyList<MedicalRecordEntryResponse>> Entries(ulong patientId,
        [FromHeader(Name = "X-Clinical-Purpose")] string? purpose, bool includeVersions = false, CancellationToken ct = default) =>
        await records.EntriesAsync(Actor, Role, await IsRecentAsync(ct), patientId, purpose, includeVersions, ct);

    [HttpGet("patients/{patientId:long}/appointments/{appointmentId:long}/draft")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordWrite)]
    public Task<MedicalRecordDraftResponse?> Draft(ulong patientId, ulong appointmentId, CancellationToken ct) =>
        records.DraftAsync(Actor, patientId, appointmentId, ct);

    [HttpPut("patients/{patientId:long}/appointments/{appointmentId:long}/draft")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordWrite), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<MedicalRecordDraftResponse> SaveDraft(ulong patientId, ulong appointmentId,
        MedicalRecordDraftWriteRequest request, CancellationToken ct) => records.SaveDraftAsync(Actor, patientId, appointmentId, request, ct);

    [HttpPost("patients/{patientId:long}/appointments/{appointmentId:long}/finalize")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordWrite), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<MedicalRecordVersionResponse> Finalize(ulong patientId, ulong appointmentId,
        MedicalRecordFinalizeRequest request, CancellationToken ct) => records.FinalizeAsync(Actor, patientId, appointmentId, request, ct);

    [HttpPost("patients/{patientId:long}/entries/{entryId:long}/rectify")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordWrite), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<MedicalRecordVersionResponse> Rectify(ulong patientId, ulong entryId,
        MedicalRecordRectifyRequest request, CancellationToken ct) => records.RectifyAsync(Actor, patientId, entryId, request, ct);

    [HttpGet("patients/{patientId:long}/documents")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordDocument)]
    public async Task<IReadOnlyList<MedicalRecordDocumentResponse>> Documents(ulong patientId,
        [FromHeader(Name = "X-Clinical-Purpose")] string? purpose, CancellationToken ct) =>
        await records.DocumentsAsync(Actor, Role, await IsRecentAsync(ct), patientId, purpose, ct);

    [HttpPost("patients/{patientId:long}/documents")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordWrite), EnableRateLimiting(SecurityPolicyNames.UploadRateLimit)]
    [RequestSizeLimit(10_600_000), RequestFormLimits(MultipartBodyLengthLimit = 10_600_000)]
    public Task<MedicalRecordDocumentResponse> Upload(ulong patientId, ulong appointmentId, string categoryCode,
        IFormFile file, CancellationToken ct) => records.UploadAsync(Actor, patientId, appointmentId, categoryCode, file, ct);

    [HttpGet("patients/{patientId:long}/documents/{documentId:long}")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordDocument)]
    [EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<IActionResult> Download(ulong patientId, ulong documentId,
        [FromHeader(Name = "X-Clinical-Purpose")] string? purpose, CancellationToken ct)
    {
        var result = await records.DownloadAsync(Actor, Role, await IsRecentAsync(ct), patientId, documentId, purpose, ct);
        PrivateResponse();
        return File(result.Content, result.Mime, result.Name, enableRangeProcessing: false);
    }

    [HttpDelete("patients/{patientId:long}/documents/{documentId:long}")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordWrite), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<IActionResult> Delete(ulong patientId, ulong documentId, ulong rowVersion, CancellationToken ct)
    {
        await records.DeleteDocumentAsync(Actor, patientId, documentId, rowVersion, ct);
        return NoContent();
    }

    [HttpPost("patients/{patientId:long}/pdf")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordExport)]
    [EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<IActionResult> Pdf(ulong patientId, MedicalRecordPdfRequest request, CancellationToken ct)
    {
        var snapshot = await records.PdfSnapshotAsync(Actor, Role, await IsRecentAsync(ct), patientId, request, ct);
        var content = pdf.Render(snapshot);
        PrivateResponse();
        return File(content, "application/pdf", $"prontuario-{patientId}-{DateTime.UtcNow:yyyyMMddHHmmss}.pdf", enableRangeProcessing: false);
    }

    [HttpGet("patients/{patientId:long}/access-history")]
    [Authorize(Policy = ViverAppPolicies.MedicalRecordAudit)]
    public async Task<IReadOnlyList<MedicalRecordAccessEventResponse>> AccessHistory(ulong patientId,
        [FromHeader(Name = "X-Clinical-Purpose")] string purpose, CancellationToken ct) =>
        await records.AccessHistoryAsync(Actor, await IsRecentAsync(ct), patientId, purpose, ct);

    private Task<bool> IsRecentAsync(CancellationToken ct) => Role == ViverAppRoles.Administrator
        ? recent.IsRecentAsync(User, ct) : Task.FromResult(true);

    private void PrivateResponse()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
    }
}
