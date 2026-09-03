using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ViverApp.Api.Features.ClinicAdministration;

internal static class ClinicAdministrationSupport
{
    public static ulong RequireActorId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : throw new InvalidOperationException("authenticated_account_id_missing");
    }

    public static string RequiredText(string value) => value.Trim();

    public static string? OptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public static string NormalizeName(string value)
    {
        var decomposed = RequiredText(value).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static bool HasValidRange(TimeSpan start, TimeSpan end) =>
        start >= TimeSpan.Zero && end <= TimeSpan.FromDays(1) && end > start;

    public static ActionResult ConcurrencyProblem(ControllerBase controller) =>
        controller.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "O cadastro foi alterado por outra sessão. Recarregue e tente novamente.");

    public static void SetConcurrency<TEntity>(
        DbContext database,
        TEntity entity,
        string propertyName,
        ulong suppliedVersion)
        where TEntity : class
    {
        database.Entry(entity).Property(propertyName).OriginalValue = suppliedVersion;
        database.Entry(entity).Property(propertyName).CurrentValue = suppliedVersion + 1;
    }
}
