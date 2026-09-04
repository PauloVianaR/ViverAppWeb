namespace ViverApp.Api.Features.Payments;

public static class PaymentServiceCollectionExtensions
{
    public static IServiceCollection AddViverAppPayments(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = PagBankOptions.Load(configuration, environment);
        services.AddSingleton(options);
        services.AddHttpClient<IPagBankClient, PagBankClient>(client =>
        {
            client.BaseAddress = options.ApiBaseUrl;
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IPaymentAuditWriter, PaymentAuditWriter>();
        services.AddScoped<PaymentService>();
        services.AddHostedService<PaymentReconciliationWorker>();
        return services;
    }
}

