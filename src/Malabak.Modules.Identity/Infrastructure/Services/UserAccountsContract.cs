using Malabak.Modules.Identity.Application.Common;
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
    private readonly IClock _clock;
    private readonly ILogger<UserAccountsContract> _logger;
    private readonly string _dashboardUrl;

    public UserAccountsContract(
        IdentityDbContext dbContext,
        IPhoneNumberNormalizer phoneNormalizer,
        IEmailSender email,
        ISmsAlertService sms,
        IClock clock,
        IConfiguration configuration,
        ILogger<UserAccountsContract> logger)
    {
        _dbContext = dbContext;
        _phoneNormalizer = phoneNormalizer;
        _email = email;
        _sms = sms;
        _clock = clock;
        _logger = logger;
        _dashboardUrl = (configuration["App:VenueDashboardUrl"] ?? "http://localhost:3000").TrimEnd('/');
    }

    public string? NormalizePhone(string phoneNumber)
    {
        var result = _phoneNormalizer.Normalize(phoneNumber);
        return result.IsSuccess ? result.Value : null;
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
        var body = $"You've been invited to join {venueName} on Malabak as {role}. Accept within 48 hours: {link}";

        try
        {
            if (!string.IsNullOrWhiteSpace(email))
            {
                await _email.SendAsync(email, $"Invitation to join {venueName} on Malabak", body, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(normalizedPhone))
            {
                await _sms.SendSecurityAlertAsync(normalizedPhone, body, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is System.Net.Mail.SmtpException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            // The owner can resend; the link (it contains the token) is never logged.
            _logger.LogError(ex, "Failed to deliver staff invitation for venue {VenueName}", venueName);
        }
    }
}
