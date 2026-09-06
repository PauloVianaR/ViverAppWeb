using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ViverApp.Web.Components.Pages;

public abstract class PatientPageBase : ComponentBase, IAsyncDisposable
{
    [Inject] protected IJSRuntime JavaScript { get; set; } = null!;
    [Inject] protected WebBackendOptions Backend { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    protected IJSObjectReference? Module;
    protected bool Loading = true;
    protected bool Busy;
    protected string? Notice;
    protected bool NoticeError;
    private string? loadedUri;
    protected override async Task OnParametersSetAsync()
    {
        if (Module is not null && loadedUri != Navigation.Uri)
        {
            loadedUri = Navigation.Uri;
            await Run(Load);
        }
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        loadedUri = Navigation.Uri;
        await Run(async () => { Module = await JavaScript.InvokeAsync<IJSObjectReference>("import", "/js/patient-experience.js"); await Load(); });
        Loading = false;
        StateHasChanged();
    }
    protected abstract Task Load();
    protected async Task<T?> Get<T>(string path) => await Request<T>(path, "GET", null);
    protected async Task<T?> Request<T>(string path, string method, object? data, string? key = null)
    {
        var result = await Module!.InvokeAsync<PatientApiResult<T>>("request", Backend.BaseUrl.ToString(), path, method, data, key);
        if (!result.Ok)
        {
            if (result.Status == 401) Navigation.NavigateTo("/acesso?estado=sessao-expirada", true);
            throw new PatientUiException(result.Error ?? "Não foi possível concluir. Tente novamente.");
        }
        return result.Data;
    }
    protected async Task Run(Func<Task> action)
    {
        if (Busy) return;
        Busy = true; Notice = null; NoticeError = false;
        try { await action(); }
        catch (PatientUiException error) { Notice = error.Message; NoticeError = true; }
        catch (JSException) { Notice = "Não foi possível conectar. Confira sua conexão e tente novamente."; NoticeError = true; }
        finally { Busy = false; }
    }
    protected async Task Download(Guid id) => await Run(async () =>
    {
        var ok = await Module!.InvokeAsync<bool>("download", Backend.BaseUrl.ToString(), $"api/v1/patient/experience/documents/{id}");
        if (!ok) throw new PatientUiException("O documento não está disponível ou seu acesso expirou.");
    });
    protected static string Escape(string value) => Uri.EscapeDataString(value);
    public virtual async ValueTask DisposeAsync()
    {
        if (Module is not null) try { await Module.DisposeAsync(); } catch (JSDisconnectedException) { }
    }
    protected sealed class PatientUiException(string message) : Exception(message);
}
