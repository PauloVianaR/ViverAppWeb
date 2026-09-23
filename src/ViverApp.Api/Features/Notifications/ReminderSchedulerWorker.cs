namespace ViverApp.Api.Features.Notifications;

internal sealed class ReminderSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<ReminderSchedulerWorker> logger) : BackgroundService
{
    private readonly string owner = $"scheduler-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Notifications:Scheduler:Enabled", false))
        {
            logger.LogInformation("Agendamento de lembretes externos desativado.");
            return;
        }

        var nextDiscovery = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            ulong? jobId = null;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var scheduler = scope.ServiceProvider.GetRequiredService<ReminderScheduler>();
                var now = clock.GetUtcNow().UtcDateTime;
                if (now >= nextDiscovery)
                {
                    var scheduled = await scheduler.DiscoverAsync(stoppingToken);
                    if (scheduled > 0)
                        logger.LogInformation("{JobCount} lembrete(s) novos foram agendados.", scheduled);
                    nextDiscovery = now.AddMinutes(5);
                }
                var job = await scheduler.ClaimAsync(owner, stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }
                jobId = job.Id;
                await scheduler.ProcessAsync(job.Id, owner, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Falha no scheduler de lembretes: {ErrorType}.",
                    exception.GetType().Name);
                if (jobId.HasValue)
                {
                    try
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var scheduler = scope.ServiceProvider.GetRequiredService<ReminderScheduler>();
                        await scheduler.MarkFailedAsync(jobId.Value, owner,
                            $"scheduler_{exception.GetType().Name.ToLowerInvariant()}", stoppingToken);
                    }
                    catch (Exception persistenceException) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogError("Falha ao registrar tentativa do job {JobId}: {ErrorType}.",
                            jobId.Value, persistenceException.GetType().Name);
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
