using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;

namespace ViverApp.Api.Features.ClinicAdministration;

[ApiController]
[Route("api/v1/public/clinic")]
[AllowAnonymous]
public sealed class PublicClinicController(ViverAppDbContext database) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true)]
    public async Task<ActionResult<PublicClinicContactResponse>> Get(CancellationToken cancellationToken)
    {
        var clinic = await database.Clinics.AsNoTracking().Select(item => new PublicClinicContactResponse(
            item.LegalName, item.DisplayName, item.Email, item.PhoneE164, item.Street, item.Number,
            item.Complement, item.District, item.City, item.StateCode, item.PostalCode))
            .SingleOrDefaultAsync(cancellationToken);
        return clinic is null ? NotFound() : Ok(clinic);
    }
}

public sealed record PublicClinicContactResponse(string LegalName, string DisplayName, string? Email,
    string? PhoneE164, string? Street, string? Number, string? Complement, string? District,
    string? City, string? StateCode, string? PostalCode);
