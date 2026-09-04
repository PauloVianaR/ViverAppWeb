namespace ViverApp.Api.Features.Payments;

internal sealed class PaymentReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    PagBankOptions options,
    ILogger<PaymentReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.ReconciliationIntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<PaymentService>();
                var processed = await service.ReconcileDueAsync(stoppingToken);
                if (processed > 0)
                {
                    logger.LogInformation("Reconciliação PagBank processou {PaymentCount} pagamento(s).", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha segura na reconciliação PagBank.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}

