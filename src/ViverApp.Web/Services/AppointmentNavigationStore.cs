using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ViverApp.Web;

public sealed class AppointmentNavigationStore(IJSRuntime javaScript, NavigationManager navigation) : IAsyncDisposable
{
    private IJSObjectReference? module;
    private async Task<IJSObjectReference> Module() => module ??= await javaScript.InvokeAsync<IJSObjectReference>(
        "import", "/js/appointment-navigation.js");

    public async Task<AppointmentNavigationContext?> RestoreAsync(string root, string? legacyReturn = null)
    {
        var context = AppointmentNavigation.Restore(root, navigation.HistoryEntryState)
            ?? AppointmentNavigation.LegacyContext(root, legacyReturn);
        if (context is not null) return context;
        var stored = await (await Module()).InvokeAsync<string?>("read", new Uri(navigation.Uri).AbsolutePath);
        return AppointmentNavigation.Restore(root, stored);
    }

    public async Task<bool> SaveAsync(string path, AppointmentNavigationContext? context)
    {
        if (context is null) return false;
        if (!path.StartsWith(context.Root + "/", StringComparison.Ordinal)
            || path.Contains('?') || path.Contains('#') || path.Contains('\\')
            || AppointmentNavigation.Restore(context.Root, AppointmentNavigation.State(context)) is null) return false;
        return await (await Module()).InvokeAsync<bool>("write", path, AppointmentNavigation.State(context));
    }

    public async Task NavigateAsync(string path, AppointmentNavigationContext? context, bool replace = false)
    {
        var destination = context is null || await SaveAsync(path, context)
            ? path : AppointmentNavigation.QueryFallback(path, context);
        navigation.NavigateTo(destination, new NavigationOptions
        {
            ReplaceHistoryEntry = replace,
            HistoryEntryState = context is null ? null : AppointmentNavigation.State(context),
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null) try { await module.DisposeAsync(); } catch (JSDisconnectedException) { }
    }
}
