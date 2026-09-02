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
        var email = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : IdentifierNormalizer.NormalizeEmail(request.Email);
        var phone = string.IsNullOrWhiteSpace(request.Phone)
            ? null
            : IdentifierNormalizer.NormalizePhone(request.Phone);
        if ((request.Email is not null && email is null)
            || (request.Phone is not null && phone is null)
            || (email is null && phone is null)
            || (request.VerificationChannel == "email" && email is null)
            || (request.VerificationChannel == "sms" && phone is null))
        {
            return InvalidRequest(new Dictionary<string, string[]>
            {
                ["contact"] = ["Informe um e-mail válido e/ou telefone no formato E.164."],
            });
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = new ViverAppUser
        {
            UserName = email ?? phone,
            NormalizedUserName = email ?? phone,
            RoleCode = ViverAppRoles.Patient,
            StatusCode = "pending_confirmation",
            FullName = request.FullName.Trim(),
            Email = request.Email?.Trim(),
            NormalizedEmail = email,
            PhoneNumber = phone,
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

        var destination = request.VerificationChannel == "email" ? email! : phone!;
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

        user.StatusCode = user.RoleCode is ViverAppRoles.Doctor or ViverAppRoles.Manager
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
            user.FullName,
            user.EmailConfirmed,
            user.PhoneNumberConfirmed,
            User.HasClaim(ViverAppClaimTypes.MfaSatisfied, bool.TrueString)));
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

        await userManager.ResetAuthenticatorKeyAsync(user);
        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError);
        }

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
    [HttpGet("google/start")]
    public ActionResult StartGoogleLogin()
    {
        if (!securityOptions.GoogleEnabled)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Login Google ainda não está configurado.");
        }

        var properties = signInManager.ConfigureExternalAuthenticationProperties(
            GoogleDefaults.AuthenticationScheme,
            Url.ActionLink(nameof(CompleteGoogleLogin))!);
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
            var normalizedEmail = IdentifierNormalizer.NormalizeEmail(email);
            var existing = normalizedEmail is null
                ? null
                : await userManager.FindByEmailAsync(normalizedEmail);
            if (existing is not null)
            {
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return Redirect(securityOptions.BuildWebReturnUrl("explicit_link_required"));
            }

            user = new ViverAppUser
            {
                UserName = email,
                NormalizedUserName = normalizedEmail,
                RoleCode = ViverAppRoles.Patient,
                StatusCode = "active",
                FullName = info.Principal.FindFirstValue(ClaimTypes.Name) ?? "Usuário",
                Email = email,
                NormalizedEmail = normalizedEmail,
                EmailConfirmed = true,
            };
            var created = await userManager.CreateAsync(user);
            if (!created.Succeeded || !(await userManager.AddLoginAsync(user, info)).Succeeded)
            {
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return Redirect(securityOptions.BuildWebReturnUrl("google_failed"));
            }

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
    [HttpPost("google/link/start")]
    public ActionResult StartGoogleLink()
    {
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
