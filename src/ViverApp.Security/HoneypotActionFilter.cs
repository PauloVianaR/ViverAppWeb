using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace ViverApp.Security;

public sealed class HoneypotActionFilter(
    HoneypotDetector detector,
    ILogger<HoneypotActionFilter> logger) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        if (!request.HasFormContentType)
        {
            context.Result = CreateRejection(context.HttpContext, "Formato de formulário inválido.");
            return;
        }

        var form = await request.ReadFormAsync(context.HttpContext.RequestAborted);
        if (detector.IsTriggered(form[HoneypotDetector.FieldName]))
        {
            var correlationId = CorrelationIdMiddleware.GetCorrelationId(context.HttpContext);
            SecurityLog.HoneypotTriggered(logger, correlationId);
            context.Result = CreateRejection(context.HttpContext, "Formulário inválido.");
            return;
        }

        await next();
    }

    private static ObjectResult CreateRejection(HttpContext context, string title)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = title,
        };
        problem.Extensions["correlationId"] = CorrelationIdMiddleware.GetCorrelationId(context);
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
