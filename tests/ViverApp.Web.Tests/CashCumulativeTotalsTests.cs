using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ViverApp.Web.Components.Shared;
using Xunit;

namespace ViverApp.Web.Tests;

public sealed class CashCumulativeTotalsTests
{
    [Fact]
    public async Task HiddenCumulativeSummary_DoesNotRenderTotals()
        => Assert.Equal("", (await Render(null)).Trim());

    [Fact]
    public async Task CumulativeTotals_StartCollapsed_AndDisplayEveryMethodWithEntriesOutflowsAndNet()
    {
        var methods = new CashMethodTotal[]
        {
            new("cash", 100m, 25m, 75m, 2),
            new("pix", 70m, 70m, 0m, 2),
            new("credit_card", 50m, 60m, -10m, 2),
            new("debit_card", 20m, 0m, 20m, 1),
            new("pagbank_online", 30m, 0m, 30m, 1),
            new("other", 10m, 0m, 10m, 1),
        };
        var html = await Render(new(280m, 70m, 0m, 25m, 0m, 125m, 9, methods));
        Assert.Contains("Totalizadores gerais do caixa", html);
        Assert.Contains("Totais gerais por forma de pagamento", html);
        Assert.DoesNotContain(" open", html);
        foreach (var method in methods)
        {
            Assert.Contains(CashLabels.Method(method.MethodCode), html);
            Assert.Contains($"Entradas {ManagerLabels.Money(method.Entries)}", html);
            Assert.Contains($"Saídas {ManagerLabels.Money(method.Outflows)}", html);
            Assert.Contains($"Líquido {ManagerLabels.Money(method.Net)}", html);
        }
    }

    [Fact]
    public async Task EmptyAccumulatedLedger_HasAnExplicitEmptyState()
    {
        var html = await Render(new(0, 0, 0, 0, 0, 0, 0, []));
        Assert.Contains("Sem movimentações acumuladas por forma de pagamento.", html);
    }

    private static async Task<string> Render(CashSummary? summary)
    {
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<CashCumulativeTotals>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(CashCumulativeTotals.Summary)] = summary }));
            return WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }
}
