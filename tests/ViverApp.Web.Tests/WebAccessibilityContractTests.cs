using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using ViverApp.Web.Navigation;
using Xunit;

namespace ViverApp.Web.Tests;

public sealed class WebAccessibilityContractTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public async Task Home_HasBrandFaviconLandmarksAndSkipLink()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/");
        var html = await ReadUtf8Async(response.Content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("lang=\"pt-BR\"", html, StringComparison.Ordinal);
        Assert.Contains("rel=\"icon\"", html, StringComparison.Ordinal);
        Assert.Contains("images/logo", html, StringComparison.Ordinal);
        Assert.Contains("Ir para o conteúdo principal", html, StringComparison.Ordinal);
        Assert.Contains("id=\"main-content\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"public-navigation\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/paciente", "Área do paciente", "Pagamentos")]
    [InlineData("/medico", "Área médica", "Pacientes")]
    [InlineData("/gestao", "Área do gestor", "Cadastros")]
    [InlineData("/administracao", "Área administrativa", "Indicadores")]
    public async Task ProfileShells_RenderExpectedContext(string path, string heading, string navigationItem)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path);
        var html = await ReadUtf8Async(response.Content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(heading, html, StringComparison.Ordinal);
        Assert.Contains(navigationItem, html, StringComparison.Ordinal);
        Assert.Contains("A API valida cada permissão", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Lorem ipsum", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DesignSystem_DocumentsRequiredInterfaceStates()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/design-system");
        var html = await ReadUtf8Async(response.Content);

        Assert.Contains("Cores semânticas", html, StringComparison.Ordinal);
        Assert.Contains("Nenhum resultado", html, StringComparison.Ordinal);
        Assert.Contains("Conexão indisponível", html, StringComparison.Ordinal);
        Assert.Contains("Sessão expirada", html, StringComparison.Ordinal);
        Assert.Contains("Carregando conteúdo", html, StringComparison.Ordinal);
        Assert.Contains("Tabela que se torna cartão no celular", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/paciente/agendar")]
    [InlineData("/paciente/agenda")]
    public async Task PatientScheduling_HasAccessibleFourStepJourney(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path);
        var html = await ReadUtf8Async(response.Content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Etapas do agendamento", html, StringComparison.Ordinal);
        Assert.Contains("Escolha o atendimento", html, StringComparison.Ordinal);
        Assert.Contains("Profissional", html, StringComparison.Ordinal);
        Assert.Contains("Data e horário", html, StringComparison.Ordinal);
        Assert.Contains("Resumo", html, StringComparison.Ordinal);
        Assert.Contains("Minha agenda", html, StringComparison.Ordinal);
        Assert.Contains("24 horas de antecedência", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownRoute_UsesLocalizedRecoveryState()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/pagina-que-nao-existe");
        var html = await ReadUtf8Async(response.Content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Esta página não foi encontrada", html, StringComparison.Ordinal);
        Assert.Contains("Voltar ao início", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/paciente/agenda", ShellProfile.Patient)]
    [InlineData("/medico", ShellProfile.Doctor)]
    [InlineData("/gestao/cadastros", ShellProfile.Manager)]
    [InlineData("/administracao/usuarios", ShellProfile.Administrator)]
    [InlineData("/", ShellProfile.Public)]
    [InlineData("/gestaox", ShellProfile.Public)]
    public void NavigationCatalog_ResolvesProfileWithoutGrantingAuthorization(string path, ShellProfile expected)
    {
        Assert.Equal(expected, ShellNavigationCatalog.Resolve(path).Profile);
    }

    [Theory]
    [InlineData("#0b2340", "#ffffff")]
    [InlineData("#172235", "#ffffff")]
    [InlineData("#526176", "#ffffff")]
    [InlineData("#8f202a", "#ffffff")]
    public void DesignTokens_MeetWcagAaForNormalText(string foreground, string background)
    {
        Assert.True(ContrastRatio(foreground, background) >= 4.5);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost:7110"),
    });

    private static async Task<string> ReadUtf8Async(HttpContent content) =>
        WebUtility.HtmlDecode(Encoding.UTF8.GetString(await content.ReadAsByteArrayAsync()));

    private static double ContrastRatio(string foreground, string background)
    {
        var light = RelativeLuminance(background);
        var dark = RelativeLuminance(foreground);
        return (Math.Max(light, dark) + 0.05) / (Math.Min(light, dark) + 0.05);
    }

    private static double RelativeLuminance(string color)
    {
        var channels = Enumerable.Range(0, 3)
            .Select(index => Convert.ToInt32(color.Substring(1 + (index * 2), 2), 16) / 255d)
            .Select(channel => channel <= 0.04045
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4))
            .ToArray();
        return (0.2126 * channels[0]) + (0.7152 * channels[1]) + (0.0722 * channels[2]);
    }
}
