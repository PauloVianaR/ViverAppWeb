using Microsoft.AspNetCore.Identity;

namespace ViverApp.Api.Features.Identity;

public sealed class PasswordTimingProtector
{
    private readonly IPasswordHasher<ViverAppUser> passwordHasher;
    private readonly ViverAppUser dummyUser = new();
    private readonly string dummyHash;

    public PasswordTimingProtector(IPasswordHasher<ViverAppUser> passwordHasher)
    {
        this.passwordHasher = passwordHasher;
        dummyHash = passwordHasher.HashPassword(dummyUser, Guid.NewGuid().ToString("N"));
    }

    public void VerifyDummy(string suppliedPassword)
    {
        _ = passwordHasher.VerifyHashedPassword(dummyUser, dummyHash, suppliedPassword);
    }
}
