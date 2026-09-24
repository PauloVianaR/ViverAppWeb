using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ViverApp.Web;
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
    public void ManagerAppointmentCreate_UsesProfessionalAccountIdContract()
    {
        var request = new ManagerAppointmentCreate(10, 20, 30, "in_person",
            new DateOnly(2026, 9, 23), new TimeOnly(9, 30), null);

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"professionalAccountId\":20", json, StringComparison.Ordinal);
        Assert.DoesNotContain("doctorAccountId", json, StringComparison.Ordinal);
    }

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
        Assert.Contains("Ocorreu um erro interno não classificado. Contate o administrador do sistema.", html, StringComparison.Ordinal);
        Assert.Contains("role=\"alertdialog\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonProductionPublicSite_RejectsIndexingAndDoesNotExposeSitemap()
    {
        using var client = CreateClient();
        using var home = await client.GetAsync("/");
        var html = await ReadUtf8Async(home.Content);
        Assert.Equal("noindex, nofollow, noarchive", home.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Contains("rel=\"canonical\"", html, StringComparison.Ordinal);
        Assert.Contains("https://viveralmenara.com/", html, StringComparison.Ordinal);
        Assert.Contains("Preferências de cookies", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/privacidade\"", html, StringComparison.Ordinal);

        using var robots = await client.GetAsync("/robots.txt");
        Assert.Equal(HttpStatusCode.OK, robots.StatusCode);
        Assert.Contains("text/plain", robots.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
        Assert.Contains("Disallow: /", await robots.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        using var sitemap = await client.GetAsync("/sitemap.xml");
        Assert.Equal(HttpStatusCode.NotFound, sitemap.StatusCode);
    }

    [Fact]
    public async Task PublishedLegalDocumentsExposeTheirVersionAndPrivateAreasAreNotCached()
    {
        using var client = CreateClient();
        foreach (var path in new[] { "/termos", "/privacidade", "/cookies" })
        {
            using var response = await client.GetAsync(path);
            var html = await ReadUtf8Async(response.Content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Documento vigente", html, StringComparison.Ordinal);
            Assert.Contains("Versão 2026-09-21", html, StringComparison.Ordinal);
            Assert.Contains("equipe técnica e jurídica da plataforma", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Codex/OpenAI", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Documento em elaboração", html, StringComparison.Ordinal);
            Assert.Equal("noindex, nofollow, noarchive", response.Headers.GetValues("X-Robots-Tag").Single());
        }

        using var protectedResponse = await client.GetAsync("/paciente");
        Assert.True(protectedResponse.Headers.CacheControl?.NoStore);
        Assert.Equal("noindex, nofollow, noarchive", protectedResponse.Headers.GetValues("X-Robots-Tag").Single());
    }

    [Fact]
    public async Task PublicInformation_UsesVerifiedClinicIdentityAndInstitutionalChannels()
    {
        using var client = CreateClient();
        foreach (var path in new[] { "/sobre", "/contato", "/termos", "/privacidade", "/cookies" })
        {
            using var response = await client.GetAsync(path);
            var html = await ReadUtf8Async(response.Content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("CLINICA DE OLHOS JUSTINIANO LTDA", html, StringComparison.Ordinal);
        }

        var contact = await ReadUtf8Async((await client.GetAsync("/contato")).Content);
        Assert.Contains("35.843.469/0001-77", contact, StringComparison.Ordinal);
        Assert.Contains("Rua Tude Tupy, 214", contact, StringComparison.Ordinal);
        Assert.Contains("contato@viveralmenara.com", contact, StringComparison.Ordinal);
        Assert.Contains("privacidade@viveralmenara.com", contact, StringComparison.Ordinal);
        Assert.Contains("seguranca@viveralmenara.com", contact, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecurityText_UsesTheMonitoredInstitutionalChannel()
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("/.well-known/security.txt");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/plain", response.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
        Assert.Contains("Contact: mailto:seguranca@viveralmenara.com", content, StringComparison.Ordinal);
        Assert.Contains("Canonical: https://viveralmenara.com/.well-known/security.txt", content, StringComparison.Ordinal);
        Assert.Contains("Preferred-Languages: pt-BR, en", content, StringComparison.Ordinal);
    }

    [Fact]
    public void SitemapCatalog_ContainsOnlyApprovedPublicRouteShapes()
    {
        var policy = factory.Services.GetRequiredService<PublicSitePolicy>();
        var xml = XDocument.Parse(policy.SitemapXml());
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var locations = xml.Descendants(ns + "loc").Select(node => node.Value).ToArray();
        Assert.Equal(8, locations.Length);
        Assert.All(locations, url => Assert.StartsWith("https://viveralmenara.com/", url, StringComparison.Ordinal));
        Assert.DoesNotContain(locations, url => url.Contains("/acesso", StringComparison.Ordinal) || url.Contains("/paciente", StringComparison.Ordinal));
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

    [Theory]
    [InlineData("/medico/prontuario/1")]
    [InlineData("/gestao/prontuario/1")]
    [InlineData("/administracao/prontuario/1")]
    public async Task MedicalRecordRoutes_RemainProtectedBeforeRenderingSensitiveContent(string path)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(path);
        var html = await ReadUtf8Async(response.Content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Validando acesso", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Registro longitudinal privado", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AdministratorNavigation_CoversPatientOperations()
    {
        var shell = ShellNavigationCatalog.ResolveRole("administrator");
        Assert.Equal(ShellProfile.Administrator, shell.Profile);
        Assert.Equal(9, shell.Items.Count);
        Assert.Equal(new[] { "/administracao", "/administracao/clinica", "/administracao/agenda", "/administracao/atendimentos", "/administracao/pacientes", "/administracao/analytics", "/administracao/caixa", "/administracao/notificacoes", "/administracao/usuarios" }, shell.Items.Select(x => x.Href));
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
        Assert.DoesNotContain("Usar chave de acesso", html, StringComparison.Ordinal);
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

    [Fact]
    public void MobileNavigation_MovesOnlyDestinationsAfterTheFirstFourIntoMoreMenu()
    {
        var manager = ShellNavigationCatalog.ResolveRole("manager");
        var administrator = ShellNavigationCatalog.ResolveRole("administrator");
        var patient = ShellNavigationCatalog.ResolveRole("patient");

        Assert.Equal(new[] { "Início", "Agenda", "Pacientes", "Atendimentos" }, manager.MobilePrimaryItems.Select(x => x.Label));
        Assert.Equal(new[] { "Caixa", "Clínica", "Perfil" }, manager.MobileOverflowItems.Select(x => x.Label));
        Assert.Equal(new[] { "Pacientes", "Analytics", "Caixa", "Alertas", "Usuários" }, administrator.MobileOverflowItems.Select(x => x.Label));
        Assert.Equal(new[] { "Pagamentos", "Perfil" }, patient.MobileOverflowItems.Select(x => x.Label));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("One or more validation errors occurred.")]
    [InlineData("Internal Server Error")]
    [InlineData("TypeError: Failed to fetch")]
    [InlineData("Failed to complete negotiation with the server")]
    [InlineData("There was an unhandled exception on the current circuit")]
    public void ErrorDialog_DoesNotExposeGenericOrEnglishInternalFailures(string? message)
    {
        Assert.Equal(UiErrorNotifier.UnexpectedMessage, UiErrorNotifier.Normalize(message));
    }

    [Fact]
    public void ErrorDialog_RemovesClientStackFromCataloguedMessage()
    {
        const string message = "Revise os sinais vitais informados. Error: Revise os sinais vitais informados. at read (https://localhost/js/medical-records.js:15:11)";
        Assert.Equal("Revise os sinais vitais informados.", UiErrorNotifier.Normalize(message));
    }

    [Fact]
    public void AvailabilityError_RemovesJavaScriptStackAndKeepsDateValidation()
    {
        const string message = "Selecione datas de hoje em diante, dentro dos próximos dois anos. Error: Selecione datas de hoje em diante, dentro dos próximos dois anos. at readResponse (https://localhost/js/identity-access.js:27:19)";
        Assert.Equal("Selecione datas de hoje em diante, dentro dos próximos dois anos.",
            UiErrorNotifier.Normalize(message));
    }

    [Fact]
    public void MedicalRecordRoute_UsesAParameterCompatibleWithTheLongConstraint()
    {
        var parameter = typeof(ViverApp.Web.Components.Pages.MedicalRecordWorkspace)
            .GetProperty("PatientId");

        Assert.NotNull(parameter);
        Assert.Equal(typeof(long), parameter.PropertyType);
    }

    [Fact]
    public void ApplicationErrorBoundary_ContainsUnhandledComponentFailures()
    {
        Assert.True(typeof(Microsoft.AspNetCore.Components.Web.ErrorBoundary)
            .IsAssignableFrom(typeof(ViverApp.Web.Components.DesignSystem.ApplicationErrorBoundary)));
    }

    [Fact]
    public void ErrorDialog_PreservesAnExplanatoryPortugueseMessage()
    {
        const string message = "O CPF informado é inválido.";
        Assert.Equal(message, UiErrorNotifier.Normalize(message));
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
