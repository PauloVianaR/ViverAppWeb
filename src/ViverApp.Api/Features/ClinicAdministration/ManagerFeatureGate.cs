using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using ViverApp.Api.Features.Identity;
using ViverApp.Api.Infrastructure.Persistence.Generated;

namespace ViverApp.Api.Features.ClinicAdministration;

[AttributeUsage(AttributeTargets.Method)]
public sealed class ManagerFeatureGateAttribute : TypeFilterAttribute
{
    public ManagerFeatureGateAttribute(string settingKey)
        : base(typeof(ManagerFeatureGateFilter))
    {
        Arguments = [settingKey];
    }
}

public sealed class ManagerFeatureGateFilter(ViverAppDbContext database, string settingKey) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.User.IsInRole(ViverAppRoles.Manager))
        {
            await next();
            return;
        }

        var value = await database.ApplicationSettings.AsNoTracking()
            .Where(item => item.SettingKey == settingKey)
            .Select(item => item.ValueJson)
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
        if (value is not null && TryReadBoolean(value, out var enabled) && enabled)
        {
            await next();
            return;
        }

        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Esta permissão do Gestor foi desabilitada pelo Administrador.",
            Extensions = { ["code"] = "manager_feature_disabled" },
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }

    internal static bool TryReadBoolean(string json, out bool value)
    {
        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(json);
            if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = element.GetBoolean();
                return true;
            }
        }
        catch (JsonException)
        {
        }

        value = false;
        return false;
    }
}
