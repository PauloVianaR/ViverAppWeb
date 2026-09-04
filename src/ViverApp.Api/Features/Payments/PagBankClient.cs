using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ViverApp.Api.Features.Payments;

public interface IPagBankClient
{
    Task<PagBankResource> CreateCheckoutAsync(PagBankCheckoutCommand command, string idempotencyKey, CancellationToken cancellationToken);
    Task<PagBankResource> GetCheckoutAsync(string checkoutId, CancellationToken cancellationToken);
    Task<PagBankResource> InactivateCheckoutAsync(string checkoutId, CancellationToken cancellationToken);
    Task<PagBankResource> RefundChargeAsync(string chargeId, long amountCents, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed record PagBankCheckoutCommand(
    string ReferenceId,
    DateTimeOffset ExpirationDate,
    string ItemReferenceId,
    string ItemName,
    long UnitAmount,
    Uri RedirectUrl,
    Uri ReturnUrl,
    Uri WebhookUrl);

public sealed record PagBankResource(
    string Id,
    string? ReferenceId,
    string Status,
    Uri? PayUrl,
    DateTime? OccurredAtUtc,
    DateTime? ExpiresAtUtc,
    long? TotalCents,
    long? RefundedCents,
    string RawKind);

internal sealed class PagBankClient(HttpClient httpClient, PagBankOptions options) : IPagBankClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<PagBankResource> CreateCheckoutAsync(
        PagBankCheckoutCommand command,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Post,
            "checkouts",
            new
            {
                reference_id = command.ReferenceId,
                expiration_date = command.ExpirationDate,
                items = new[]
                {
                    new
                    {
                        reference_id = command.ItemReferenceId,
                        name = command.ItemName,
                        quantity = 1,
                        unit_amount = command.UnitAmount,
                    },
                },
                redirect_url = command.RedirectUrl,
                redirect_waiting_time = 5,
                return_url = command.ReturnUrl,
                notification_urls = new[] { command.WebhookUrl },
                payment_notification_urls = new[] { command.WebhookUrl },
            },
            idempotencyKey,
            retry: true,
            cancellationToken);

    public Task<PagBankResource> GetCheckoutAsync(string checkoutId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, $"checkouts/{Uri.EscapeDataString(checkoutId)}", null, null, retry: true, cancellationToken);

    public Task<PagBankResource> InactivateCheckoutAsync(string checkoutId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"checkouts/{Uri.EscapeDataString(checkoutId)}/inactivate", null, null, retry: false, cancellationToken);

    public Task<PagBankResource> RefundChargeAsync(
        string chargeId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        SendAsync(
            HttpMethod.Post,
            $"charges/{Uri.EscapeDataString(chargeId)}/cancel",
            new { amount = new { value = amountCents, currency = "BRL" } },
            idempotencyKey,
            retry: false,
            cancellationToken);

    private async Task<PagBankResource> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        string? idempotencyKey,
        bool retry,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            throw new PaymentRuleException(StatusCodes.Status503ServiceUnavailable, "Pagamentos estão temporariamente indisponíveis.");
        }

        var attempts = retry ? 3 : 1;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                request.Headers.TryAddWithoutValidation("x-idempotency-key", idempotencyKey);
            }

            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            try
            {
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    return PagBankResourceParser.Parse(document.RootElement);
                }

                if (attempt < attempts && IsTransient(response.StatusCode))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
                    continue;
                }

                throw new PaymentRuleException(
                    response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                        ? StatusCodes.Status422UnprocessableEntity
                        : StatusCodes.Status502BadGateway,
                    "O PagBank não aceitou a operação. Nenhuma credencial ou detalhe sensível foi exposto.");
            }
            catch (HttpRequestException) when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken);
            }
        }

        throw new PaymentRuleException(StatusCodes.Status502BadGateway, "O PagBank está temporariamente indisponível.");
    }

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
}

internal static class PagBankResourceParser
{
    public static PagBankResource Parse(JsonElement root)
    {
        var checkout = FindObject(root, element => ReadString(element, "id")?.StartsWith("CHEC_", StringComparison.Ordinal) == true);
        var charge = FindLatestCharge(root);
        var selected = charge ?? checkout ?? root;
        var id = ReadString(selected, "id") ?? throw Invalid();
        var status = ReadString(selected, "status") ?? ReadString(root, "status") ?? throw Invalid();
        var reference = ReadString(selected, "reference_id") ?? ReadString(root, "reference_id");
        var amount = selected.TryGetProperty("amount", out var amountElement) ? amountElement : default;
        var summary = amount.ValueKind == JsonValueKind.Object && amount.TryGetProperty("summary", out var summaryElement)
            ? summaryElement
            : default;
        var total = ReadInt64(amount, "value") ?? ReadInt64(summary, "total");
        var refunded = ReadInt64(summary, "refunded");
        var occurred = ReadDateTime(selected, "paid_at")
            ?? ReadDateTime(selected, "created_at")
            ?? ReadDateTime(root, "created_at");
        var expires = ReadDateTime(root, "expiration_date");
        return new PagBankResource(
            id,
            reference,
            status,
            ReadPayUrl(root),
            occurred,
            expires,
            total,
            refunded,
            charge.HasValue ? "charge" : "checkout");
    }

    private static JsonElement? FindObject(JsonElement element, Func<JsonElement, bool> predicate)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (predicate(element))
            {
                return element;
            }

            foreach (var property in element.EnumerateObject())
            {
                var match = FindObject(property.Value, predicate);
                if (match.HasValue)
                {
                    return match;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var match = FindObject(item, predicate);
                if (match.HasValue)
                {
                    return match;
                }
            }
        }

        return null;
    }

    private static JsonElement? FindLatestCharge(JsonElement root)
    {
        JsonElement? selected = null;
        DateTime selectedTime = DateTime.MinValue;
        Visit(root);
        return selected;

        void Visit(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (ReadString(element, "id")?.StartsWith("CHAR_", StringComparison.Ordinal) == true)
                {
                    var occurred = ReadDateTime(element, "paid_at")
                        ?? ReadDateTime(element, "created_at")
                        ?? DateTime.MinValue;
                    if (!selected.HasValue || occurred >= selectedTime)
                    {
                        selected = element;
                        selectedTime = occurred;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    Visit(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item);
                }
            }
        }
    }

    private static Uri? ReadPayUrl(JsonElement root)
    {
        if (!root.TryGetProperty("links", out var links) || links.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var link in links.EnumerateArray())
        {
            if (string.Equals(ReadString(link, "rel"), "PAY", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(ReadString(link, "href"), UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && string.IsNullOrEmpty(uri.UserInfo))
            {
                return uri;
            }
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? ReadInt64(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.TryGetInt64(out var result)
            ? result
            : null;

    private static DateTime? ReadDateTime(JsonElement element, string name) =>
        DateTimeOffset.TryParse(ReadString(element, name), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var value)
            ? value.UtcDateTime
            : null;

    private static PaymentRuleException Invalid() =>
        new(StatusCodes.Status502BadGateway, "O PagBank retornou uma resposta inválida.");
}
