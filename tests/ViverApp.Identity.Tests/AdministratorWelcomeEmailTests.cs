using System.Text.Json;
using ViverApp.Api.Features.AdministratorExperience;
using ViverApp.Api.Features.Identity;
using Xunit;

namespace ViverApp.Identity.Tests;

public sealed class AdministratorWelcomeEmailTests
{
    [Fact]
    public void WelcomeProvidesSafeHtmlAndPlainTextWithoutCredentials()
    {
        var result = AdministratorWelcomeEmail.Render(new("QA <script>alert(1)</script>", "Clínica & Cuidado", "https://viveralmenara.com/acesso", DateTime.UtcNow.AddDays(7)));
        Assert.Contains("Esqueci a senha", result.TextBody); Assert.Contains("sem senha", result.HtmlBody);
        Assert.Contains("Clínica & Cuidado", System.Net.WebUtility.HtmlDecode(result.HtmlBody)); Assert.Contains("&lt;script&gt;", result.HtmlBody);
        Assert.DoesNotContain("<script>", result.HtmlBody); Assert.Contains("https://viveralmenara.com/acesso", result.HtmlBody);
        Assert.Throws<InvalidOperationException>(() => AdministratorWelcomeEmail.Render(new("QA", "Clínica", "javascript:alert(1)", DateTime.UtcNow)));
        Assert.Throws<InvalidOperationException>(() => AdministratorWelcomeEmail.Render(new("QA", "Clínica", "http://untrusted.example/acesso", DateTime.UtcNow)));
    }

    [Fact]
    public void AdministrativeContractsRejectPasswordRoleStatusAndVerificationMassAssignment()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var field in new[] { "password", "passwordHash", "emailVerified", "statusCode", "roleCode", "adminEmailVerificationPending" })
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AdministratorAccountUpdateRequest>("{\"rowVersion\":1,\"data\":{\"fullName\":\"QA35\",\"" + field + "\":\"injetado\"}}", options));
    }
}
