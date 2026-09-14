using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ViverApp.Web.Components.DesignSystem;

public sealed class ApplicationErrorBoundary : ErrorBoundary
{
    [Inject] private UiErrorNotifier ErrorNotifier { get; set; } = null!;
    [Inject] private ILogger<ApplicationErrorBoundary> Logger { get; set; } = null!;

    protected override Task OnErrorAsync(Exception exception)
    {
        Logger.LogError(exception, "Falha não tratada contida pela barreira global da interface.");
        ErrorNotifier.Show(UiErrorNotifier.UnexpectedMessage);
        return Task.CompletedTask;
    }
}
