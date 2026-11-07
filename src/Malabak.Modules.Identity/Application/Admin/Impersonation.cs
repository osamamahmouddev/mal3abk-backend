using System.Security.Cryptography;
using System.Text;
using Malabak.Modules.Identity.Application.AdminAuth;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.Sessions;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Application.Admin;

public sealed record ImpersonationGrant(Guid AdminId, Guid TargetUserId, string Reason);

/// <summary>Single-use impersonation codes. Only a SHA-256 hash of the code is stored, and it disappears on first read.</summary>
public interface IImpersonationCodeStore
{
    Task SaveAsync(string codeHash, ImpersonationGrant grant, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>Returns the grant and deletes it in one step (a second exchange of the same code finds nothing).</summary>
    Task<ImpersonationGrant?> TakeAsync(string codeHash, CancellationToken cancellationToken = default);
}

public sealed record ImpersonationCodeDto(string Code, int ExpiresInSeconds);

public sealed record ImpersonationTokenDto(string AccessToken, int ExpiresIn, Guid UserId, Guid ImpersonatorId, DateTime ExpiresAtUtc);

public static class ImpersonationErrors
{
    public static readonly Error ReasonRequired = Error.Validation("Impersonation.ReasonRequired", "Explain why you need to access this account.");
    public static readonly Error NotAllowed = Error.Forbidden("Impersonation.NotAllowed",
        "This account cannot be accessed (your own, a staff account, or a suspended or deleted account).");
    public static readonly Error InvalidCode = Error.Unauthorized("Impersonation.InvalidCode", "The code is invalid, expired or already used.");
    public static readonly Error Restricted = Error.Forbidden("Impersonation.Restricted",
        "This action is not available while support is signed in to your account.");
}

/// <summary>
/// Support impersonation (TASK-063, SECURITY.md): a staff member asks for a 60-second single-use code with a reason; the code
/// is exchanged once for a 15-minute access token for the customer that carries the <c>act</c> claim (who is acting) and has no
/// refresh token. It opens a visible session on the customer's account (they can revoke it), the customer is notified, every
/// step is audited, and sensitive actions are refused for such tokens by the API.
/// </summary>
public sealed class ImpersonationService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);
    public const int MaxReasonLength = 500;
    public const string SessionDeviceName = "Malabak support";

    private readonly IdentityDbContext _dbContext;
    private readonly IImpersonationCodeStore _codes;
    private readonly ITokenService _tokens;
    private readonly IDeviceContext _device;
    private readonly INotificationPublisher _notifications;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public ImpersonationService(
        IdentityDbContext dbContext,
        IImpersonationCodeStore codes,
        ITokenService tokens,
        IDeviceContext device,
        INotificationPublisher notifications,
        IAuditLog audit,
        IClock clock)
    {
        _dbContext = dbContext;
        _codes = codes;
        _tokens = tokens;
        _device = device;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
    }

    public async Task<Result<ImpersonationCodeDto>> IssueCodeAsync(Guid adminId, Guid targetUserId, string? reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
        {
            return Result.Failure<ImpersonationCodeDto>(ImpersonationErrors.ReasonRequired);
        }

        var target = await LoadTargetAsync(targetUserId, cancellationToken);
        if (target is null || targetUserId == adminId)
        {
            return Result.Failure<ImpersonationCodeDto>(ImpersonationErrors.NotAllowed);
        }

        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        await _codes.SaveAsync(Hash(code), new ImpersonationGrant(adminId, targetUserId, reason.Trim()), CodeLifetime, cancellationToken);

        await _audit.RecordAsync(new AuditEntry("admin.impersonation.code_issued", "User", targetUserId.ToString("D"), adminId, AuditActorType.Admin,
            Reason: reason.Trim()), cancellationToken);
        return Result.Success(new ImpersonationCodeDto(code, (int)CodeLifetime.TotalSeconds));
    }

    public async Task<Result<ImpersonationTokenDto>> ExchangeAsync(string? code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 100)
        {
            return Result.Failure<ImpersonationTokenDto>(ImpersonationErrors.InvalidCode);
        }

        var grant = await _codes.TakeAsync(Hash(code.Trim()), cancellationToken);
        if (grant is null)
        {
            return Result.Failure<ImpersonationTokenDto>(ImpersonationErrors.InvalidCode);
        }

        // Re-checked at exchange: the account may have been suspended or become staff within the minute.
        var target = await LoadTargetAsync(grant.TargetUserId, cancellationToken);
        if (target is null)
        {
            return Result.Failure<ImpersonationTokenDto>(ImpersonationErrors.NotAllowed);
        }

        var now = _clock.UtcNow;
        var session = UserSession.Create(Guid.CreateVersion7(), target.Id, _device.IpAddress, _device.UserAgent, now,
            deviceId: $"impersonation:{grant.AdminId:N}", deviceName: SessionDeviceName);
        _dbContext.UserSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var token = _tokens.GenerateImpersonationToken(target, session.Id, grant.AdminId, TokenLifetime);
        await _audit.RecordAsync(new AuditEntry("admin.impersonation.started", "User", target.Id.ToString("D"), grant.AdminId, AuditActorType.Admin,
            After: new { SessionId = session.Id, ExpiresAtUtc = now + TokenLifetime }, Reason: grant.Reason), cancellationToken);
        await _notifications.PublishAsync(new NotificationRequest(target.Id, NotificationTypes.ImpersonationStarted,
            new Dictionary<string, string> { ["reason"] = grant.Reason }, $"{NotificationTypes.ImpersonationStarted}:{session.Id:N}"), cancellationToken);

        return Result.Success(new ImpersonationTokenDto(token.AccessToken, token.ExpiresIn, target.Id, grant.AdminId, now + TokenLifetime));
    }

    /// <summary>Only ordinary, active customer accounts can be impersonated; staff accounts never.</summary>
    private async Task<User?> LoadTargetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted && u.SuspendedAtUtc == null, cancellationToken);
        return user is not null && !user.Roles.Any(AdminAuthHandlers.AdminRoles.Contains) ? user : null;
    }

    private static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
