using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Application.PasswordAuth;
using Malabak.Modules.Identity.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Infrastructure.Services;

public sealed class UserAccountsContract : IUserAccountsContract
{
    private readonly IdentityDbContext _dbContext;
    private readonly IPhoneNumberNormalizer _phoneNormalizer;
    private readonly IEmailSender _email;
    private readonly ISmsAlertService _sms;
    private readonly IPasswordHasher _hasher;
    private readonly IPasswordPolicy _policy;
    private readonly IClock _clock;
    private readonly ILogger<UserAccountsContract> _logger;
    private readonly string _dashboardUrl;

    public UserAccountsContract(
        IdentityDbContext dbContext,
        IPhoneNumberNormalizer phoneNormalizer,
        IEmailSender email,
        ISmsAlertService sms,
        IPasswordHasher hasher,
        IPasswordPolicy policy,
        IClock clock,
        IConfiguration configuration,
        ILogger<UserAccountsContract> logger)
    {
        _dbContext = dbContext;
        _phoneNormalizer = phoneNormalizer;
        _email = email;
        _sms = sms;
        _hasher = hasher;
        _policy = policy;
        _clock = clock;
        _logger = logger;
        _dashboardUrl = (configuration["App:VenueDashboardUrl"] ?? "https://mal3abk-venue.vercel.app/venue").TrimEnd('/');
    }

    public string? NormalizePhone(string phoneNumber)
    {
        var result = _phoneNormalizer.Normalize(phoneNumber);
        return result.IsSuccess ? result.Value : null;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetFullNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        return await _dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && !u.IsDeleted)
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, UserAccountSummary>> GetSummariesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, UserAccountSummary>();
        }

        var users = await _dbContext.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && !u.IsDeleted)
            .Select(u => new { u.Id, u.FullName, u.PhoneNumber, u.Email })
            .ToListAsync(cancellationToken);

        return users.ToDictionary(
            u => u.Id,
            u => new UserAccountSummary(u.Id, u.FullName, PhoneNumberMasker.Mask(u.PhoneNumber), u.Email));
    }

    public async Task<bool> MatchesContactAsync(Guid userId, string? email, string? normalizedPhone, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeleted)
            .Select(u => new { u.Email, u.IsEmailConfirmed, u.PhoneNumber, u.IsPhoneNumberConfirmed })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return false;
        }

        // Only verified contacts count, otherwise anyone could type the invitee's email into their profile.
        var emailMatches = email is not null && user.IsEmailConfirmed
            && string.Equals(user.Email, email.Trim(), StringComparison.OrdinalIgnoreCase);
        var phoneMatches = normalizedPhone is not null && user.IsPhoneNumberConfirmed
            && string.Equals(user.PhoneNumber, normalizedPhone, StringComparison.Ordinal);
        return emailMatches || phoneMatches;
    }

    public async Task GrantRoleAsync(Guid userId, string role, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, cancellationToken);
        if (user is null || user.Roles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        user.AddRole(role);
        user.UpdateSecurityStamp(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("AUDIT: role {Role} granted to user {UserId}", role, userId);
    }

    public async Task SendStaffInvitationAsync(string? email, string? normalizedPhone, string venueName, string role, string rawToken,
        CancellationToken cancellationToken = default)
    {
        var link = $"{_dashboardUrl}/accept-invitation?token={rawToken}";

        try
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                await _email.SendAsync(email, EmailTemplates.StaffInvitation(venueName, role, link), cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(normalizedPhone))
            {
                await _sms.SendSecurityAlertAsync(normalizedPhone, SecurityAlert.StaffInvitation(venueName, role, link), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is System.Net.Mail.SmtpException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            // The owner can resend; the link (it contains the token) is never logged.
            _logger.LogError(ex, "Failed to deliver staff invitation for venue {VenueName}", venueName);
        }
    }

    public async Task<Result<Guid>> CreateInvitedStaffAccountAsync(InvitedStaffAccount account, CancellationToken cancellationToken = default)
    {
        string? email = null;
        if (!string.IsNullOrWhiteSpace(account.Email))
        {
            if (!PasswordAuthSupport.TryNormalizeEmail(account.Email, out var normalized))
            {
                return Result.Failure<Guid>(PasswordAuthErrors.InvalidEmail);
            }

            email = normalized;
        }

        if (string.IsNullOrWhiteSpace(account.FullName) || account.FullName.Trim().Length > 200)
        {
            return Result.Failure<Guid>(PasswordAuthErrors.RegistrationFailed);
        }

        var violations = await _policy.ValidateAsync(account.Password, email, cancellationToken);
        if (violations.Count > 0)
        {
            return Result.Failure<Guid>(PasswordAuthErrors.WeakPassword(violations));
        }

        // Accounts in their deletion grace period still hold their number and email.
        if (await _dbContext.Users.IgnoreQueryFilters()
                .AnyAsync(u => u.PhoneNumber == account.NormalizedPhone || (email != null && u.Email == email), cancellationToken))
        {
            return Result.Failure<Guid>(StaffAccountErrors.AlreadyRegistered);
        }

        var user = User.CreateInvitedStaff(Guid.CreateVersion7(), email, account.NormalizedPhone, _hasher.Hash(account.Password),
            account.FullName, account.EmailVerified, account.PhoneVerified, _clock.UtcNow);
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Staff account {UserId} created from an invitation", user.Id);
        return Result.Success(user.Id);
    }
}

public static class StaffAccountErrors
{
    /// <summary>The number or email already has an account: the invitee should sign in and accept from there.</summary>
    public static readonly Error AlreadyRegistered = new("Identity.AccountExists", "There's already an account with this phone number or email. Sign in to accept the invitation.");
}
