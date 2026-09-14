using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ViverApp.Api.Features.Payments;
using Xunit;

namespace ViverApp.Payments.Tests;

public sealed class PaymentSecurityTests
{
    [Fact]
    public void Webhook_signature_uses_exact_raw_payload_and_constant_contract()
    {
        const string token = "sandbox-secret";
        var payload = Encoding.UTF8.GetBytes("{\"id\":\"CHAR_1\",\"status\":\"PAID\"}");
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{token}-{Encoding.UTF8.GetString(payload)}"))).ToLowerInvariant();

        Assert.True(PagBankWebhookAuthenticator.Verify(token, payload, expected, out _));

        var formatted = Encoding.UTF8.GetBytes("{ \"id\": \"CHAR_1\", \"status\": \"PAID\" }");
        Assert.False(PagBankWebhookAuthenticator.Verify(token, formatted, expected, out _));
        Assert.False(PagBankWebhookAuthenticator.Verify(token, payload, new string('0', 64), out _));
    }

    [Theory]
    [InlineData("WAITING", "pending")]
    [InlineData("IN_ANALYSIS", "authorized")]
    [InlineData("PAID", "paid")]
    [InlineData("DECLINED", "failed")]
    [InlineData("EXPIRED", "canceled")]
    public void Provider_status_is_normalized(string providerStatus, string expected) =>
        Assert.Equal(expected, PaymentStateMachine.Normalize(providerStatus));

    [Fact]
    public void Paid_and_refunded_states_do_not_regress()
    {
        var now = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(PaymentStateMachine.Decide("paid", "pending", now, now.AddMinutes(1)).Apply);
        Assert.False(PaymentStateMachine.Decide("refunded", "paid", now, now.AddMinutes(1)).Apply);
        Assert.False(PaymentStateMachine.Decide("authorized", "failed", now, now.AddMinutes(-1)).Apply);
        Assert.True(PaymentStateMachine.Decide("failed", "paid", now, now.AddMinutes(-1)).Apply);
        Assert.Equal("refunded", PaymentStateMachine.Normalize("CANCELED", 10_000, 10_000));
    }

    [Fact]
    public void Parser_finds_checkout_pay_link_and_nested_charge()
    {
        using var checkoutJson = JsonDocument.Parse("""
            {"id":"CHEC_123","reference_id":"appointment-7","status":"ACTIVE","links":[{"rel":"PAY","href":"https://pagamento.pagseguro.uol.com.br/pagamento?code=123"}]}
            """);
        var checkout = PagBankResourceParser.Parse(checkoutJson.RootElement);
        Assert.Equal("CHEC_123", checkout.Id);
        Assert.NotNull(checkout.PayUrl);

        using var chargeJson = JsonDocument.Parse("""
            {"id":"CHEC_123","reference_id":"appointment-7","status":"ACTIVE","payments":[{"charges":[{"id":"CHAR_123","status":"PAID","amount":{"value":12500,"summary":{"total":12500,"refunded":0}},"payment_response":{"reference":"NSU-123","raw_data":{"authorization_code":"AUTH-456"}},"payment_method":{"type":"CREDIT_CARD","card":{"last_digits":"4242"}}}]}]}
            """);
        var charge = PagBankResourceParser.Parse(chargeJson.RootElement);
        Assert.Equal("CHAR_123", charge.Id);
        Assert.Equal("appointment-7", charge.ReferenceId);
        Assert.Equal(12_500, charge.TotalCents);
        Assert.Equal("CREDIT_CARD", charge.MethodCode);
        Assert.Equal("4242", charge.CardLastFour);
        Assert.Equal("AUTH-456", charge.AuthorizationReference);
    }

    [Fact]
    public void Production_cannot_start_without_explicit_activation()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PagBank:Enabled"] = "true",
            ["PagBank:Environment"] = "Production",
            ["PagBank:ProductionEnabled"] = "false",
            ["PagBank:TokenProduction"] = "not-a-real-token",
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PagBankOptions.Load(configuration, new TestHostEnvironment()));

        Assert.Contains("ProductionEnabled=true", error.Message, StringComparison.Ordinal);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ViverApp.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
