using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ViverApp.Api.Features.Identity;
using ViverApp.Security;

namespace ViverApp.Api.Features.AdministratorExperience;

[ApiController, Route("api/v1/administrator/accounts"), Authorize(Policy = ViverAppPolicies.Administrator)]
[ServiceFilter(typeof(AdministratorExceptionFilter)), ServiceFilter(typeof(AdministratorStepUpFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdministratorAccountsController(AdministratorAccountService service) : ControllerBase
{
    private ulong Actor => ulong.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    [HttpGet("{id:long}")]
    public Task<AdministratorAccountResponse> Get(ulong id, CancellationToken ct) => service.GetAsync(Actor, id, ct);
    [HttpPost, EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public async Task<ActionResult<AdministratorAccountSavedResponse>> Create(AdministratorAccountCreateRequest request, CancellationToken ct)
    {
        var result = await service.CreateAsync(Actor, request, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Account.Id }, result);
    }
    [HttpPut("{id:long}"), EnableRateLimiting(SecurityPolicyNames.AuthenticatedOperationRateLimit)]
    public Task<AdministratorAccountResponse> Update(ulong id, AdministratorAccountUpdateRequest request, CancellationToken ct) => service.UpdateAsync(Actor, id, request, ct);
}
