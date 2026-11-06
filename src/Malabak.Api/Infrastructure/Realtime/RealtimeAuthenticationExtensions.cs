using Malabak.Modules.Identity.Application.Sessions;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Configuration;
using Malabak.Api.Infrastructure.Redis;
using Malabak.Modules.Identity.Application.Common;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Caching;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Malabak.Api.Infrastructure.Realtime;

public sealed record WsTicketResult(string Ticket, int ExpiresInSeconds);

public interface ITicketService
{
    Task<WsTicketResult> CreateTicketAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}

public sealed class TicketService : ITicketService
{
    private readonly IJwtKeyProvider _keyProvider;
    private readonly IOptions<JwtOptions> _jwtOptions;
    private readonly IAtomicConsumeStore _atomicStore;
    private readonly IClock _clock;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public TicketService(
        IJwtKeyProvider keyProvider,
        IOptions<JwtOptions> jwtOptions,
        IAtomicConsumeStore atomicStore,
        IClock clock)
    {
        _keyProvider = keyProvider;
        _jwtOptions = jwtOptions;
        _atomicStore = atomicStore;
        _clock = clock;
    }

    public async Task<WsTicketResult> CreateTicketAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var userId = user.GetUserId() ?? throw new InvalidOperationException("User id claim is required to create a WS ticket.");
        var sessionId = user.GetSessionId();
        var nowUtc = _clock.UtcNow;
        var expiresAtUtc = nowUtc.AddSeconds(60);
        var jti = Guid.NewGuid().ToString("N");

        // 1. Store single-use token in Redis with 60s TTL
        await _atomicStore.SetAsync($"wsjti:{jti}", userId.ToString("D"), TimeSpan.FromSeconds(60), cancellationToken);

        // 2. Issue minimal ES256 JWT ticket
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(nowUtc).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        if (sessionId.HasValue)
        {
            claims.Add(new Claim("session_id", sessionId.Value.ToString("D")));
        }

        claims.AddRange(user.FindAll(ClaimTypes.Role));

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _jwtOptions.Value.Issuer,
            Audience = RealtimeAuthenticationExtensions.WsAudience,
            NotBefore = nowUtc,
            IssuedAt = nowUtc,
            Expires = expiresAtUtc,
            SigningCredentials = _keyProvider.GetCurrentSigningCredentials()
        };

        var token = _tokenHandler.WriteToken(_tokenHandler.CreateToken(tokenDescriptor));
        return new WsTicketResult(token, 60);
    }
}

public static class RealtimeAuthenticationExtensions
{
    public const string WsTicketScheme = "WsTicket";
    public const string WsAudience = "malabak-ws";

    public static IServiceCollection AddRealtimeInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ITicketService, TicketService>();
        services.AddScoped<BookingRealtimeBroadcaster>();
        services.AddScoped<Malabak.Modules.Booking.Application.IBookingRealtimeBroadcaster>(sp =>
            sp.GetRequiredService<BookingRealtimeBroadcaster>());

        // Configure SignalR
        var signalRBuilder = services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 16 * 1024; // 16 KB max payload
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(45);
            options.HandshakeTimeout = TimeSpan.FromSeconds(10);
        });

        var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>();
        if (redisOptions != null && !string.IsNullOrWhiteSpace(redisOptions.ConnectionString))
        {
            // Same TLS, CA trust, password and timeouts as the app's own Redis connection; a bare connection
            // string would skip the CA and fail the TLS handshake against a private certificate authority.
            signalRBuilder.AddStackExchangeRedis(redis =>
            {
                redis.Configuration = RedisConnectionFactory.CreateConfigurationOptions(redisOptions);
                redis.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("malabak:signalr:");
            });
        }

        // Configure WsTicket Authentication Scheme
        services.AddAuthentication()
            .AddJwtBearer(WsTicketScheme, options =>
            {
                // Handled via ConfigureNamedOptions below
            });

        services.AddOptions<JwtBearerOptions>(WsTicketScheme)
            .Configure<IJwtKeyProvider, IOptions<JwtOptions>>((options, keyProvider, jwtOptions) =>
            {
                var opts = jwtOptions.Value;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = keyProvider.GetValidationKeys(),
                    ValidateIssuer = true,
                    ValidIssuer = opts.Issuer,
                    ValidateAudience = true,
                    ValidAudience = WsAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(10),
                    ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256]
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/hubs") &&
                            context.Request.Query.TryGetValue("access_token", out var token))
                        {
                            context.Token = token;
                        }
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var jti = context.Principal?.FindFirst("jti")?.Value
                                  ?? context.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

                        if (string.IsNullOrEmpty(jti))
                        {
                            context.Fail("missing_jti");
                            return;
                        }

                        // SignalR first POSTs /negotiate and then opens the WebSocket, both carrying the same ticket. The
                        // ticket is consumed when the connection is actually opened; negotiate only proves it is valid.
                        var isNegotiate = context.Request.Path.Value?.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase) == true;

                        // Atomically consume single-use ticket from Redis
                        var atomicStore = context.HttpContext.RequestServices.GetRequiredService<IAtomicConsumeStore>();
                        var ticketKey = $"wsjti:{jti}";
                        bool ticketValid;
                        if (isNegotiate)
                        {
                            ticketValid = await atomicStore.ExistsAsync(ticketKey, context.HttpContext.RequestAborted);
                        }
                        else
                        {
                            ticketValid = await atomicStore.ConsumeAsync(ticketKey, context.HttpContext.RequestAborted) is not null;
                        }

                        if (!ticketValid)
                        {
                            context.Fail("ticket_already_used");
                            return;
                        }

                        // Verify session is active
                        var sessionId = context.Principal?.GetSessionId();
                        if (sessionId.HasValue)
                        {
                            var sessionStore = context.HttpContext.RequestServices.GetRequiredService<ISessionStore>();
                            if (!await sessionStore.IsActiveAsync(sessionId.Value, context.HttpContext.RequestAborted))
                            {
                                context.Fail("session_revoked");
                            }
                        }
                    }
                };
            });

        return services;
    }
}
