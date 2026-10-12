using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Xunit;

namespace ViverApp.Web.Tests;

public sealed class AppointmentNavigationStoreTests
{
    [Fact]
    public async Task ReloadRestoresOnlyTheCurrentPageAndAccessArea()
    {
        var runtime = new StorageRuntime();
        var navigation = new TestNavigation("/gestao/agendar");
        var context = new AppointmentNavigationContext("/gestao", "day", new(2026, 10, 19), 42, "09:10");
        await using var store = new AppointmentNavigationStore(runtime, navigation);
        Assert.True(await store.SaveAsync("/gestao/agendar", context));
        Assert.Equal(context, await store.RestoreAsync("/gestao"));
        Assert.Null(await store.RestoreAsync("/medico"));
        Assert.False(await store.SaveAsync("/medico/agendar", context));
        await store.NavigateAsync("/gestao/agenda", context with { Time = null });
        Assert.Equal("http://localhost/gestao/agenda", navigation.Uri);
        Assert.Equal(context with { Time = null }, await store.RestoreAsync("/gestao"));
    }

    [Fact]
    public async Task StorageBlockedKeepsThePreviousQueryNavigationInsteadOfLosingContext()
    {
        var runtime = new StorageRuntime { CanWrite = false };
        var navigation = new TestNavigation("/gestao/agenda");
        var context = new AppointmentNavigationContext("/gestao", "day", new(2026, 10, 19), 42, "09:10");
        await using var store = new AppointmentNavigationStore(runtime, navigation);
        await store.NavigateAsync("/gestao/agendar", context);
        Assert.Contains("date=2026-10-19", navigation.Uri);
        Assert.Contains("time=09:10", navigation.Uri);
        Assert.Contains("professional=42", navigation.Uri);
        await store.NavigateAsync("/gestao/agenda", context with { Time = null }, replace: true);
        Assert.True(navigation.Replaced);
        Assert.Contains("view=day", navigation.Uri);
        await store.NavigateAsync("/gestao/agenda", null);
        Assert.Equal("http://localhost/gestao/agenda", navigation.Uri);
    }

    private sealed class TestNavigation : NavigationManager
    {
        public bool Replaced { get; private set; }
        public TestNavigation(string path) => Initialize("http://localhost/", "http://localhost" + path);
        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).ToString();
        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
            Replaced = options.ReplaceHistoryEntry;
            HistoryEntryState = options.HistoryEntryState;
        }
    }

    private sealed class StorageRuntime : IJSRuntime, IJSObjectReference
    {
        private readonly Dictionary<string, string> values = [];
        public bool CanWrite { get; init; } = true;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            object? result;
            if (identifier == "import") result = this;
            else if (identifier == "read") result = values.GetValueOrDefault((string)args![0]!);
            else if (identifier == "write")
            {
                if (CanWrite) values[(string)args![0]!] = (string)args[1]!;
                result = CanWrite;
            }
            else throw new InvalidOperationException(identifier);
            return ValueTask.FromResult(result is null ? default! : (TValue)result);
        }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
