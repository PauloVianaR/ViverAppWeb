using ViverApp.Api.Features.Identity;
using Xunit;

namespace ViverApp.PatientScheduling.Tests;

public sealed class NotificationPatientNameTests
{
    [Theory]
    [InlineData("Ana Maria Silva", "Ana Maria")]
    [InlineData("Gonçalo", "Gonçalo")]
    [InlineData("  João   da Costa ", "João da")]
    [InlineData("María\tLuísa\nSilva", "María Luísa")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Names_IncludeAtMostTwoNonemptyParts(string? fullName, string expected)
        => Assert.Equal(expected, NotificationPatientName.FirstTwo(fullName));
}
