using System.Security.Cryptography;
using Malabak.Modules.Identity.Application.AdminAuth;
using Malabak.Modules.Identity.Application.Authentication.Logout;
using Malabak.Modules.Identity.Application.Authentication.LogoutAll;
using Malabak.Modules.Identity.Application.Authentication.RefreshToken;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Authorization;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Application.StepUp;
using Malabak.Modules.Identity.Application.PhoneChange.RecoverPhone;
using Malabak.Modules.Identity.Application.PhoneChange.StartPhoneChange;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyNewPhone;
using Malabak.Modules.Identity.Application.PhoneChange.VerifyOldPhone;
using Malabak.Modules.Identity.Infrastructure.Services;
using Malabak.Modules.Identity.Infrastructure.Sms;
using Microsoft.Extensions.Options;
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
        AddSecurityOptions(services, configuration);

        // JWT & Token Services
        services.AddSingleton<IJwtKeyProvider, JwtKeyProvider>();
        services.AddSingleton<ITokenService, TokenService>();

        // Core Identity Services
        services.AddSingleton<IPhoneNumberNormalizer, PhoneNumberNormalizer>();
        services.AddSingleton<ITurnstileValidator, TurnstileValidator>();
        services.AddSingleton<IOtpSecurityService, OtpSecurityService>();
        services.AddSingleton<IStepUpService, StepUpService>();

        // Twilio delivery receipts are accepted whichever OTP provider is active; the gateway itself is
        // registered by AddOtpDelivery when Identity:OtpDelivery:Provider is Sms.
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));
        services.AddScoped<SmsDeliveryReceipts>();
        services.AddScoped<EmailBounceHandler>();

        // Password authentication (venue owners / staff)
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IBreachedPasswordChecker, HibpBreachedPasswordChecker>();
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddSingleton<ITotpService, TotpService>();
        services.AddScoped<ICommandHandler<PasswordLoginCommand, Result<PasswordLoginResponse>>, PasswordLoginHandler>();
        services.AddScoped<PasswordAccountHandlers>();
        services.AddScoped<ICommandHandler<RegisterVenueOwnerCommand, Result>>(sp => sp.GetRequiredService<PasswordAccountHandlers>());
        services.AddScoped<ICommandHandler<VerifyEmailCommand, Result>>(sp => sp.GetRequiredService<PasswordAccountHandlers>());
        services.AddScoped<ICommandHandler<ResendVerificationCommand, Result>>(sp => sp.GetRequiredService<PasswordAccountHandlers>());
        services.AddScoped<ICommandHandler<ForgotPasswordCommand, Result>>(sp => sp.GetRequiredService<PasswordAccountHandlers>());
        services.AddScoped<ICommandHandler<ResetPasswordCommand, Result>>(sp => sp.GetRequiredService<PasswordAccountHandlers>());
        services.AddScoped<ICommandHandler<ChangePasswordCommand, Result>>(sp => sp.GetRequiredService<PasswordAccountHandlers>());
        services.AddScoped<ICommandHandler<EnrollTotpCommand, Result<TotpEnrollmentResponse>>>(sp => sp.GetRequiredService<PasswordAccountHandlers>());
        services.AddScoped<ICommandHandler<ConfirmTotpCommand, Result>>(sp => sp.GetRequiredService<PasswordAccountHandlers>());

        // Admin authentication (separate audience, mandatory TOTP)
        services.AddScoped<AdminAuthHandlers>();
        services.AddScoped<ICommandHandler<AdminLoginCommand, Result<AdminLoginResponse>>>(sp => sp.GetRequiredService<AdminAuthHandlers>());
        services.AddScoped<ICommandHandler<AdminEnrollTotpCommand, Result<TotpEnrollmentResponse>>>(sp => sp.GetRequiredService<AdminAuthHandlers>());
        services.AddScoped<ICommandHandler<AdminConfirmTotpCommand, Result<IReadOnlyList<string>>>>(sp => sp.GetRequiredService<AdminAuthHandlers>());
        services.AddScoped<ICommandHandler<AdminRefreshCommand, Result<AdminTokens>>>(sp => sp.GetRequiredService<AdminAuthHandlers>());
        services.AddScoped<ICommandHandler<AdminLogoutCommand, Result>>(sp => sp.GetRequiredService<AdminAuthHandlers>());
        services.AddScoped<IQueryHandler<ListAdminSessionsQuery, Result<IReadOnlyList<AdminSessionDto>>>>(sp => sp.GetRequiredService<AdminAuthHandlers>());
        services.AddScoped<ICommandHandler<RevokeAdminSessionCommand, Result>>(sp => sp.GetRequiredService<AdminAuthHandlers>());

        // Contract used by other modules (e.g. Venues staff invitations)
        services.AddScoped<Malabak.SharedKernel.Contracts.IUserAccountsContract, UserAccountsContract>();

        // Device sessions (sid claim checked on every request; IDeviceContext is provided by the API host)
        services.AddScoped<ISessionStore, RedisSessionStore>();
        services.AddScoped<Malabak.SharedKernel.Contracts.ISessionActivityChecker, SessionActivityChecker>();
        services.AddScoped<SessionService>();
        services.AddScoped<Malabak.Modules.Identity.Application.Admin.AdminUserService>();
        services.AddScoped<Malabak.Modules.Identity.Application.Admin.AdminAccountService>();
        services.AddScoped<Malabak.SharedKernel.Contracts.IAdminMetricsProvider, Malabak.Modules.Identity.Application.Admin.UserMetricsProvider>();
        services.AddScoped<Malabak.Modules.Identity.Application.Waitlist.IWaitlistService, Malabak.Modules.Identity.Application.Waitlist.WaitlistService>();

        // Player profile & onboarding
        services.AddScoped<Malabak.Modules.Identity.Application.Profile.ProfileHandlers>();
        services.AddScoped<Malabak.Modules.Identity.Application.Privacy.PrivacyHandlers>();
        services.AddScoped<Malabak.Modules.Identity.Application.Privacy.AccountAnonymizer>();
        services.AddScoped<ICommandHandler<Malabak.Modules.Identity.Application.Profile.CompleteProfileCommand, Result<Malabak.Modules.Identity.Application.Profile.MyProfileDto>>>(sp => sp.GetRequiredService<Malabak.Modules.Identity.Application.Profile.ProfileHandlers>());
        services.AddScoped<ICommandHandler<Malabak.Modules.Identity.Application.Profile.SetLanguageCommand, Result>>(sp => sp.GetRequiredService<Malabak.Modules.Identity.Application.Profile.ProfileHandlers>());
        services.AddScoped<ICommandHandler<Malabak.Modules.Identity.Application.Profile.UpdateMyProfileCommand, Result<Malabak.Modules.Identity.Application.Profile.MyProfileDto>>>(sp => sp.GetRequiredService<Malabak.Modules.Identity.Application.Profile.ProfileHandlers>());
        services.AddScoped<ICommandHandler<Malabak.Modules.Identity.Application.Profile.SetAvatarCommand, Result<Malabak.Modules.Identity.Application.Profile.MyProfileDto>>>(sp => sp.GetRequiredService<Malabak.Modules.Identity.Application.Profile.ProfileHandlers>());
        services.AddScoped<Malabak.Modules.Identity.Application.Profile.IPlayerStatsSummaryProvider, Malabak.Modules.Identity.Application.Profile.NoPlayerStatsSummaryProvider>();

        // Step-up re-authentication for sensitive actions
        services.AddScoped<StepUpHandlers>();
        services.AddScoped<ICommandHandler<SendStepUpOtpCommand, Result>>(sp => sp.GetRequiredService<StepUpHandlers>());
        services.AddScoped<ICommandHandler<VerifyStepUpCommand, Result<StepUpTokenResponse>>>(sp => sp.GetRequiredService<StepUpHandlers>());
        services.AddScoped<IStepUpVerifier>(sp => sp.GetRequiredService<StepUpHandlers>());

        // Authorization (roles/permissions are read from the database on each request)
        services.AddScoped<IUserPermissionService, UserPermissionService>();

        AddOtpDelivery(services, configuration);

        // OTP codes live in Redis, through the host's single IConnectionMultiplexer (TLS, timeouts and
        // logging come from RedisConnectionFactory). No in-memory fallback: if Redis is down, OTP
        // requests fail closed instead of being checked per instance (SECURITY.md).
        services.AddSingleton<IOtpStore, RedisOtpStore>();
        services.AddSingleton<Malabak.Modules.Identity.Application.Admin.IImpersonationCodeStore, RedisImpersonationCodeStore>();
        services.AddScoped<Malabak.Modules.Identity.Application.Admin.ImpersonationService>();

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

    // Secrets fall back to built-in values only in Development/Testing. Everywhere else, Staging included,
    // a missing or placeholder secret fails startup instead of silently weakening auth (SECURITY.md).
    private static void AddSecurityOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate<IHostEnvironment>(
                (options, environment) => !string.IsNullOrWhiteSpace(options.PrivateKeyPem) || IdentityEnvironment.AllowsDevelopmentShortcuts(environment),
                $"{JwtOptions.SectionName}:PrivateKeyPem is required outside Development/Testing: without it every instance signs tokens with its own throwaway key.")
            .Validate(
                options => string.IsNullOrWhiteSpace(options.PrivateKeyPem) || IsP256PrivateKeyPem(options.PrivateKeyPem),
                $"{JwtOptions.SectionName}:PrivateKeyPem must be a PEM-encoded ECDSA P-256 private key.")
            .ValidateOnStart();

        services.AddOptions<OtpSecurityOptions>()
            .Bind(configuration.GetSection(OtpSecurityOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                if (string.IsNullOrWhiteSpace(options.HmacSecret) && IdentityEnvironment.AllowsDevelopmentShortcuts(environment))
                {
                    options.HmacSecret = DevelopmentSecrets.OtpHmacSecret;
                }
            })
            .Validate(
                options => IsStrongSecret(options.HmacSecret),
                $"{OtpSecurityOptions.SectionName}:HmacSecret is required outside Development/Testing and must be at least {_minimumSecretLength} characters.")
            .ValidateOnStart();

        services.AddOptions<StepUpOptions>()
            .Bind(configuration.GetSection(StepUpOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                if (string.IsNullOrWhiteSpace(options.SecretKey) && IdentityEnvironment.AllowsDevelopmentShortcuts(environment))
                {
                    options.SecretKey = DevelopmentSecrets.StepUpSecretKey;
                }
            })
            .Validate(
                options => IsStrongSecret(options.SecretKey),
                $"{StepUpOptions.SectionName}:SecretKey is required outside Development/Testing and must be at least {_minimumSecretLength} characters.")
            .ValidateOnStart();

        services.AddOptions<TurnstileOptions>()
            .Bind(configuration.GetSection(TurnstileOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                if (string.IsNullOrWhiteSpace(options.SecretKey) && IdentityEnvironment.AllowsDevelopmentShortcuts(environment))
                {
                    options.SecretKey = DevelopmentSecrets.TurnstileAlwaysPassSecretKey;
                }
            })
            .ValidateDataAnnotations()
            .Validate<IHostEnvironment>(
                (options, environment) => !options.BypassInDevelopment || IdentityEnvironment.AllowsDevelopmentShortcuts(environment),
                $"{TurnstileOptions.SectionName}:BypassInDevelopment is strictly prohibited outside of Development/Testing.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.SecretKey),
                $"{TurnstileOptions.SectionName}:SecretKey is required outside Development/Testing.")
            .Validate<IHostEnvironment>(
                (options, environment) => !IsTurnstileTestSecretKey(options.SecretKey) || IdentityEnvironment.AllowsDevelopmentShortcuts(environment),
                $"{TurnstileOptions.SectionName}:SecretKey must not be a Cloudflare test secret key outside Development/Testing.")
            .ValidateOnStart();
    }

    private const int _minimumSecretLength = 32;

    private static bool IsStrongSecret(string? secret) =>
        !string.IsNullOrWhiteSpace(secret) && secret.Length >= _minimumSecretLength;

    // Cloudflare's published test secrets: 1x always passes, 2x always fails, 3x reports the token as spent.
    private static bool IsTurnstileTestSecretKey(string? secretKey) =>
        secretKey is "1x0000000000000000000000000000000AA" or "2x0000000000000000000000000000000AA" or "3x0000000000000000000000000000000AA";

    private static bool IsP256PrivateKeyPem(string pem)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            return ecdsa.ExportParameters(includePrivateParameters: true).Curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value;
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            return false;
        }
    }

    // Known values, so never usable outside Development/Testing (see AddSecurityOptions).
    private static class DevelopmentSecrets
    {
        public const string OtpHmacSecret = "Malabak-Development-Only-Otp-HMAC-SHA256-Key!";
        public const string StepUpSecretKey = "Malabak-Development-Only-StepUp-HMAC-SHA256-Key!";
        public const string TurnstileAlwaysPassSecretKey = "1x0000000000000000000000000000000AA";
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
        else if (provider == OtpDeliveryProvider.Sms)
        {
            // Cost-controlled gateway with provider failover (Twilio accounts in Sms:Providers order). In
            // Development with no providers configured, messages go to the local Mailpit inbox instead.
            services.AddOptions<SmsOptions>()
                .PostConfigure<IHostEnvironment>((options, environment) =>
                {
                    if (environment.IsDevelopment() && options.Providers.Count == 0)
                    {
                        options.Providers.Add(MailpitSmsProvider.ProviderName);
                    }
                });
            services.AddScoped<IEnumerable<ISmsProvider>>(sp => SmsProviderFactory.Create(
                sp.GetRequiredService<IOptions<SmsOptions>>(),
                sp.GetRequiredService<IEmailSender>(),
                sp.GetRequiredService<IHostEnvironment>().IsDevelopment()).ToList());
            services.AddScoped<ISmsGateway, SmsGateway>();
            services.AddScoped<IOtpSender, SmsOtpSender>();
            services.AddScoped<ISmsAlertService, SmsSecurityAlertService>();
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
