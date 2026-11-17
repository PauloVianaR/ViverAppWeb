using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using ViverApp.Web.Components.Pages;
using Xunit;

namespace ViverApp.Web.Tests;

public sealed class AdministratorAccountEditorTests
{
    [Theory]
    [InlineData("patient")]
    [InlineData("doctor")]
    [InlineData("psychologist")]
    [InlineData("manager")]
    [InlineData("administrator")]
    public async Task CreationFormExposesRoleSpecificFieldsWithoutPasswordOrApproval(string role)
    {
        var html = await Render(role, false);
        Assert.Contains("Cadastrar usuário", html); Assert.Contains("sem aprovação posterior", html);
        Assert.Contains("id=\"account-email\"", html); Assert.Contains("Esqueci a senha", html);
        Assert.DoesNotContain("type=\"password\"", html);
        var staff = role is "doctor" or "psychologist" or "manager";
        Assert.Equal(staff, Regex.IsMatch(html, "id=\"account-phone\"[^>]*required"));
        var professional = role is "doctor" or "psychologist";
        Assert.Equal(professional, html.Contains("id=\"account-license\"", StringComparison.Ordinal));
        Assert.Equal(role == "patient", html.Contains("id=\"account-postal\"", StringComparison.Ordinal));
        if (professional) { Assert.Contains("value=\"10\"", html); Assert.Contains(role == "psychologist" ? "CRP" : "CRM", html); }
        if (role == "administrator") Assert.Contains("MFA", html);
    }

    [Fact]
    public async Task EditFormRetainsDataAndLocksTheRole()
    {
        var html = await Render("psychologist", true);
        Assert.Contains("Editar cadastro", html); Assert.Contains("QA35 Pessoa editável", html);
        Assert.Matches("id=\"account-role\"[^>]*disabled", html);
        Assert.Contains("Salvar cadastro", html); Assert.Contains("somente na criação", html);
    }

    private static async Task<string> Render(string role, bool existing)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Backend:BaseUrl"] = "https://localhost:7176/" }).Build();
        await using var services = new ServiceCollection().AddLogging().AddSingleton<IJSRuntime, NoJavaScript>()
            .AddSingleton<NavigationManager, Navigation>().AddSingleton<UiErrorNotifier>()
            .AddSingleton(WebBackendOptions.Load(config, new Environment())).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<EditorHarness>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(EditorHarness.TestRole)] = role, [nameof(EditorHarness.Existing)] = existing }));
            return WebUtility.HtmlDecode(output.ToHtmlString());
        });
    }

    public sealed class EditorHarness : AdministratorAccountEditor
    {
        [Parameter] public string TestRole { get; set; } = "patient";
        [Parameter] public bool Existing { get; set; }
        protected override Task OnParametersSetAsync()
        {
            Loading = false;
            Set("Ready", true); Set("RoleCode", TestRole); Set("FullName", "QA35 Pessoa editável");
            Set("Title", TestRole == "psychologist" ? "Psicóloga" : "Dra.");
            Set("Options", new WebRegistrationOptions("teste", "teste", [new(1, "Oftalmologia"), new(2, "Psicologia")]));
            if (Existing) Set("Item", new AdminAccountDetails(35, TestRole, "active", true, false, true, false, 1,
                new("QA35 Pessoa editável", "qa35@example.test", null, null, null, null, null)));
            return Task.CompletedTask;
        }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
        private void Set(string name, object value) => typeof(AdministratorAccountEditor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, value);
    }
    private sealed class Navigation : NavigationManager { public Navigation() => Initialize("https://localhost/", "https://localhost/administracao/usuarios/novo"); }
    private sealed class NoJavaScript : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => throw new NotSupportedException();
    }
    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ViverApp.Web.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
