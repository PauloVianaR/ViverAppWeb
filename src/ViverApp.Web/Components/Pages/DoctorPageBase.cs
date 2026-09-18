using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace ViverApp.Web.Components.Pages;

public abstract class DoctorPageBase : ComponentBase, IAsyncDisposable
{
    [Inject] protected IJSRuntime JavaScript { get; set; } = null!;
    [Inject] protected WebBackendOptions Backend { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected UiErrorNotifier ErrorNotifier { get; set; } = null!;
    protected IJSObjectReference? Module;
    protected bool Loading = true, Busy;
    protected string AppointmentViewMode = "cards";
    protected DoctorCapabilities Capabilities { get; private set; } = new(false);
    private bool appointmentViewLoaded;
    private string? notice;
    private bool noticeError;
    protected string? Notice { get => notice; set { notice = value; if (noticeError && value is not null) ErrorNotifier.Show(value); } }
    protected bool NoticeError { get => noticeError; set { noticeError = value; if (value && notice is not null) ErrorNotifier.Show(notice); } }
    private string? loadedUri;
    private bool disposed;
    protected override Task OnParametersSetAsync() => ReloadForLocationAsync(Navigation.Uri);
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return; loadedUri = Navigation.Uri;
        Navigation.LocationChanged += OnLocationChanged;
        await Run(async () => { Module = await JavaScript.InvokeAsync<IJSObjectReference>("import", "/js/doctor-experience.js"); Capabilities = await Get<DoctorCapabilities>("api/v1/doctor/capabilities") ?? new(false); await LoadAppointmentViewAsync(); await Load(); });
        Loading = false; StateHasChanged();
    }
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
        return InSameRouteGroup(currentPath, nextPath,
                "/medico/agenda", "/medico/historico")
            || InSameRouteGroup(currentPath, nextPath,
                "/medico/perfil", "/medico/disponibilidade");
    }
    private static bool InSameRouteGroup(string currentPath, string nextPath, string first, string second) =>
        (currentPath == first || currentPath == second) && (nextPath == first || nextPath == second);
    protected abstract Task Load();
    protected Task<T?> Get<T>(string path) => Request<T>(path, "GET", null);
    protected async Task LoadAppointmentViewAsync() { if (appointmentViewLoaded) return; var preference = await Get<AppointmentViewPreference>("api/v1/me/preferences/appointment-view"); AppointmentViewMode = preference?.Mode == "compact" ? "compact" : "cards"; appointmentViewLoaded = true; }
    protected Task ChangeAppointmentView(string mode) => Run(async () => { var preference = await Request<AppointmentViewPreference>("api/v1/me/preferences/appointment-view", "PUT", new { mode }); AppointmentViewMode = preference?.Mode == "compact" ? "compact" : "cards"; });
    protected async Task<T?> Request<T>(string path, string method, object? data, string? key = null)
    {
        var result = await Module!.InvokeAsync<PatientApiResult<T>>("request", Backend.BaseUrl.ToString(), path, method, data, key);
        if (!result.Ok) { if (result.Status == 401) Navigation.NavigateTo("/acesso?estado=sessao-expirada", true); throw new DoctorUiException(result.Error ?? "Não foi possível concluir."); }
        return result.Data;
    }
    protected async Task Run(Func<Task> action)
    {
        if (Busy) return; Busy = true; Notice = null; NoticeError = false;
        try { await action(); }
        catch (DoctorUiException e) { Notice = e.Message; NoticeError = true; }
        catch (JSException) { Notice = "Não foi possível conectar. Confira sua conexão e tente novamente."; NoticeError = true; }
        catch (Exception) { Notice = UiErrorNotifier.UnexpectedMessage; NoticeError = true; }
        finally { Busy = false; }
    }
    protected static string Escape(string value) => Uri.EscapeDataString(value);
    protected async Task Open(string id) => await Module!.InvokeVoidAsync("showDialog", id);
    protected async Task Close(string id) => await Module!.InvokeVoidAsync("closeDialog", id);
    public virtual async ValueTask DisposeAsync() { disposed = true; Navigation.LocationChanged -= OnLocationChanged; if (Module is not null) try { await Module.DisposeAsync(); } catch (JSDisconnectedException) { } }
    protected sealed class DoctorUiException(string message) : Exception(message);
}
