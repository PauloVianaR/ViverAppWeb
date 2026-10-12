using Xunit;

namespace ViverApp.Web.Tests;

public sealed class PatientSearchKeyTests
{
    [Theory]
    [InlineData("GONÇALO", "GONcalo")]
    [InlineData("João da Silva", "JOAO")]
    [InlineData("Ana-Luíza", "ana luiza")]
    [InlineData("123.456.789-00", "12345678900")]
    [InlineData("Érica D'Ávila", "erica davila")]
    public void Search_IgnoresCaseAccentsAndPunctuation(string name, string query)
        => Assert.Contains(PatientSearchKey.Normalize(query), PatientSearchKey.Normalize(name), StringComparison.Ordinal);

    [Fact]
    public void PendingAppointment_HasWaitingForPaymentLabel()
        => Assert.Equal("Aguardando Pagamento", PatientLabels.Status("pending"));
}
