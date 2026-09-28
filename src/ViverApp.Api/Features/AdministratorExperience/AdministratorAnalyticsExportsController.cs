using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Features.PatientExperience;
using ViverApp.Security;

namespace ViverApp.Api.Features.AdministratorExperience;

[ApiController]
[Route("api/v1/administrator/analytics-exports")]
[Authorize(Policy = ViverAppPolicies.Administrator)]
[ServiceFilter(typeof(AdministratorExceptionFilter))]
[ServiceFilter(typeof(AdministratorStepUpFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdministratorAnalyticsExportsController(
    AdministratorAnalyticsExportService exports, RecentAuthentication recent) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);

    [HttpGet]
    public Task<IReadOnlyList<AdministratorAnalyticsExportResponse>> List(CancellationToken ct) =>
        exports.ListAsync(Actor, ct);

    [HttpPost]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    public Task<AdministratorAnalyticsExportResponse> Create(
        AdministratorAnalyticsExportRequest request, CancellationToken ct) =>
        exports.RequestAsync(Actor, request, ct);

    [HttpGet("{id:long}/download")]
    [EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<IActionResult> Download(ulong id, CancellationToken ct)
    {
        if (!await recent.IsRecentAsync(User, ct))
            return StatusCode(403, new ProblemDetails
            {
                Status = 403,
                Title = "Confirme novamente sua identidade antes de baixar esta exportação.",
                Extensions = { ["code"] = "administrator_step_up_required" },
            });
        var content = await exports.DownloadAsync(Actor, id, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(content, "text/csv; charset=utf-8", $"viverapp-analytics-{id}.csv");
    }
}
