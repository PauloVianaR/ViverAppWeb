using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;
using ViverApp.Api.Features.AdministratorExperience;

namespace ViverApp.Api.Features.ClinicAdministration;

[ApiController]
[Route("api/v1/users")]
[Authorize]
[EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
[ServiceFilter(typeof(AdministratorStepUpFilter))]
public sealed class UsersController(
    ViverAppDbContext database,
    IdentityAuditWriter auditWriter) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = ViverAppPolicies.Management)]
    public async Task<ActionResult<PagedResponse<UserResponse>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!ValidateFilters(page, pageSize, search, role, status))
        {
            return ValidationProblem(ModelState);
        }

        var query = database.Accounts.AsNoTracking();
        if (User.IsInRole(ViverAppRoles.Manager) && !User.IsInRole(ViverAppRoles.Administrator))
        {
            query = query.Where(item => item.RoleCode != ViverAppRoles.Administrator);
        }

        var term = ClinicAdministrationSupport.OptionalText(search);
        if (term is not null)
        {
            query = query.WhereNameEmailOrPhoneContains(term);
        }

        if (role is not null)
        {
            query = query.Where(item => item.RoleCode == role);
        }

        if (status is not null)
        {
            query = query.Where(item => item.StatusCode == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var entities = await query.OrderBy(item => item.FullName)
            .ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(new PagedResponse<UserResponse>(
            entities.Select(ToResponse).ToArray(),
            page,
            pageSize,
            total));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<UserResponse>> GetById(ulong id, CancellationToken cancellationToken)
    {
        var account = await database.Accounts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        return CanView(account) ? Ok(ToResponse(account)) : Forbid();
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<UserResponse>> Update(
        ulong id,
        [FromBody] UserUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var account = await database.Accounts.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (!CanEdit(account))
        {
            return Forbid();
        }

        ClinicAdministrationSupport.SetConcurrency(database, account, nameof(Account.RowVersion), request.RowVersion);
        account.FullName = request.FullName.Trim();
        account.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        await Audit("user.updated", id, null, cancellationToken);
        return Ok(ToResponse(account));
    }

    [HttpPost("{id:long}/status")]
    [Authorize(Policy = ViverAppPolicies.Administrator)]
    public async Task<ActionResult<UserResponse>> ChangePatientStatus(
        ulong id,
        [FromBody] PatientStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DecisionCode == "blocked" && (request.Reason?.Trim().Length ?? 0) < 5)
        {
            ModelState.AddModelError(nameof(request.Reason), "A justificativa deve ter pelo menos cinco caracteres.");
            return ValidationProblem(ModelState);
        }

        var account = await database.Accounts.SingleOrDefaultAsync(
            item => item.Id == id && item.RoleCode == ViverAppRoles.Patient,
            cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (request.DecisionCode == "reactivated" && !account.EmailVerified && !account.PhoneVerified)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "O usuário precisa confirmar ao menos um contato antes da reativação.");
        }

        if (request.DecisionCode == "blocked" && account.StatusCode != "active"
            || request.DecisionCode == "reactivated" && account.StatusCode != "blocked")
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A mudança de status não é válida para o estado atual do usuário.");
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        ClinicAdministrationSupport.SetConcurrency(database, account, nameof(Account.RowVersion), request.RowVersion);
        account.StatusCode = request.DecisionCode == "blocked" ? "blocked" : "active";
        account.UpdatedAtUtc = DateTime.UtcNow;
        if (request.DecisionCode == "blocked")
        {
            account.SecurityStamp = RandomNumberGenerator.GetBytes(32);
            await database.AuthSessions
                .Where(item => item.AccountId == id && item.RevokedAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(item => item.RevokedAtUtc, DateTime.UtcNow)
                        .SetProperty(item => item.RevokeReasonCode, "account_status"),
                    cancellationToken);
        }

        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await auditWriter.WriteAsync(
                "user.status_changed",
                ActorId,
                "account",
                id.ToString(),
                new Dictionary<string, string> { ["decision"] = request.DecisionCode },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return ClinicAdministrationSupport.ConcurrencyProblem(this);
        }

        return Ok(ToResponse(account));
    }

    private ulong ActorId => ClinicAdministrationSupport.RequireActorId(User);

    private bool CanView(Account account) =>
        User.IsInRole(ViverAppRoles.Administrator)
        || ActorId == account.Id
        || User.IsInRole(ViverAppRoles.Manager) && account.RoleCode != ViverAppRoles.Administrator;

    private bool CanEdit(Account account) =>
        User.IsInRole(ViverAppRoles.Administrator) || ActorId == account.Id;

    private bool ValidateFilters(int page, int pageSize, string? search, string? role, string? status)
    {
        var valid = page >= 1
            && pageSize is >= 1 and <= 100
            && (search?.Length ?? 0) <= 200
            && (role is null || ViverAppRoles.All.Contains(role))
            && (status is null || status is "pending_confirmation" or "pending_approval" or "active" or "rejected" or "blocked");
        if (!valid)
        {
            ModelState.AddModelError("filters", "Filtros ou paginação inválidos.");
        }

        return valid;
    }

    private async Task Audit(
        string eventCode,
        ulong entityId,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken cancellationToken) =>
        await auditWriter.WriteAsync(
            eventCode,
            ActorId,
            "account",
            entityId.ToString(),
            data,
            cancellationToken);

    private static UserResponse ToResponse(Account item) => new(
        item.Id,
        item.FullName,
        item.RoleCode,
        item.StatusCode,
        item.Email,
        item.EmailVerified,
        item.PhoneE164,
        item.PhoneVerified,
        item.CreatedAtUtc,
        item.LastLoginAtUtc,
        item.RowVersion);
}
