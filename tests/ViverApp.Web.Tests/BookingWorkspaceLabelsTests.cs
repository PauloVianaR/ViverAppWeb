using System.Text.Json;
using Xunit;

namespace ViverApp.Web.Tests;

public sealed class BookingWorkspaceLabelsTests
{
    [Fact]
    public void IdOnlyResponseResolvesItsOwnSpecialtyIncludingInactive()
    {
        var metadata = JsonSerializer.Deserialize<WebBookingWorkspaceMetadata>(
            """{"billingFormCode":"private","specialtyId":43,"rowVersion":1}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("Oftalmologia", BookingWorkspaceLabels.Specialty(metadata, [new(43, "Oftalmologia", false, 1)]));
        Assert.Equal("Psicologia", BookingWorkspaceLabels.Specialty(new("private", 56, 1),
            [new(43, "Oftalmologia", true, 1), new(56, "Psicologia", true, 1)]));
    }

    [Fact]
    public void ServerLabelTakesPriorityAndUnknownIdsAreNotGuessed()
    {
        Assert.Equal("Oftalmologia", BookingWorkspaceLabels.Specialty(new("private", 43, 1, "Oftalmologia"), null));
        Assert.Equal("Oftalmologia", BookingWorkspaceLabels.Specialty(new("private", 43, 1, " "),
            [new(43, "Oftalmologia", true, 1)]));
        Assert.Equal("Não informada", BookingWorkspaceLabels.Specialty(new("private", 99, 1),
            [new(43, "Oftalmologia", true, 1)]));
        Assert.Equal("Não informada", BookingWorkspaceLabels.Specialty(new("private", null, 1), null));
    }
}
