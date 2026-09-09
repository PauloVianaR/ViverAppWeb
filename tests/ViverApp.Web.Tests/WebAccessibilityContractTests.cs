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
        Assert.Contains("Cadastro rápido com Google", html, StringComparison.Ordinal);
        Assert.Contains("Criar conta com Google", html, StringComparison.Ordinal);
        Assert.Contains("Escolha seu perfil", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/paciente")]
    [InlineData("/medico")]
    [InlineData("/gestao")]
    [InlineData("/administracao")]
    public async Task ProtectedAreas_DoNotPrerenderContentBeforeIdentityResolution(string path)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path);
        var html = await ReadUtf8Async(response.Content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Validando acesso", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell pronto", html, StringComparison.Ordinal);
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
        Assert.Contains("Validando acesso", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Minha agenda", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PatientPayments_ExplainsServerAuthoritativeCheckout()
    {
        using var client = CreateClient();

        using var response = await client.GetAsync("/paciente/pagamentos");
        var html = await ReadUtf8Async(response.Content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Validando acesso", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Pagamento seguro", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/medico/agenda", "Agenda clínica")]
    [InlineData("/medico/historico", "Histórico de atendimentos")]
    [InlineData("/medico/pacientes", "Pacientes vinculados")]
    [InlineData("/medico/disponibilidade", "Minha disponibilidade")]
    [InlineData("/gestao/agenda", "Agenda clínica")]
    [InlineData("/gestao/pacientes", "Pacientes vinculados")]
    [InlineData("/gestao/historico", "Histórico dos atendimentos")]
    [InlineData("/gestao/perfil", "Meu perfil gerencial")]
    [InlineData("/gestao/premium", "Solicitações Premium")]
    [InlineData("/administracao/clinica", "Clínica única")]
    [InlineData("/administracao/consultas", "Consultas planejadas")]
    [InlineData("/administracao/analytics", "Analytics da clínica")]
    [InlineData("/administracao/notificacoes", "Notificações operacionais")]
    [InlineData("/administracao/usuarios", "Usuários e Premium")]
    public async Task ClinicalWorkspace_ExposesRoleAwareResponsiveJourneys(string path, string heading)
    {
        using var client = CreateClient();

        using var response = await client.GetAsync(path);
        var html = await ReadUtf8Async(response.Content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Validando acesso", html, StringComparison.Ordinal);
        Assert.DoesNotContain(heading, html, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministratorNavigation_CoversPatientOperations()
    {
        var shell = ShellNavigationCatalog.ResolveRole("administrator");
        Assert.Equal(ShellProfile.Administrator, shell.Profile);
        Assert.Equal(7, shell.Items.Count);
        Assert.Equal(new[] { "/administracao", "/administracao/clinica", "/administracao/consultas", "/administracao/pacientes", "/administracao/analytics", "/administracao/notificacoes", "/administracao/usuarios" }, shell.Items.Select(x => x.Href));
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
    [InlineData("patient", "/paciente", true)]
    [InlineData("patient", "/gestao", false)]
    [InlineData("doctor", "/medico/agenda", true)]
    [InlineData("manager", "/administracao", false)]
    [InlineData("administrator", "/administracao/aprovacoes", true)]
    public void NavigationCatalog_UsesAuthenticatedRoleToOwnRoutes(string role, string path, bool expected)
    {
        Assert.Equal(expected, ShellNavigationCatalog.OwnsPath(role, path));
    }

    [Fact]
    public async Task AccessPage_OffersAllSupportedAuthenticationAndRegistrationRoles()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/acesso");
        var html = await ReadUtf8Async(response.Content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Entrar com senha", html, StringComparison.Ordinal);
        Assert.Contains("Continuar com Google", html, StringComparison.Ordinal);
        Assert.Contains("Usar chave de acesso", html, StringComparison.Ordinal);
        Assert.Contains("Entrar com código", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"administrator\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegistrationJourney_UsesGoogleFirstAndKeepsClassicAsAlternative()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/acesso?modo=cadastro");
        var html = await ReadUtf8Async(response.Content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Crie sua conta com Google", html, StringComparison.Ordinal);
        Assert.Contains("Continuar com Google como Paciente", html, StringComparison.Ordinal);
        Assert.Contains("Cadastrar com e-mail ou telefone", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"full-name\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"administrator\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GoogleOnboarding_LocksProfessionalRoleAndShowsApprovalBeforeForm()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/acesso?result=google_onboarding&onboarding=test-token&role=doctor");
        var html = await ReadUtf8Async(response.Content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Conta Google vinculada", html, StringComparison.Ordinal);
        Assert.Contains("Este cadastro precisa de aprovação", html, StringComparison.Ordinal);
        Assert.Contains("Dados profissionais", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"role\"", html, StringComparison.Ordinal);
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
