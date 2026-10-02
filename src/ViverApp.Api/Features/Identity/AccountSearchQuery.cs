using ViverApp.Api.Infrastructure.Persistence.Generated.Entities;

namespace ViverApp.Api.Features.Identity;

internal static class AccountSearchQuery
{
    // PhoneE164 usa charset ASCII no MySQL; termos Unicode só podem ser comparados com nome/e-mail UTF-8.
    public static IQueryable<Account> WhereNameEmailOrPhoneContains(this IQueryable<Account> query, string term) =>
        term.All(char.IsAscii)
            ? query.Where(account => account.FullName.Contains(term)
                || account.Email != null && account.Email.Contains(term)
                || account.PhoneE164 != null && account.PhoneE164.Contains(term))
            : query.Where(account => account.FullName.Contains(term)
                || account.Email != null && account.Email.Contains(term));
}
