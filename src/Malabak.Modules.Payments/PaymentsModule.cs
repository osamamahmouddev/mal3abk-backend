using Malabak.Modules.Payments.Application.Services;
using Malabak.Modules.Payments.Infrastructure.Paymob;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Malabak.Modules.Payments;

public static class PaymentsModule
{
    public const string Schema = "payments";

    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PaymobOptions>(options =>
        {
            var section = configuration.GetSection(PaymobOptions.SectionName);
            options.ApiKey = section["ApiKey"] ?? string.Empty;
            options.SecretKey = section["SecretKey"] ?? string.Empty;
            options.PublicKey = section["PublicKey"] ?? string.Empty;
            options.HmacSecret = section["HmacSecret"] ?? string.Empty;
            if (int.TryParse(section["CardIntegrationId"], out var cardId))
            {
                options.CardIntegrationId = cardId;
            }
            if (int.TryParse(section["WalletIntegrationId"], out var walletId))
            {
                options.WalletIntegrationId = walletId;
            }
            options.BaseUrl = section["BaseUrl"] ?? string.Empty;
        });
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
