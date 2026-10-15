using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;
using ViverApp.Web.Components.Shared;
using Xunit;

namespace ViverApp.Web.Tests;

public sealed class AppointmentListPrintTests
{
    [Fact]
    public async Task CompactPrintContainsHeaderEveryRowDiscountsAndEscapedNotes()
    {
        var rows = Enumerable.Range(100, 25).Select(number => new AppointmentPrintRow((ulong)number,
            "QA Paciente", "QA Profissional", "Ecobiometria + Paquimetria", "examination", "Oftalmologia",
            new(2026, 10, 14, 9, 30, 0), "private", 100, 72, true, false, true,
            "<script>alert('não executar')</script>", "completed")).ToArray();
        var html = await Render(new("Clínica QA", "Gestor QA", new(2026, 10, 14, 10, 0, 0),
            "America/Sao_Paulo", "Pagamento Pendente: Sim", rows));
        Assert.Contains("appointment-print-document", html);
        Assert.Contains("cash-print-header", html);
        Assert.Contains("Cl", html);
        Assert.Contains("#100", html); Assert.Contains("#124", html);
        Assert.Equal(25, html.Split("<s>").Length - 1);
        Assert.Contains("Pagamento Pendente", html);
        Assert.Contains("Oftalmologia", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task PrintDistinguishesInsuranceAndPaidWithoutClaimingFreeAppointmentsAreUnpaid()
    {
        var rows = new AppointmentPrintRow[]
        {
            new(100, "QA Paciente", "QA Médico", "Consulta", "consultation", "Oftalmologia", DateTime.Today,
                "insurance", 100, 0, false, false, false, null, "confirmed"),
            new(101, "QA Paciente", "QA Médico", "Consulta", "consultation", "Oftalmologia", DateTime.Today,
                "private", 100, 100, true, true, true, null, "completed"),
        };
        var html = await Render(new("QA", "Gestor QA", DateTime.Today, "America/Sao_Paulo", "Sem filtros adicionais", rows));
        var text = System.Net.WebUtility.HtmlDecode(html);
        Assert.Contains("Convênio", text); Assert.Contains("Sem cobrança", text); Assert.Contains("Pago", text);
        Assert.DoesNotContain("Não pago", text); Assert.DoesNotContain("Pagamento Pendente</strong>", text);
    }

    private static async Task<string> Render(AppointmentPrintData report)
    {
        await using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IJSRuntime, NoBrowser>().AddSingleton(WebBackendOptions.Load(
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Backend:BaseUrl"] = "http://localhost:5115" }).Build(),
                Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Development" }).Environment))
            .AddSingleton<UiErrorNotifier>().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<AppointmentListPrint>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(AppointmentListPrint.Report)] = report }));
            return output.ToHtmlString();
        });
    }
    private sealed class NoBrowser : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args) => throw new NotSupportedException();
    }
}
