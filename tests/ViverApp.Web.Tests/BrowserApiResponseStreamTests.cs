using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using Xunit;

namespace ViverApp.Web.Tests;

public sealed class BrowserApiResponseStreamTests
{
    [Fact]
    public async Task ReadsPatientPageLargerThanTheCircuitMessageLimitAndDisposesReferences()
    {
        var patients = Enumerable.Range(1, 100).Select(id => new { accountId = id, fullName = new string('A', 500) }).ToArray();
        var json = JsonSerializer.Serialize(new { ok = true, status = 200, data = patients });
        Assert.True(Encoding.UTF8.GetByteCount(json) > 32768);
        var runtime = new StreamRuntime(json);

        var response = await BrowserApiResponseStream.GetAsync<JsonElement[]>(runtime,
            "https://api.example.test/", "api/v1/booking-workspace/patients?page=1&pageSize=100");

        Assert.True(response.Ok);
        Assert.Equal(100, response.Data!.Length);
        Assert.Equal(100, response.Data[^1].GetProperty("accountId").GetInt32());
        Assert.Equal(BrowserApiResponseStream.MaximumResponseBytes, runtime.Reference.RequestedLimit);
        Assert.True(runtime.Disposed);
        Assert.True(runtime.Reference.Disposed);
        Assert.False(runtime.Reference.Stream!.CanRead);
    }

    [Fact]
    public async Task PreservesSessionErrorsForThePageToHandle()
    {
        var runtime = new StreamRuntime("{\"ok\":false,\"status\":401,\"error\":\"Sessão expirada\"}");
        var response = await BrowserApiResponseStream.GetAsync<JsonElement>(runtime, "https://api.example.test/", "patients");
        Assert.False(response.Ok);
        Assert.Equal(401, response.Status);
        Assert.Equal("Sessão expirada", response.Error);
    }

    [Fact]
    public async Task RejectsOversizedResponsesAndStillReleasesTheBrowserReferences()
    {
        var runtime = new StreamRuntime(new string('A', (int)BrowserApiResponseStream.MaximumResponseBytes + 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            BrowserApiResponseStream.GetAsync<JsonElement>(runtime, "https://api.example.test/", "patients"));
        Assert.True(runtime.Disposed);
        Assert.True(runtime.Reference.Disposed);
    }

    private sealed class StreamRuntime(string json) : IJSRuntime, IJSObjectReference
    {
        public StreamReference Reference { get; } = new(json);
        public bool Disposed { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            cancellationToken.ThrowIfCancellationRequested();
            object result;
            if (identifier == "import")
            {
                Assert.Equal("/js/booking-api-stream.js", args![0]);
                result = this;
            }
            else
            {
                Assert.Equal("get", identifier);
                result = Reference;
            }
            return ValueTask.FromResult((TValue)result);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class StreamReference(string json) : IJSStreamReference
    {
        private readonly byte[] bytes = Encoding.UTF8.GetBytes(json);
        public long Length => bytes.Length;
        public long RequestedLimit { get; private set; }
        public bool Disposed { get; private set; }
        public MemoryStream? Stream { get; private set; }
        public ValueTask<Stream> OpenReadStreamAsync(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            RequestedLimit = maxAllowedSize;
            if (Length > maxAllowedSize) throw new ArgumentOutOfRangeException(nameof(maxAllowedSize));
            Stream = new MemoryStream(bytes, writable: false);
            return ValueTask.FromResult<Stream>(Stream);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
