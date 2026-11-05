using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ViverApp.Web.Components.Shared;
using Xunit;

namespace ViverApp.Web.Tests;

public sealed class AppointmentCategoryPickerTests
{
    [Theory]
    [InlineData("procedure,consultation,procedure", "Consulta + Procedimento")]
    [InlineData("surgery,examination", "Exame + Cirurgia")]
    [InlineData("procedure,surgery,examination,consultation", "Consulta + Exame + Cirurgia + Procedimento")]
    [InlineData("", "Consulta")]
    public void LabelsAreDistinctOrderedAndRetainTheLegacyFallback(string codes, string expected) =>
        Assert.Equal(expected, PatientLabels.CombinedCategories(codes.Split(',', StringSplitOptions.RemoveEmptyEntries), "consultation"));

    [Fact]
    public async Task PickerExposesFourIndependentCheckboxesAndAllSelectedCategories()
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<AppointmentCategoryPicker>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(AppointmentCategoryPicker.Selected)] = new[] { "consultation", "examination", "surgery", "procedure" } }));
            return WebUtility.HtmlDecode(output.ToHtmlString());
        });
        Assert.Contains("Consulta + Exame + Cirurgia + Procedimento", html);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "type=\"checkbox\"").Count);
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(html, "checked").Count);
    }
}
