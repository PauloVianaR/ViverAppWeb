using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Infrastructure.Persistence.Generated;
using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;
using ViverApp.Security;

namespace ViverApp.Api.Features.Identity;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(
    UserManager<ViverAppUser> userManager,
    SignInManager<ViverAppUser> signInManager,
    ViverAppSessionService sessionService,
    IdentityChallengeService challengeService,
    IdentityAuditWriter auditWriter,
    IdentitySecurityOptions securityOptions,
    PasswordTimingProtector passwordTimingProtector,
    GoogleOnboardingProtector googleOnboardingProtector,
    ViverAppDbContext database,
    IAntiforgery antiforgery) : ControllerBase
{
    private const string GenericChallengeMessage =
        "Se a conta e o canal estiverem disponíveis, o código será enviado.";

    [AllowAnonymous]
    [HttpGet("antiforgery")]
    public ActionResult GetAntiforgeryToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { requestToken = tokens.RequestToken });
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.PublicFormRateLimit)]
    [HttpPost("register")]
    public async Task<ActionResult<ChallengeAcceptedResponse>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            await AddEnumerationDelayAsync(cancellationToken);
            return Accepted(new ChallengeAcceptedResponse(
                IdentityChallengeService.CreateOpaqueRequestId(),
                GenericChallengeMessage));
        }

        var email = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : IdentifierNormalizer.NormalizeEmail(request.Email);
        var phone = IdentifierNormalizer.NormalizePhone(request.Phone);
        if ((request.Email is not null && email is null)
            || phone is null
            || (request.VerificationChannel == "email" && email is null)
            || !await ValidateRegistrationAsync(
                request.RoleCode,
                request.TaxId,
                request.BirthDate,
                request.TermsAccepted,
                request.Address,
                request.Doctor,
                cancellationToken))
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            return InvalidRequest(new Dictionary<string, string[]>
            {
                ["contact"] = ["Informe telefone no formato E.164 e, quando escolhido, um e-mail válido."],
            });
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = new ViverAppUser
        {
            UserName = email ?? phone,
            NormalizedUserName = email ?? phone,
            RoleCode = request.RoleCode,
            StatusCode = "pending_confirmation",
            FullName = request.FullName.Trim(),
            Email = request.Email?.Trim(),
            NormalizedEmail = email,
            PhoneNumber = phone,
            TaxId = request.TaxId,
            BirthDate = request.BirthDate.ToDateTime(TimeOnly.MinValue),
            PreferredRecoveryChannel = request.VerificationChannel,
        };
        var created = await userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (created.Errors.Any(error => error.Code == "DuplicateAccount"))
            {
                await AddEnumerationDelayAsync(cancellationToken);
                return Accepted(new ChallengeAcceptedResponse(
                    IdentityChallengeService.CreateOpaqueRequestId(),
                    GenericChallengeMessage));
            }

            return InvalidRequest(ToValidationErrors(created));
        }

        try
        {
            await AddRegistrationDetailsAsync(
                user.Id,
                request.RoleCode,
                request.TaxId,
                request.BirthDate,
                request.Address,
                request.Doctor,
                "local",
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Não foi possível concluir o cadastro com os dados informados.",
            });
        }

        var destination = request.VerificationChannel == "email" ? email! : phone;
        var requestId = await challengeService.CreateAsync(
            user,
            "contact_verification",
            request.VerificationChannel,
            destination,
            cancellationToken);
        await auditWriter.WriteAsync(
            "identity.account_registered",
            user.Id,
            new Dictionary<string, string> { ["channel"] = request.VerificationChannel },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Accepted(new ChallengeAcceptedResponse(requestId, GenericChallengeMessage));
    }

    [AllowAnonymous]
    [HttpGet("registration/options")]
    public async Task<ActionResult<RegistrationOptionsResponse>> RegistrationOptions(
        CancellationToken cancellationToken)
    {
        var specialties = await database.Specialties.AsNoTracking()
            .Where(item => item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => new RegistrationOption(item.Id, item.Name))
            .ToListAsync(cancellationToken);
        return Ok(new RegistrationOptionsResponse(LegalDocumentVersions.Current, LegalDocumentVersions.Current, specialties));
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("contact/resend")]
    public Task<ActionResult<ChallengeAcceptedResponse>> ResendContactVerification(
        [FromBody] IdentifierChallengeRequest request,
        CancellationToken cancellationToken)
    {
        return RequestChallengeAsync(
            request,
            "contact_verification",
            requireVerifiedContact: false,
            cancellationToken,
            requiredStatus: "pending_confirmation");
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("contact/verify")]
    public async Task<ActionResult> VerifyContact(
        [FromBody] ChallengeCodeRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var verification = await challengeService.VerifyAsync(
            request.RequestId,
            "contact_verification",
            request.Code,
            cancellationToken);
        if (!verification.Succeeded || verification.AccountId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvalidCode();
        }

        var user = await userManager.FindByIdAsync(verification.AccountId.Value.ToString());
        if (user is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvalidCode();
        }

        if (verification.Channel == "email")
        {
            user.EmailConfirmed = true;
        }
        else
        {
            user.PhoneNumberConfirmed = true;
        }

        user.StatusCode = user.RoleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist or ViverAppRoles.Manager
            ? "pending_approval"
            : "active";
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Não foi possível confirmar a conta.");
        }

        await auditWriter.WriteAsync(
            "identity.contact_verified",
            user.Id,
            new Dictionary<string, string> { ["channel"] = verification.Channel! },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("login/password")]
    public async Task<ActionResult<AuthenticationResponse>> PasswordLogin(
        [FromBody] PasswordLoginRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = IdentifierNormalizer.NormalizeIdentifier(request.Identifier);
        var user = normalized is null ? null : await userManager.FindByNameAsync(normalized);
        if (user is null)
        {
            passwordTimingProtector.VerifyDummy(request.Password);
            await AddEnumerationDelayAsync(cancellationToken);
            return InvalidCredentials();
        }

        if (user.StatusCode != "active" || await userManager.IsLockedOutAsync(user))
        {
            passwordTimingProtector.VerifyDummy(request.Password);
            await AddEnumerationDelayAsync(cancellationToken);
            return InvalidCredentials();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            await auditWriter.WriteAsync("identity.login_failed", user.Id, null, cancellationToken);
            await AddEnumerationDelayAsync(cancellationToken);
            return InvalidCredentials();
        }

        await userManager.ResetAccessFailedCountAsync(user);
        var twoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user);
        if (twoFactorEnabled)
        {
            if (string.IsNullOrWhiteSpace(request.TotpCode)
                && string.IsNullOrWhiteSpace(request.RecoveryCode))
            {
                await sessionService.SignInAsync(
                    user,
                    "password",
                    mfaSatisfied: false,
                    persistent: false,
                    cancellationToken);
                return Ok(new AuthenticationResponse(true, user.RoleCode, true, false));
            }

            var factorValid = await VerifyMfaCodeAsync(user, request.TotpCode, request.RecoveryCode);
            if (!factorValid)
            {
                await userManager.AccessFailedAsync(user);
                await auditWriter.WriteAsync(
                    "identity.mfa_challenge_failed",
                    user.Id,
                    null,
                    cancellationToken);
                await AddEnumerationDelayAsync(cancellationToken);
                return InvalidCredentials();
            }
        }

        var enrollmentRequired = user.RoleCode == ViverAppRoles.Administrator && !twoFactorEnabled;
        await sessionService.SignInAsync(
            user,
            "password",
            !enrollmentRequired,
            request.RememberMe && !enrollmentRequired,
            cancellationToken);
        await auditWriter.WriteAsync("identity.login_succeeded", user.Id, null, cancellationToken);
        return Ok(new AuthenticationResponse(
            true,
            user.RoleCode,
            false,
            enrollmentRequired));
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("login/code/request")]
    public Task<ActionResult<ChallengeAcceptedResponse>> RequestLoginCode(
        [FromBody] IdentifierChallengeRequest request,
        CancellationToken cancellationToken)
    {
        return RequestChallengeAsync(request, "login", requireVerifiedContact: true, cancellationToken);
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("login/code/complete")]
    public async Task<ActionResult<AuthenticationResponse>> CompleteCodeLogin(
        [FromBody] ChallengeCodeRequest request,
        CancellationToken cancellationToken)
    {
        var verification = await challengeService.VerifyAsync(
            request.RequestId,
            "login",
            request.Code,
            cancellationToken);
        if (!verification.Succeeded || verification.AccountId is null)
        {
            return InvalidCode();
        }

        var user = await userManager.FindByIdAsync(verification.AccountId.Value.ToString());
        if (user is null || user.StatusCode != "active" || await userManager.IsLockedOutAsync(user))
        {
            return InvalidCode();
        }

        var twoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user);
        var requiresMfa = twoFactorEnabled || user.RoleCode == ViverAppRoles.Administrator;
        await sessionService.SignInAsync(
            user,
            verification.Channel == "sms" ? "sms_code" : "email_code",
            mfaSatisfied: !requiresMfa,
            persistent: false,
            cancellationToken);
        await auditWriter.WriteAsync("identity.login_code_accepted", user.Id, null, cancellationToken);
        return Ok(new AuthenticationResponse(
            true,
            user.RoleCode,
            requiresMfa,
            user.RoleCode == ViverAppRoles.Administrator && !twoFactorEnabled));
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("password/recovery/request")]
    public Task<ActionResult<ChallengeAcceptedResponse>> RequestPasswordRecovery(
        [FromBody] IdentifierChallengeRequest request,
        CancellationToken cancellationToken)
    {
        return RequestChallengeAsync(
            request,
            "password_reset",
            requireVerifiedContact: true,
            cancellationToken);
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("password/recovery/complete")]
    public async Task<ActionResult> CompletePasswordRecovery(
        [FromBody] PasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var verification = await challengeService.VerifyAsync(
            request.RequestId,
            "password_reset",
            request.Code,
            cancellationToken);
        if (!verification.Succeeded || verification.AccountId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvalidCode();
        }

        var user = await userManager.FindByIdAsync(verification.AccountId.Value.ToString());
        if (user is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvalidCode();
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!reset.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return InvalidRequest(ToValidationErrors(reset));
        }

        await sessionService.RevokeAllAsync(user.Id, "password_reset", cancellationToken);
        await auditWriter.WriteAsync("identity.password_reset", user.Id, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ViverAppPolicies.MfaEnrollment)]
    [EnableRateLimiting(SecurityPolicyNames.WriteRateLimit)]
    [HttpPost("logout")]
    public async Task<ActionResult> Logout(CancellationToken cancellationToken)
    {
        await sessionService.RevokeCurrentAsync("logout", cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ViverAppPolicies.MfaEnrollment)]
    [HttpGet("me")]
    public async Task<ActionResult<CurrentAccountResponse>> Me()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new CurrentAccountResponse(
            user.Id.ToString(),
            user.RoleCode,
            user.StatusCode,
            user.FullName,
            user.EmailConfirmed,
            user.PhoneNumberConfirmed,
            User.HasClaim(ViverAppClaimTypes.MfaSatisfied, bool.TrueString),
            RoleDestination(user.RoleCode)));
    }

    [Authorize(Policy = ViverAppPolicies.MfaEnrollment)]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("mfa/authenticator/begin")]
    public async Task<ActionResult> BeginAuthenticatorSetup()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "O autenticador já está ativo.",
            });
        }

        var authenticationMethod = User.FindFirstValue(ViverAppClaimTypes.AuthenticationMethod) ?? "password";
        await userManager.ResetAuthenticatorKeyAsync(user);
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

        // ResetAuthenticatorKeyAsync rotates the security stamp. Reissue the restricted
        // enrollment session so the following confirmation request remains authenticated.
        await sessionService.RevokeCurrentAsync("mfa_enrollment_started", HttpContext.RequestAborted);
        await sessionService.SignInAsync(
            user,
            authenticationMethod,
            mfaSatisfied: false,
            persistent: false,
            HttpContext.RequestAborted);

        var label = user.Email ?? user.PhoneNumber ?? user.Id.ToString();
        var uri = $"otpauth://totp/ViverApp:{Uri.EscapeDataString(label)}?secret={Uri.EscapeDataString(key)}&issuer=ViverApp&digits=6";
        return Ok(new { sharedKey = key, authenticatorUri = uri });
    }

    [Authorize(Policy = ViverAppPolicies.MfaEnrollment)]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("mfa/authenticator/complete")]
    public async Task<ActionResult> CompleteAuthenticatorSetup(
        [FromBody] MfaCodeRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || string.IsNullOrWhiteSpace(request.TotpCode))
        {
            return InvalidCode();
        }

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "O autenticador já está ativo.",
            });
        }

        var valid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            IdentifierNormalizer.NormalizeCode(request.TotpCode));
        if (!valid)
        {
            return InvalidCode();
        }

        var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Não foi possível ativar o MFA.");
        }

        var recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray() ?? [];
        await sessionService.RevokeCurrentAsync("mfa_enabled", cancellationToken);
        var method = User.FindFirstValue(ViverAppClaimTypes.AuthenticationMethod) ?? "password";
        await sessionService.SignInAsync(user, method, mfaSatisfied: true, persistent: false, cancellationToken);
        await auditWriter.WriteAsync("identity.mfa_enabled", user.Id, null, cancellationToken);
        return Ok(new { recoveryCodes });
    }

    [Authorize(Policy = ViverAppPolicies.MfaEnrollment)]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("mfa/complete-signin")]
    public async Task<ActionResult<AuthenticationResponse>> CompleteMfaSignIn(
        [FromBody] MfaCodeRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !await userManager.GetTwoFactorEnabledAsync(user))
        {
            return InvalidCode();
        }

        if (!await VerifyMfaCodeAsync(user, request.TotpCode, request.RecoveryCode))
        {
            await userManager.AccessFailedAsync(user);
            await auditWriter.WriteAsync(
                "identity.mfa_challenge_failed",
                user.Id,
                null,
                cancellationToken);
            return InvalidCode();
        }

        var method = User.FindFirstValue(ViverAppClaimTypes.AuthenticationMethod) ?? "password";
        await sessionService.RevokeCurrentAsync("mfa_completed", cancellationToken);
        await sessionService.SignInAsync(user, method, mfaSatisfied: true, persistent: false, cancellationToken);
        await auditWriter.WriteAsync("identity.mfa_challenge_succeeded", user.Id, null, cancellationToken);
        return Ok(new AuthenticationResponse(true, user.RoleCode, false, false));
    }

    [Authorize(Policy = ViverAppPolicies.MfaSatisfied)]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("mfa/recovery-codes/reset")]
    public async Task<ActionResult> ResetRecoveryCodes(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null || !await userManager.GetTwoFactorEnabledAsync(user))
        {
            return BadRequest();
        }

        var recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))?.ToArray() ?? [];
        await auditWriter.WriteAsync("identity.recovery_codes_reset", user.Id, null, cancellationToken);
        return Ok(new { recoveryCodes });
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("google/register")]
    public async Task<ActionResult<RegistrationCompletedResponse>> CompleteGoogleRegistration(
        [FromBody] GoogleRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Website)
            || !googleOnboardingProtector.TryUnprotect(request.OnboardingToken, out var identity))
        {
            return InvalidRequest(new Dictionary<string, string[]>
            {
                ["onboardingToken"] = ["O cadastro Google expirou. Inicie novamente."],
            });
        }

        var phone = IdentifierNormalizer.NormalizePhone(request.Phone);
        var normalizedEmail = IdentifierNormalizer.NormalizeEmail(identity!.Email);
        if (phone is null
            || normalizedEmail is null
            || !await ValidateRegistrationAsync(
                identity.RoleCode,
                request.TaxId,
                request.BirthDate,
                request.TermsAccepted,
                request.Address,
                request.Doctor,
                cancellationToken))
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            return InvalidRequest(new Dictionary<string, string[]>
            {
                ["phone"] = ["Informe um telefone válido no formato E.164."],
            });
        }

        if (await userManager.FindByLoginAsync(GoogleDefaults.AuthenticationScheme, identity.ProviderSubject) is not null
            || await userManager.FindByEmailAsync(normalizedEmail) is not null)
        {
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Esta identidade Google já está vinculada. Entre na conta ou faça o vínculo explícito.",
            });
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var status = identity.RoleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist or ViverAppRoles.Manager
            ? "pending_approval"
            : "active";
        var user = new ViverAppUser
        {
            UserName = identity.Email,
            NormalizedUserName = normalizedEmail,
            RoleCode = identity.RoleCode,
            StatusCode = status,
            FullName = identity.FullName.Trim(),
            Email = identity.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            PhoneNumber = phone,
            TaxId = request.TaxId,
            BirthDate = request.BirthDate.ToDateTime(TimeOnly.MinValue),
            PreferredRecoveryChannel = "email",
        };
        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Não foi possível concluir o cadastro com os dados informados.",
            });
        }

        var login = new UserLoginInfo(
            GoogleDefaults.AuthenticationScheme,
            identity.ProviderSubject,
            GoogleDefaults.DisplayName);
        var linked = await userManager.AddLoginAsync(user, login);
        if (!linked.Succeeded)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict();
        }

        try
        {
            await AddRegistrationDetailsAsync(
                user.Id,
                identity.RoleCode,
                request.TaxId,
                request.BirthDate,
                request.Address,
                request.Doctor,
                "google",
                cancellationToken);
            await MarkGoogleEmailVerifiedAsync(
                user.Id,
                identity.ProviderSubject,
                identity.Email,
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Não foi possível concluir o cadastro com os dados informados.",
            });
        }

        await auditWriter.WriteAsync("identity.google_account_registered", user.Id, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var authenticated = status == "active";
        if (authenticated)
        {
            await sessionService.SignInAsync(user, "google", true, false, cancellationToken);
        }

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Ok(new RegistrationCompletedResponse(
            status,
            identity.RoleCode,
            authenticated,
            authenticated ? RoleDestination(identity.RoleCode) : "/acesso?estado=aguardando-aprovacao"));
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpGet("google/start")]
    public ActionResult StartGoogleLogin([FromQuery] string? role = null)
    {
        if (!securityOptions.GoogleEnabled)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Login Google ainda não está configurado.");
        }

        if (role is not null
            && role is not ViverAppRoles.Patient and not ViverAppRoles.Doctor and not ViverAppRoles.Psychologist and not ViverAppRoles.Manager)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "O tipo de cadastro informado não está disponível.",
            });
        }

        var properties = signInManager.ConfigureExternalAuthenticationProperties(
            GoogleDefaults.AuthenticationScheme,
            Url.ActionLink(nameof(CompleteGoogleLogin))!);
        if (role is not null)
        {
            properties.Items["registration_role"] = role;
        }

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpGet("google/complete")]
    public async Task<ActionResult> CompleteGoogleLogin(CancellationToken cancellationToken)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (!IsVerifiedGoogleIdentity(info, out var email))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Redirect(securityOptions.BuildWebReturnUrl("google_failed"));
        }

        var user = await userManager.FindByLoginAsync(info!.LoginProvider, info.ProviderKey);
        if (user is null)
        {
            var registrationRole = info.AuthenticationProperties is { } authenticationProperties
                && authenticationProperties.Items.TryGetValue("registration_role", out var storedRole)
                    ? storedRole
                    : null;
            if (registrationRole is not ViverAppRoles.Patient
                and not ViverAppRoles.Doctor
                and not ViverAppRoles.Psychologist
                and not ViverAppRoles.Manager)
            {
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return Redirect(securityOptions.BuildWebReturnUrl("google_registration_role_required"));
            }

            var normalizedEmail = IdentifierNormalizer.NormalizeEmail(email);
            var existing = normalizedEmail is null
                ? null
                : await userManager.FindByEmailAsync(normalizedEmail);
            if (existing is not null)
            {
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return Redirect(securityOptions.BuildWebReturnUrl("explicit_link_required"));
            }

            var token = googleOnboardingProtector.Protect(
                info.ProviderKey,
                email!,
                info.Principal.FindFirstValue(ClaimTypes.Name) ?? "Usuário",
                registrationRole);
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            var returnUrl = securityOptions.BuildWebReturnUrl("google_onboarding", "onboarding", token);
            return Redirect(QueryHelpers.AddQueryString(returnUrl, "role", registrationRole));
        }

        await MarkGoogleEmailVerifiedAsync(user.Id, info.ProviderKey, email!, cancellationToken);

        if (user.StatusCode != "active")
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Redirect(securityOptions.BuildWebReturnUrl("account_unavailable"));
        }

        var twoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(user);
        var requiresMfa = twoFactorEnabled || user.RoleCode == ViverAppRoles.Administrator;
        await sessionService.SignInAsync(user, "google", !requiresMfa, false, cancellationToken);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        await auditWriter.WriteAsync("identity.google_login_succeeded", user.Id, null, cancellationToken);
        return Redirect(securityOptions.BuildWebReturnUrl(
            requiresMfa
                ? twoFactorEnabled ? "mfa_required" : "mfa_enrollment_required"
                : "success"));
    }

    [Authorize]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpGet("google/link/start")]
    public async Task<ActionResult> StartGoogleLink([FromServices] PatientExperience.RecentAuthentication recent, CancellationToken cancellationToken)
    {
        if (!await recent.IsRecentAsync(User, cancellationToken))
            return Redirect(securityOptions.BuildWebReturnUrl("reauthentication_required"));
        if (!securityOptions.GoogleEnabled)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var userId = userManager.GetUserId(User);
        var properties = signInManager.ConfigureExternalAuthenticationProperties(
            GoogleDefaults.AuthenticationScheme,
            Url.ActionLink(nameof(CompleteGoogleLink))!,
            userId);
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [Authorize]
    [HttpGet("google/link/complete")]
    public async Task<ActionResult> CompleteGoogleLink(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        var info = user is null
            ? null
            : await signInManager.GetExternalLoginInfoAsync(user.Id.ToString());
        if (user is null || !IsVerifiedGoogleIdentity(info, out var email))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Redirect(securityOptions.BuildWebReturnUrl("google_link_failed"));
        }

        var owner = await userManager.FindByLoginAsync(info!.LoginProvider, info.ProviderKey);
        if (owner is not null && owner.Id != user.Id)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return Redirect(securityOptions.BuildWebReturnUrl("google_link_failed"));
        }

        var result = owner is null ? await userManager.AddLoginAsync(user, info) : IdentityResult.Success;
        if (result.Succeeded)
        {
            await MarkGoogleEmailVerifiedAsync(user.Id, info.ProviderKey, email!, cancellationToken);
            await auditWriter.WriteAsync("identity.google_linked", user.Id, null, cancellationToken);
        }

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Redirect(securityOptions.BuildWebReturnUrl(
            result.Succeeded ? "google_linked" : "google_link_failed"));
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("passkeys/options")]
    public async Task<ActionResult> GetPasskeyRequestOptions([FromBody] PasskeyOptionsRequest request)
    {
        var normalized = IdentifierNormalizer.NormalizeIdentifier(request.Identifier);
        var user = normalized is null ? null : await userManager.FindByNameAsync(normalized);
        var optionsJson = await signInManager.MakePasskeyRequestOptionsAsync(user);
        return Content(optionsJson, "application/json");
    }

    [AllowAnonymous]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("passkeys/login")]
    public async Task<ActionResult<AuthenticationResponse>> PasskeyLogin(
        [FromBody] PasskeyCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var assertion = await signInManager.PerformPasskeyAssertionAsync(request.CredentialJson);
        if (!assertion.Succeeded || assertion.User is null || assertion.User.StatusCode != "active")
        {
            return InvalidCredentials();
        }

        var update = await userManager.AddOrUpdatePasskeyAsync(assertion.User, assertion.Passkey!);
        if (!update.Succeeded)
        {
            return InvalidCredentials();
        }

        var twoFactorEnabled = await userManager.GetTwoFactorEnabledAsync(assertion.User);
        var requiresMfa = twoFactorEnabled || assertion.User.RoleCode == ViverAppRoles.Administrator;
        await sessionService.SignInAsync(
            assertion.User,
            "passkey",
            !requiresMfa,
            request.RememberMe && !requiresMfa,
            cancellationToken);
        await auditWriter.WriteAsync(
            "identity.passkey_login_succeeded",
            assertion.User.Id,
            null,
            cancellationToken);
        return Ok(new AuthenticationResponse(
            true,
            assertion.User.RoleCode,
            requiresMfa,
            assertion.User.RoleCode == ViverAppRoles.Administrator && !twoFactorEnabled));
    }

    [Authorize]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpGet("passkeys")]
    public async Task<ActionResult<IReadOnlyList<PasskeyResponse>>> ListPasskeys(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Unauthorized();
        var items = await database.AccountPasskeys.AsNoTracking()
            .Where(item => item.AccountId == user.Id)
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => new PasskeyResponse(
                WebEncoders.Base64UrlEncode(item.CredentialId),
                item.DisplayName ?? "Chave de acesso",
                item.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    [Authorize]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("passkeys/create/options")]
    public async Task<ActionResult> GetPasskeyCreationOptions()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var optionsJson = await signInManager.MakePasskeyCreationOptionsAsync(new PasskeyUserEntity
        {
            Id = user.Id.ToString(),
            Name = user.UserName ?? user.Id.ToString(),
            DisplayName = user.FullName,
        });
        return Content(optionsJson, "application/json");
    }

    [Authorize]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("passkeys/create/complete")]
    public async Task<ActionResult> CompletePasskeyCreation(
        [FromBody] PasskeyCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var existing = await userManager.GetPasskeysAsync(user);
        if (existing.Count >= 10)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Limite de passkeys atingido.");
        }

        var attestation = await signInManager.PerformPasskeyAttestationAsync(request.CredentialJson);
        if (!attestation.Succeeded || attestation.Passkey is null)
        {
            return BadRequest(new ProblemDetails { Status = 400, Title = "Passkey inválida." });
        }

        attestation.Passkey.Name = string.IsNullOrWhiteSpace(request.DisplayName)
            ? "Passkey"
            : request.DisplayName.Trim();
        var result = await userManager.AddOrUpdatePasskeyAsync(user, attestation.Passkey);
        if (!result.Succeeded)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Não foi possível cadastrar a passkey.");
        }

        await auditWriter.WriteAsync("identity.passkey_added", user.Id, null, cancellationToken);
        return NoContent();
    }

    [Authorize]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpDelete("passkeys/{credentialId}")]
    public async Task<ActionResult> RemovePasskey(
        string credentialId,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        byte[] decoded;
        try
        {
            decoded = WebEncoders.Base64UrlDecode(credentialId);
        }
        catch (FormatException)
        {
            return BadRequest();
        }

        var result = await userManager.RemovePasskeyAsync(user, decoded);
        if (!result.Succeeded)
        {
            return BadRequest();
        }

        await auditWriter.WriteAsync("identity.passkey_removed", user.Id, null, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ViverAppPolicies.MfaSatisfied)]
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<SessionResponse>>> ListSessions(
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(await sessionService.ListAsync(user.Id, cancellationToken));
    }

    [Authorize(Policy = ViverAppPolicies.MfaSatisfied)]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<ActionResult> RevokeSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        var revoked = await sessionService.RevokeAsync(
            user.Id,
            sessionId,
            "user_revoked",
            cancellationToken);
        if (!revoked)
        {
            return NotFound();
        }

        await auditWriter.WriteAsync("identity.session_revoked", user.Id, null, cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = ViverAppPolicies.MfaSatisfied)]
    [EnableRateLimiting(SecurityPolicyNames.SensitiveRateLimit)]
    [HttpPost("sessions/revoke-all")]
    public async Task<ActionResult> RevokeAllSessions(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        await sessionService.RevokeAllAsync(user.Id, "user_revoked_all", cancellationToken);
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        await auditWriter.WriteAsync("identity.sessions_revoked_all", user.Id, null, cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("result")]
    public ActionResult GetAuthenticationResult([FromQuery] string result)
    {
        return Ok(new { result });
    }

    private async Task<bool> ValidateRegistrationAsync(
        string roleCode,
        string taxId,
        DateOnly birthDate,
        bool termsAccepted,
        RegistrationAddressRequest? address,
        DoctorRegistrationRequest? doctor,
        CancellationToken cancellationToken)
    {
        if (roleCode is not (ViverAppRoles.Patient or ViverAppRoles.Doctor or ViverAppRoles.Psychologist or ViverAppRoles.Manager))
        {
            ModelState.AddModelError(nameof(roleCode), "O papel escolhido não permite cadastro público.");
        }

        if (!BrazilianDocumentValidator.IsValidCpf(taxId))
        {
            ModelState.AddModelError(nameof(taxId), "Informe um CPF válido.");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (birthDate < new DateOnly(1900, 1, 1) || birthDate > today)
        {
            ModelState.AddModelError(nameof(birthDate), "Informe uma data de nascimento válida.");
        }
        else if (roleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist or ViverAppRoles.Manager
            && birthDate > today.AddYears(-18))
        {
            ModelState.AddModelError(nameof(birthDate), "Profissionais clínicos e gestores devem ser maiores de 18 anos.");
        }

        if (!termsAccepted)
        {
            ModelState.AddModelError(nameof(termsAccepted), "Confirme o aceite dos Termos de uso e a leitura da Política de Privacidade.");
        }

        if (roleCode == ViverAppRoles.Patient)
        {
            if (address is null)
            {
                ModelState.AddModelError(nameof(address), "O endereço completo é obrigatório para pacientes.");
            }

            if (doctor is not null)
            {
                ModelState.AddModelError(nameof(doctor), "Dados médicos não são aceitos para pacientes.");
            }
        }
        else if (address is not null)
        {
            ModelState.AddModelError(nameof(address), "Endereço residencial não é aceito neste cadastro profissional.");
        }

        if (roleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist)
        {
            if (doctor is null)
            {
                ModelState.AddModelError(nameof(doctor), "Os dados profissionais são obrigatórios.");
            }
            else if (roleCode == ViverAppRoles.Doctor && doctor.ProfessionalTitle is not ("Dr." or "Dra.")
                || roleCode == ViverAppRoles.Psychologist && doctor.ProfessionalTitle is not ("Psic." or "Psicóloga"))
            {
                ModelState.AddModelError(nameof(doctor.ProfessionalTitle), "O título profissional deve corresponder ao papel selecionado.");
            }
            else if (!await database.Specialties.AsNoTracking().AnyAsync(
                item => item.Id == doctor.PrimarySpecialtyId && item.IsActive,
                cancellationToken))
            {
                ModelState.AddModelError(nameof(doctor.PrimarySpecialtyId), "A especialidade informada não está disponível.");
            }
        }
        else if (doctor is not null)
        {
            ModelState.AddModelError(nameof(doctor), "Dados profissionais são aceitos somente no cadastro de Médico ou Psicólogo.");
        }

        return ModelState.IsValid;
    }

    private async Task AddRegistrationDetailsAsync(
        ulong accountId,
        string roleCode,
        string taxId,
        DateOnly birthDate,
        RegistrationAddressRequest? address,
        DoctorRegistrationRequest? doctor,
        string sourceCode,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        database.AccountConsents.Add(new AccountConsent
        {
            AccountId = accountId,
            TermsVersion = LegalDocumentVersions.Current,
            PrivacyVersion = LegalDocumentVersions.Current,
            AcceptedAtUtc = now,
            SourceCode = sourceCode,
        });

        if (roleCode == ViverAppRoles.Patient)
        {
            database.PatientProfiles.Add(new PatientProfile
            {
                AccountId = accountId,
                TaxId = taxId,
                BirthDate = birthDate.ToDateTime(TimeOnly.MinValue),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            database.AccountAddresses.Add(new AccountAddress
            {
                AccountId = accountId,
                PostalCode = address!.PostalCode,
                Street = address.Street.Trim(),
                Number = address.Number.Trim(),
                Complement = string.IsNullOrWhiteSpace(address.Complement) ? null : address.Complement.Trim(),
                District = address.District.Trim(),
                City = address.City.Trim(),
                StateCode = address.StateCode,
                UpdatedAtUtc = now,
            });
        }
        else if (roleCode is ViverAppRoles.Doctor or ViverAppRoles.Psychologist)
        {
            database.ProfessionalProfiles.Add(new ProfessionalProfile
            {
                AccountId = accountId,
                LicenseTypeCode = roleCode == ViverAppRoles.Psychologist ? "CRP" : "CRM",
                ProfessionalTitle = doctor!.ProfessionalTitle,
                LicenseStateCode = doctor.LicenseStateCode,
                LicenseNumber = doctor.LicenseNumber.Trim(),
                YearsExperience = doctor.YearsExperience,
                DefaultAppointmentDurationMinutes = 30,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = 1,
            });
            database.ProfessionalSpecialties.Add(new ProfessionalSpecialty
            {
                ProfessionalAccountId = accountId,
                SpecialtyId = doctor.PrimarySpecialtyId,
                IsPrimary = true,
            });
            database.ProfessionalPreferences.Add(new ProfessionalPreference
            {
                ProfessionalAccountId = accountId,
                EmailEnabled = true,
                SmsEnabled = true,
                OnlineEnabled = true,
                MaxOnlineDaily = 8,
                MaxInPersonDaily = 16,
                UpdatedAtUtc = now,
                RowVersion = 1,
            });
            var activeServices = await database.AppointmentTypes.AsNoTracking().Where(item => item.IsActive).Select(item => item.Id).ToArrayAsync(cancellationToken);
            database.ProfessionalServices.AddRange(activeServices.Select(id => new ProfessionalService
            {
                ProfessionalAccountId = accountId,
                AppointmentTypeId = id,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = 1,
            }));
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private static string RoleDestination(string roleCode) => roleCode switch
    {
        ViverAppRoles.Patient => "/paciente",
        ViverAppRoles.Doctor => "/medico",
        ViverAppRoles.Psychologist => "/psicologo",
        ViverAppRoles.Manager => "/gestao",
        ViverAppRoles.Administrator => "/administracao",
        _ => "/acesso",
    };

    private async Task<ActionResult<ChallengeAcceptedResponse>> RequestChallengeAsync(
        IdentifierChallengeRequest request,
        string purpose,
        bool requireVerifiedContact,
        CancellationToken cancellationToken,
        string requiredStatus = "active")
    {
        var normalized = IdentifierNormalizer.NormalizeIdentifier(request.Identifier);
        var user = normalized is null ? null : await userManager.FindByNameAsync(normalized);
        var destination = request.Channel == "email"
            ? user?.NormalizedEmail
            : user?.PhoneNumber;
        var verified = request.Channel == "email"
            ? user?.EmailConfirmed == true
            : user?.PhoneNumberConfirmed == true;

        Guid requestId;
        if (user is not null
            && user.StatusCode == requiredStatus
            && destination is not null
            && (!requireVerifiedContact || verified))
        {
            requestId = await challengeService.CreateAsync(
                user,
                purpose,
                request.Channel,
                destination,
                cancellationToken);
        }
        else
        {
            requestId = IdentityChallengeService.CreateOpaqueRequestId();
        }

        await AddEnumerationDelayAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Accepted(new ChallengeAcceptedResponse(requestId, GenericChallengeMessage));
    }

    private async Task<bool> VerifyMfaCodeAsync(
        ViverAppUser user,
        string? totpCode,
        string? recoveryCode)
    {
        if (!string.IsNullOrWhiteSpace(totpCode))
        {
            return await userManager.VerifyTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultAuthenticatorProvider,
                IdentifierNormalizer.NormalizeCode(totpCode));
        }

        return !string.IsNullOrWhiteSpace(recoveryCode)
            && (await userManager.RedeemTwoFactorRecoveryCodeAsync(
                user,
                IdentifierNormalizer.NormalizeCode(recoveryCode))).Succeeded;
    }

    private async Task MarkGoogleEmailVerifiedAsync(
        ulong accountId,
        string providerSubject,
        string email,
        CancellationToken cancellationToken)
    {
        var login = await database.ExternalLogins.SingleAsync(
            item => item.AccountId == accountId
                && item.ProviderCode == "google"
                && item.ProviderSubject == providerSubject,
            cancellationToken);
        login.ProviderEmail = email;
        login.ProviderEmailVerified = true;
        login.LastUsedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
    }

    private static bool IsVerifiedGoogleIdentity(
        ExternalLoginInfo? info,
        out string? email)
    {
        email = info?.Principal.FindFirstValue(ClaimTypes.Email);
        var verified = info?.Principal.FindFirstValue("email_verified");
        return info is not null
            && info.LoginProvider == GoogleDefaults.AuthenticationScheme
            && IdentifierNormalizer.NormalizeEmail(email) is not null
            && bool.TryParse(verified, out var emailVerified)
            && emailVerified;
    }

    private static Dictionary<string, string[]> ToValidationErrors(IdentityResult result)
    {
        return result.Errors
            .GroupBy(error => error.Code)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => error.Description).ToArray());
    }

    private static BadRequestObjectResult InvalidRequest(Dictionary<string, string[]> errors)
    {
        return new BadRequestObjectResult(new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Um ou mais dados são inválidos.",
        });
    }

    private static UnauthorizedObjectResult InvalidCredentials()
    {
        return new UnauthorizedObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Credenciais inválidas.",
        });
    }

    private static BadRequestObjectResult InvalidCode()
    {
        return new BadRequestObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Código inválido ou expirado.",
        });
    }

    private static Task AddEnumerationDelayAsync(CancellationToken cancellationToken)
    {
        return Task.Delay(RandomNumberGenerator.GetInt32(35, 76), cancellationToken);
    }
}
