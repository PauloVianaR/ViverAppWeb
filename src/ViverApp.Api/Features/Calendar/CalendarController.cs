using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ViverApp.Api.Features.Identity;

namespace ViverApp.Api.Features.Calendar;

[ApiController]
[Route("api/v1/calendar")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CalendarController(CalendarService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CalendarResponse>> Get(
        [FromQuery] string view,
        [FromQuery] DateOnly anchor,
        [FromQuery] ulong? professionalAccountId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.GetAsync(ActorId, RoleCode, view, anchor, professionalAccountId, cancellationToken));
        }
        catch (CalendarRuleException exception)
        {
            return Problem(statusCode: exception.StatusCode, title: exception.Message);
        }
    }

    [HttpGet("professionals")]
    [Authorize(Policy = ViverAppPolicies.Management)]
    public async Task<ActionResult<IReadOnlyList<CalendarProfessionalResponse>>> Professionals(CancellationToken cancellationToken) =>
        Ok(await service.ProfessionalsAsync(cancellationToken));

    private ulong ActorId => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    private string RoleCode => User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
}
