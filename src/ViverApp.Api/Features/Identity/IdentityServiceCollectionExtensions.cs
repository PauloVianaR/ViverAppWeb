using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;

namespace ViverApp.Api.Features.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddViverAppIdentity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        var allowInsecureLocalHttp = environment?.IsDevelopment() == true
            && configuration.GetValue("Security:AllowInsecureLocalHttp", false);
        var securityOptions = IdentitySecurityOptions.Load(configuration, allowInsecureLocalHttp);
        services.AddSingleton(securityOptions);
        services.AddScoped<ViverAppUserStore>();
        services.AddScoped<IUserStore<ViverAppUser>>(provider =>
            provider.GetRequiredService<ViverAppUserStore>());
        services.AddScoped<IUserClaimsPrincipalFactory<ViverAppUser>, ViverAppClaimsPrincipalFactory>();
        services.AddScoped<ViverAppSessionService>();
        services.AddScoped<ViverAppCookieEvents>();
        services.AddScoped<IdentityChallengeService>();
        services.AddScoped<IdentityAuditWriter>();
        services.AddScoped<IdentityNotificationService>();
        services.AddScoped<PasswordTimingProtector>();
        services.AddSingleton<GoogleOnboardingProtector>();
        if (configuration.GetValue("Authentication:Delivery:Enabled", true))
        {
            var deliveryOptions = IdentityDeliveryOptions.Load(configuration);
            services.AddSingleton(deliveryOptions);
            services.AddHttpClient<SmsBaratoIdentitySender>(client =>
            {
                client.BaseAddress = deliveryOptions.SmsBaratoBaseUrl;
                client.Timeout = TimeSpan.FromSeconds(10);
            });
            services.AddSingleton<SmtpIdentitySender>();
            services.AddHostedService<IdentityOutboxWorker>();
        }

        services.AddIdentityCore<ViverAppUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.SignIn.RequireConfirmedAccount = false;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
                options.User.RequireUniqueEmail = false;
            })
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.Configure<IdentityPasskeyOptions>(options =>
        {
            options.ServerDomain = configuration["Authentication:Passkeys:ServerDomain"];
            options.UserVerificationRequirement = "required";
            options.ResidentKeyRequirement = "preferred";
        });

        var authentication = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });
        authentication.AddIdentityCookies();

        services.Configure<CookieAuthenticationOptions>(
            IdentityConstants.ApplicationScheme,
            options =>
            {
                options.Cookie.Name = allowInsecureLocalHttp
                    ? "ViverApp.Session.Local"
                    : "__Host-ViverApp.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.Path = "/";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = allowInsecureLocalHttp
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.EventsType = typeof(ViverAppCookieEvents);
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = false;
            });
        services.Configure<CookieAuthenticationOptions>(
            IdentityConstants.ExternalScheme,
            options =>
            {
                options.Cookie.Name = allowInsecureLocalHttp
                    ? "ViverApp.External.Local"
                    : "__Host-ViverApp.External";
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
                options.Cookie.Path = "/";
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = allowInsecureLocalHttp
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = false;
            });

        if (securityOptions.GoogleEnabled)
        {
            authentication.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
            {
                options.ClientId = securityOptions.GoogleClientId!;
                options.ClientSecret = securityOptions.GoogleClientSecret!;
                options.CallbackPath = securityOptions.GoogleRedirectUri!.AbsolutePath;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.SaveTokens = false;
                options.UsePkce = true;
                options.ClaimActions.MapJsonKey("email_verified", "email_verified");
                options.CorrelationCookie.Name = allowInsecureLocalHttp
                    ? "ViverApp.Google.Correlation.Local."
                    : "__Host-ViverApp.Google.Correlation.";
                options.CorrelationCookie.Path = "/";
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = allowInsecureLocalHttp
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect(securityOptions.BuildWebReturnUrl("google_failed"));
                    return Task.CompletedTask;
                };
            });
        }

        var authorization = services.AddAuthorizationBuilder();
        authorization.SetDefaultPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
                IdentityConstants.ApplicationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(ViverAppClaimTypes.MfaSatisfied, bool.TrueString)
            .Build());
        authorization.SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
            IdentityConstants.ApplicationScheme)
            .RequireAuthenticatedUser()
            .RequireClaim(ViverAppClaimTypes.MfaSatisfied, bool.TrueString)
            .Build());
        authorization.AddPolicy(ViverAppPolicies.Patient, policy => policy.RequireRole(ViverAppRoles.Patient));
        authorization.AddPolicy(ViverAppPolicies.Doctor, policy => policy.RequireRole(ViverAppRoles.Doctor));
        authorization.AddPolicy(ViverAppPolicies.Manager, policy => policy.RequireRole(ViverAppRoles.Manager));
        authorization.AddPolicy(
            ViverAppPolicies.Administrator,
            policy => policy
                .RequireRole(ViverAppRoles.Administrator)
                .RequireClaim(ViverAppClaimTypes.MfaSatisfied, bool.TrueString));
        authorization.AddPolicy(
            ViverAppPolicies.ClinicalStaff,
            policy => policy.RequireRole(
                ViverAppRoles.Doctor,
                ViverAppRoles.Manager,
                ViverAppRoles.Administrator));
        authorization.AddPolicy(
            ViverAppPolicies.Management,
            policy => policy.RequireRole(
                ViverAppRoles.Manager,
                ViverAppRoles.Administrator));
        foreach (var policyName in new[]
        {
            ViverAppPolicies.MedicalRecordRead,
            ViverAppPolicies.MedicalRecordClinicalRead,
            ViverAppPolicies.MedicalRecordDocument,
            ViverAppPolicies.MedicalRecordExport,
        })
        {
            authorization.AddPolicy(policyName, policy => policy.RequireRole(
                ViverAppRoles.Doctor, ViverAppRoles.Manager, ViverAppRoles.Administrator));
        }
        authorization.AddPolicy(ViverAppPolicies.MedicalRecordWrite, policy => policy.RequireRole(ViverAppRoles.Doctor));
        authorization.AddPolicy(ViverAppPolicies.MedicalRecordAudit, policy => policy
            .RequireRole(ViverAppRoles.Administrator)
            .RequireClaim(ViverAppClaimTypes.MfaSatisfied, bool.TrueString));
        foreach (var policyName in new[]
        {
            ViverAppPolicies.CashRead,
            ViverAppPolicies.CashWrite,
            ViverAppPolicies.CashClose,
            ViverAppPolicies.CashPrint,
            ViverAppPolicies.PaymentReverse,
        })
        {
            authorization.AddPolicy(
                policyName,
                policy => policy
                    .RequireAssertion(context =>
                        context.User.IsInRole(ViverAppRoles.Manager)
                        || (context.User.IsInRole(ViverAppRoles.Administrator)
                            && context.User.HasClaim(ViverAppClaimTypes.MfaSatisfied, bool.TrueString))));
        }
        authorization.AddPolicy(
            ViverAppPolicies.MfaEnrollment,
            policy => policy.RequireAuthenticatedUser());
        authorization.AddPolicy(
            ViverAppPolicies.MfaSatisfied,
            policy => policy.RequireClaim(ViverAppClaimTypes.MfaSatisfied, bool.TrueString));

        return services;
    }
}
