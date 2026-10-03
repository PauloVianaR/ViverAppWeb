using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ViverApp.Api.Features.Identity;

public sealed class IdentitySmsUnavailableException : Exception;

public sealed class IdentitySmsUnavailableHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not IdentitySmsUnavailableException)
            return false;

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "O envio por SMS está temporariamente indisponível. Use e-mail.",
        }, cancellationToken);
        return true;
    }
}
