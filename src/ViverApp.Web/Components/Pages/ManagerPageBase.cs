using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ViverApp.Web.Components.Pages;

public abstract class ManagerPageBase : ComponentBase, IAsyncDisposable
{
    [Inject] protected IJSRuntime JavaScript { get; set; } = null!;
    [Inject] protected WebBackendOptions Backend { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    protected IJSObjectReference? Module;
    protected bool Loading = true, Busy;
    protected string? Notice;
    protected bool NoticeError;
    private string? loadedUri;
    protected override async Task OnParametersSetAsync() { if (Module is not null && loadedUri != Navigation.Uri) { loadedUri = Navigation.Uri; await Run(Load); } }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return; loadedUri = Navigation.Uri;
        await Run(async () => { Module = await JavaScript.InvokeAsync<IJSObjectReference>("import", "/js/manager-experience.js"); await Load(); });
        Loading = false; StateHasChanged();
    }
    protected abstract Task Load();
    protected Task<T?> Get<T>(string path) => Request<T>(path, "GET", null);
    protected async Task<T?> Request<T>(string path, string method, object? data, string? key = null)
    {
        var result = await Module!.InvokeAsync<PatientApiResult<T>>("request", Backend.BaseUrl.ToString(), path, method, data, key);
        if (!result.Ok) { if (result.Status == 401) Navigation.NavigateTo("/acesso?estado=sessao-expirada", true); throw new ManagerUiException(result.Error ?? "Não foi possível concluir."); }
        return result.Data;
    }
    protected async Task Run(Func<Task> action) { if (Busy) return; Busy = true; Notice = null; NoticeError = false; try { await action(); } catch (ManagerUiException e) { Notice = e.Message; NoticeError = true; } catch (JSException) { Notice = "Não foi possível conectar. Confira sua conexão e tente novamente."; NoticeError = true; } finally { Busy = false; } }
    protected static string Escape(string value) => Uri.EscapeDataString(value);
    protected async Task Open(string id) => await Module!.InvokeVoidAsync("showDialog", id);
    protected async Task Close(string id) => await Module!.InvokeVoidAsync("closeDialog", id);
    public async ValueTask DisposeAsync() { if (Module is not null) try { await Module.DisposeAsync(); } catch (JSDisconnectedException) { } }
    protected sealed class ManagerUiException(string message) : Exception(message);
}
