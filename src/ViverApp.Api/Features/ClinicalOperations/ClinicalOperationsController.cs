using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Security;

namespace ViverApp.Api.Features.ClinicalOperations;

[ApiController]
[Route("api/v1/clinical")]
[Authorize(Policy = ViverAppPolicies.ClinicalStaff)]
public sealed class ClinicalOperationsController(
    ClinicalOperationsService operations,
    RecentAuthentication recent) : ControllerBase
{
    [HttpGet("context")]
    public Task<ActionResult<ClinicalContextResponse>> GetContext(CancellationToken cancellationToken) =>
        ExecuteAsync(() => operations.GetContextAsync(ActorId, RoleCode, cancellationToken));

    [HttpGet("appointments")]
    public Task<ActionResult<ClinicalPage<ClinicalAppointmentResponse>>> GetAppointments(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? status = null,
        [FromQuery] ulong? doctorAccountId = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => operations.GetAppointmentsAsync(
            ActorId, RoleCode, from, to, status, doctorAccountId, search, page, pageSize, cancellationToken));

    [HttpGet("appointments/{id:long}")]
    public async Task<ActionResult<ClinicalAppointmentResponse>> GetAppointment(
        ulong id,
        CancellationToken cancellationToken)
    {
        if (!await AdminAccessIsRecentAsync(cancellationToken)) return Forbid();
        return await ExecuteAsync(() => operations.GetAppointmentAsync(ActorId, RoleCode, id, cancellationToken));
    }

    [HttpGet("patients")]
    public Task<ActionResult<ClinicalPage<ClinicalPatientResponse>>> GetPatients(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => operations.GetPatientsAsync(ActorId, RoleCode, search, page, pageSize, cancellationToken));

    [HttpPut("appointments/{id:long}/medical-report")]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public async Task<ActionResult<ClinicalReportResponse>> SaveDraft(
        ulong id,
        [FromBody] MedicalReportWriteRequest request,
        CancellationToken cancellationToken)
    {
        if (!await AdminAccessIsRecentAsync(cancellationToken)) return Forbid();
        return await ExecuteAsync(() => operations.SaveDraftAsync(ActorId, RoleCode, id, request, cancellationToken));
    }

    [HttpGet("appointments/{id:long}/medical-report/versions")]
    public async Task<ActionResult<IReadOnlyList<ClinicalReportVersionResponse>>> ReportVersions(
        ulong id, CancellationToken cancellationToken)
    {
        if (!await AdminAccessIsRecentAsync(cancellationToken)) return Forbid();
        return await ExecuteAsync(() => operations.GetOphthalmologyVersionsAsync(ActorId, RoleCode, id, cancellationToken));
    }

    private Task<bool> AdminAccessIsRecentAsync(CancellationToken ct) =>
        RoleCode == ViverAppRoles.Administrator ? recent.IsRecentAsync(User, ct) : Task.FromResult(true);

    [HttpPost("appointments/{id:long}/complete")]
    [Authorize(Policy = ViverAppPolicies.ClinicalProfessional)]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<ActionResult<ClinicalAppointmentResponse>> Complete(
        ulong id,
        [FromBody] CompleteAppointmentRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => operations.CompleteAsync(ActorId, id, request, cancellationToken));

    [HttpPost("appointments/{id:long}/no-show")]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<ActionResult<ClinicalAppointmentResponse>> RecordNoShow(
        ulong id,
        [FromBody] RecordNoShowRequest request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(() => operations.RecordNoShowAsync(ActorId, RoleCode, id, request, cancellationToken));

    private ulong ActorId
    {
        get
        {
            var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : throw new InvalidOperationException("authenticated_account_id_missing");
        }
    }

    private string RoleCode =>
        User.FindFirstValue(ClaimTypes.Role) ?? throw new InvalidOperationException("authenticated_role_missing");

    private async Task<ActionResult<T>> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (ClinicalRuleException exception)
        {
            return Problem(statusCode: exception.StatusCode, title: exception.Message);
        }
    }
}
