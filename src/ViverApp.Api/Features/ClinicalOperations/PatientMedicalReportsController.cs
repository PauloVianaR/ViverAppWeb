using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ViverApp.Api.Features.Identity;

namespace ViverApp.Api.Features.ClinicalOperations;

[ApiController]
[Route("api/v1/patient/appointments")]
[Authorize(Policy = ViverAppPolicies.Patient)]
public sealed class PatientMedicalReportsController(ClinicalOperationsService operations) : ControllerBase
{
    [HttpGet("{id:long}/medical-report")]
    public async Task<ActionResult<PatientMedicalReportResponse>> GetMedicalReport(
        ulong id,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await operations.GetPublishedPatientReportAsync(ActorId, id, cancellationToken));
        }
        catch (ClinicalRuleException exception)
        {
            return Problem(statusCode: exception.StatusCode, title: exception.Message);
        }
    }

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
}
