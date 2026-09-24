using ViverApp.Api.Features.Identity;

namespace ViverApp.Api.Features.Notifications;

internal static class NotificationServiceCollectionExtensions
{
    public static IServiceCollection AddViverAppNotifications(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<OutboxStore>();
        services.AddScoped<NotificationPreferencesService>();
        services.AddScoped<NotificationRuleFilter>();
        services.AddScoped<NotificationDeliveryPolicy>();
        services.AddScoped<BusinessNotificationTemplate>();
        services.AddScoped<ReminderScheduler>();
        services.AddScoped<InternalNotificationHandler>();
        services.AddHostedService<InternalOutboxWorker>();

        var businessEnabled = configuration.GetValue("Notifications:BusinessDelivery:Enabled", false);
        var schedulerEnabled = configuration.GetValue("Notifications:Scheduler:Enabled", false);
        if (schedulerEnabled && !businessEnabled)
            throw new InvalidOperationException(
                "Notifications:Scheduler:Enabled exige Notifications:BusinessDelivery:Enabled.");
        if (businessEnabled)
        {
            if (!configuration.GetValue("Authentication:Delivery:Enabled", true))
                throw new InvalidOperationException(
                    "Notificações externas exigem provedores SMTP e SMSBarato configurados.");
            services.AddSingleton<INotificationEmailSender, SmtpNotificationSender>();
            services.AddHttpClient<INotificationSmsSender, SmsBaratoNotificationSender>(
                (provider, client) =>
                {
                    var options = provider.GetRequiredService<IdentityDeliveryOptions>();
                    client.BaseAddress = options.SmsBaratoBaseUrl;
                    client.Timeout = TimeSpan.FromSeconds(10);
                });
            services.AddHostedService<BusinessOutboxWorker>();
        }
        if (schedulerEnabled)
            services.AddHostedService<ReminderSchedulerWorker>();

        return services;
    }
}
