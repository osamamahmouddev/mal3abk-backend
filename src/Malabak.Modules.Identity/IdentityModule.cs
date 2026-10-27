using Malabak.Modules.Identity.Application.Authentication.Logout;
using Malabak.Modules.Identity.Application.Authentication.LogoutAll;
using Malabak.Modules.Identity.Application.Authentication.RefreshToken;
using Malabak.Modules.Identity.Application.Authentication.SendOtp;
using Malabak.Modules.Identity.Application.Authentication.VerifyOtp;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure.Services;
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

        // Command Handlers
        services.AddScoped<ICommandHandler<SendOtpCommand, Result<SendOtpResponse>>, SendOtpHandler>();
        services.AddScoped<ICommandHandler<VerifyOtpCommand, Result<VerifyOtpResponse>>, VerifyOtpHandler>();
        services.AddScoped<ICommandHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>, RefreshTokenHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand, Result>, LogoutHandler>();
        services.AddScoped<ICommandHandler<LogoutAllCommand, Result>, LogoutAllHandler>();

        return services;
    }
}
