using System.Text.Json;
using Microsoft.JSInterop;

namespace ViverApp.Web;

public static class BrowserApiResponseStream
{
    public const long MaximumResponseBytes = 1_048_576;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<PatientApiResult<T>> GetAsync<T>(IJSRuntime javaScript, string baseUrl,
        string path, CancellationToken cancellationToken = default)
    {
        await using var module = await javaScript.InvokeAsync<IJSObjectReference>(
            "import", cancellationToken, "/js/booking-api-stream.js");
        await using var reference = await module.InvokeAsync<IJSStreamReference>(
            "get", cancellationToken, baseUrl, path);
        await using var stream = await reference.OpenReadStreamAsync(MaximumResponseBytes, cancellationToken);
        return await JsonSerializer.DeserializeAsync<PatientApiResult<T>>(stream, JsonOptions, cancellationToken)
            ?? throw new JsonException("Resposta de dados inválida.");
    }
}
