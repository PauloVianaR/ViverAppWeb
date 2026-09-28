using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;

if (args.Length != 4 || args[0] is not ("api-ready" or "landing" or "agenda") ||
    !Uri.TryCreate(args[1], UriKind.Absolute, out var origin) || !origin.IsLoopback ||
    origin.Scheme is not ("http" or "https") || origin.AbsolutePath != "/" ||
    !int.TryParse(args[2], CultureInfo.InvariantCulture, out var seconds) || seconds is < 1 or > 60 ||
    !int.TryParse(args[3], CultureInfo.InvariantCulture, out var concurrency) || concurrency is < 1 or > 32)
{
    Console.Error.WriteLine("Uso: ViverApp.LoadProbe <api-ready|landing|agenda> <http://localhost:porta/> <1-60 segundos> <1-32 clientes>. Somente loopback.");
    return 2;
}

var scenario = args[0];
var cookie = Environment.GetEnvironmentVariable("VIVERAPP_LOAD_SESSION_COOKIE");
if (scenario == "agenda" && string.IsNullOrWhiteSpace(cookie))
{
    Console.Error.WriteLine("Agenda exige uma sessão sintética de Gestor em VIVERAPP_LOAD_SESSION_COOKIE; não use conta real.");
    return 2;
}
var path = scenario switch
{
    "api-ready" => "health/ready",
    "landing" => "",
    _ => $"api/v1/manager/agenda?from={DateOnly.FromDateTime(DateTime.Today):yyyy-MM-dd}&to={DateOnly.FromDateTime(DateTime.Today):yyyy-MM-dd}&page=1&pageSize=12",
};

using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, MaxConnectionsPerServer = concurrency };
using var client = new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(10) };
var durations = new ConcurrentBag<double>();
var statuses = new ConcurrentDictionary<int, int>();
var deadline = Stopwatch.StartNew();
var issued = 0;
var maximumRequests = scenario == "api-ready" ? 10_000 : 250;
await Parallel.ForEachAsync(Enumerable.Range(0, concurrency), async (_, ct) =>
{
    while (deadline.Elapsed < TimeSpan.FromSeconds(seconds) && Volatile.Read(ref issued) < maximumRequests)
    {
        if (Interlocked.Increment(ref issued) > maximumRequests) break;
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (scenario == "agenda") request.Headers.TryAddWithoutValidation("Cookie", cookie);
        var watch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            statuses.AddOrUpdate((int)response.StatusCode, 1, (_, count) => count + 1);
            durations.Add(watch.Elapsed.TotalMilliseconds);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            statuses.AddOrUpdate(0, 1, (_, count) => count + 1);
        }
        if (scenario != "api-ready") await Task.Delay(TimeSpan.FromMilliseconds(concurrency * 100), ct);
    }
});
deadline.Stop();
var sorted = durations.Order().ToArray();
double Percentile(double fraction) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * fraction) - 1)];
var failures = statuses.Where(pair => pair.Key != (int)HttpStatusCode.OK).Sum(pair => pair.Value);
var report = new
{
    scenario,
    target = origin.GetLeftPart(UriPartial.Authority),
    durationSeconds = Math.Round(deadline.Elapsed.TotalSeconds, 2),
    concurrency,
    requests = statuses.Values.Sum(),
    successful = statuses.Where(pair => pair.Key is >= 200 and < 300).Sum(pair => pair.Value),
    failures,
    requestsPerSecond = Math.Round(statuses.Values.Sum() / deadline.Elapsed.TotalSeconds, 2),
    p50Milliseconds = Math.Round(Percentile(0.50), 2),
    p95Milliseconds = Math.Round(Percentile(0.95), 2),
    p99Milliseconds = Math.Round(Percentile(0.99), 2),
    statusCodes = statuses.OrderBy(pair => pair.Key).ToDictionary(pair => pair.Key.ToString(CultureInfo.InvariantCulture), pair => pair.Value),
};
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return failures == 0 && sorted.Length > 0 ? 0 : 1;
