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
using StackExchange.Redis;

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
        // SMS / WhatsApp: cost-controlled gateway with provider failover (Twilio accounts; Mailpit in Development)
        services.Configure<SmsOptions>(configuration.GetSection(SmsOptions.SectionName));
        if (SecurityConfiguration.IsDevelopment(configuration) && !configuration.GetSection($"{SmsOptions.SectionName}:Providers").Exists())
        {
            services.PostConfigure<SmsOptions>(o => o.Providers.Add(MailpitSmsProvider.ProviderName));
        }

        var isDevelopment = SecurityConfiguration.IsDevelopment(configuration);
        services.AddScoped<IEnumerable<ISmsProvider>>(sp =>
            SmsProviderFactory.Create(sp.GetRequiredService<IOptions<SmsOptions>>(), sp.GetRequiredService<IEmailSender>(), isDevelopment).ToList());
        services.AddScoped<ISmsGateway, SmsGateway>();
        services.AddScoped<ISmsAlertService, GatewaySmsAlertService>();
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

        // Player profile & onboarding
        services.AddScoped<Malabak.Modules.Identity.Application.Profile.ProfileHandlers>();
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

        // Redis / In-Memory OTP Store
        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisConnectionString))
        {
            try
            {
                var redis = ConnectionMultiplexer.Connect(redisConnectionString);
                services.AddSingleton<IConnectionMultiplexer>(redis);
                services.AddSingleton<IOtpStore, RedisOtpStore>();
            }
            catch
            {
                services.AddSingleton<IOtpStore, InMemoryOtpStore>();
            }
        }
        else
        {
            services.AddSingleton<IOtpStore, InMemoryOtpStore>();
        }

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
}
