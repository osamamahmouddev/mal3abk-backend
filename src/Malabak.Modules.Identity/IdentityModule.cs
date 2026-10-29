using Malabak.Modules.Identity.Application.Authentication.Logout;
using Malabak.Modules.Identity.Application.Authentication.LogoutAll;
using Malabak.Modules.Identity.Application.Authentication.RefreshToken;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PhoneChange.RecoverPhone;
using Malabak.Modules.Identity.Application.PhoneChange.StartPhoneChange;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyNewPhone;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyOldPhone;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Malabak.Modules.Identity;

public static class IdentityModule
{
    public const string Schema = "identity";

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        // JWT & Token Options & Services
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<IJwtKeyProvider, JwtKeyProvider>();
        services.AddSingleton<ITokenService, TokenService>();

        // Core Identity Services
        services.AddSingleton<IPhoneNumberNormalizer, PhoneNumberNormalizer>();
        services.AddSingleton<ITurnstileValidator, TurnstileValidator>();
        services.AddSingleton<IOtpSecurityService, OtpSecurityService>();
        services.AddSingleton<IStepUpService, StepUpService>();

        AddOtpDelivery(services, configuration);

        // OTP codes live in Redis, through the host's single IConnectionMultiplexer (TLS, timeouts and
        // logging come from RedisConnectionFactory). No in-memory fallback: if Redis is down, OTP
        // requests fail closed instead of being checked per instance (SECURITY.md).
        services.AddSingleton<IOtpStore, RedisOtpStore>();

        // Command Handlers - Authentication
        services.AddScoped<ICommandHandler<SendOtpCommand, Result<SendOtpResponse>>, SendOtpHandler>();
        services.AddScoped<ICommandHandler<VerifyOtpCommand, Result<VerifyOtpResponse>>, VerifyOtpHandler>();
        services.AddScoped<ICommandHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>, RefreshTokenHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand, Result>, LogoutHandler>();
        services.AddScoped<ICommandHandler<LogoutAllCommand, Result>, LogoutAllHandler>();

        // Command Handlers - Phone Change State Machine
        services.AddScoped<ICommandHandler<StartPhoneChangeCommand, Result<StartPhoneChangeResponse>>, StartPhoneChangeHandler>();
        services.AddScoped<ICommandHandler<VerifyOldPhoneCommand, Result>, VerifyOldPhoneHandler>();
        services.AddScoped<ICommandHandler<VerifyNewPhoneCommand, Result<VerifyNewPhoneResponse>>, VerifyNewPhoneHandler>();
        services.AddScoped<ICommandHandler<RecoverPhoneCommand, Result>, RecoverPhoneHandler>();

        return services;
    }

    // OTP codes and security alerts share one provider switch, webhook and HttpClient.
    private static void AddOtpDelivery(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OtpDeliveryOptions>()
            .Bind(configuration.GetSection(OtpDeliveryOptions.SectionName))
            .Validate<IHostEnvironment>(
                (options, environment) => options.Provider != OtpDeliveryProvider.Slack || !environment.IsProduction(),
                "OTP delivery to Slack is for testing only and is not allowed in Production: everyone in the channel could read every code.")
            .Validate(
                options => options.Provider != OtpDeliveryProvider.Slack || IsSlackWebhookUrl(options.Slack.WebhookUrl),
                $"{OtpDeliveryOptions.SectionName}:Slack:WebhookUrl must be an https://hooks.slack.com/ Incoming Webhook URL.")
            .ValidateOnStart();

        var provider = configuration.GetSection(OtpDeliveryOptions.SectionName).GetValue<OtpDeliveryProvider>(nameof(OtpDeliveryOptions.Provider));
        if (provider == OtpDeliveryProvider.Slack)
        {
            services.AddHttpClient<SlackWebhookClient>(client => client.Timeout = TimeSpan.FromSeconds(10))
                // The default HttpClient logging writes the request URL, and the webhook URL is a credential.
                .RemoveAllLoggers();
            services.AddTransient<IOtpSender, SlackOtpSender>();
            services.AddTransient<ISmsAlertService, SlackSmsAlertService>();
        }
        else
        {
            services.AddSingleton<IOtpSender, UnavailableOtpSender>();
            services.AddSingleton<ISmsAlertService, UnavailableSmsAlertService>();
        }
    }

    private static bool IsSlackWebhookUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == "hooks.slack.com";
}
