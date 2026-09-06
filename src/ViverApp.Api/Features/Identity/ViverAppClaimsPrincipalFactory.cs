using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ViverApp.Api.Features.Identity;

public sealed class ViverAppClaimsPrincipalFactory(
    UserManager<ViverAppUser> userManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ViverAppUser>(userManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ViverAppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(ViverAppClaimTypes.RoleCode, user.RoleCode));
        identity.AddClaim(new Claim(identity.RoleClaimType, user.RoleCode));
        return identity;
    }
}
