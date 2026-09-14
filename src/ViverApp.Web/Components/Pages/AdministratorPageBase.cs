using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace ViverApp.Web.Components.Pages;

public abstract class AdministratorPageBase : ComponentBase, IAsyncDisposable
{
    [Inject] protected IJSRuntime JavaScript { get; set; } = null!;
    [Inject] protected WebBackendOptions Backend { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected UiErrorNotifier ErrorNotifier { get; set; } = null!;
    protected IJSObjectReference? Module;
    protected bool Loading = true, Busy;
    private string? notice;
    private bool noticeError;
    protected string? Notice { get => notice; set { notice = value; if (noticeError && value is not null) ErrorNotifier.Show(value); } }
    protected bool NoticeError { get => noticeError; set { noticeError = value; if (value && notice is not null) ErrorNotifier.Show(notice); } }
    private string? loadedUri;
    private bool disposed;
    protected override Task OnParametersSetAsync() => ReloadForLocationAsync(Navigation.Uri);
    protected override async Task OnAfterRenderAsync(bool firstRender) { if (!firstRender) return; loadedUri = Navigation.Uri; Navigation.LocationChanged += OnLocationChanged; await Run(async () => { Module = await JavaScript.InvokeAsync<IJSObjectReference>("import", "/js/administrator-experience.js"); await Load(); }); Loading = false; StateHasChanged(); }
    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        var currentPath = new Uri(loadedUri ?? Navigation.Uri).AbsolutePath;
        if (disposed || !HandlesLocation(currentPath, new Uri(args.Location).AbsolutePath)) return;
        _ = InvokeAsync(() => ReloadForLocationAsync(args.Location));
    }
    private async Task ReloadForLocationAsync(string uri)
    {
        if (disposed || Module is null || loadedUri == uri || Busy) return;
        loadedUri = uri;
        await Run(Load);
        if (!disposed) StateHasChanged();
    }
    protected virtual bool HandlesLocation(string currentPath, string nextPath)
    {
        return currentPath is "/administracao/consultas" or "/administracao/consultas/historico"
            && nextPath is "/administracao/consultas" or "/administracao/consultas/historico";
    }
    protected abstract Task Load();
    protected Task<T?> Get<T>(string path) => Request<T>(path, "GET", null);
    protected async Task<T?> Request<T>(string path, string method, object? data, string? key = null)
    {
        var result = await Module!.InvokeAsync<PatientApiResult<T>>("request", Backend.BaseUrl.ToString(), path, method, data, key);
        if (!result.Ok) { if (result.Status == 401) Navigation.NavigateTo("/acesso?estado=sessao-expirada", true); if (result.Status == 403 && result.Error?.Contains("Confirme", StringComparison.OrdinalIgnoreCase) == true) Notice = "Sessão elevada expirada. Entre novamente com MFA e retorne para concluir a ação."; throw new AdministratorUiException(result.Error ?? "Não foi possível concluir."); }
        return result.Data;
    }
    protected async Task Run(Func<Task> action) { if (Busy) return; Busy = true; Notice = null; NoticeError = false; try { await action(); } catch (AdministratorUiException e) { Notice = e.Message; NoticeError = true; } catch (JSException) { Notice = "Não foi possível conectar. Confira sua conexão e tente novamente."; NoticeError = true; } catch (Exception) { Notice = UiErrorNotifier.UnexpectedMessage; NoticeError = true; } finally { Busy = false; } }
    protected static string Escape(string value) => Uri.EscapeDataString(value);
    protected async Task Open(string id) => await Module!.InvokeVoidAsync("showDialog", id);
    protected async Task Close(string id) => await Module!.InvokeVoidAsync("closeDialog", id);
    public async ValueTask DisposeAsync() { disposed = true; Navigation.LocationChanged -= OnLocationChanged; if (Module is not null) try { await Module.DisposeAsync(); } catch (JSDisconnectedException) { } }
    private sealed class AdministratorUiException(string message) : Exception(message);
}
