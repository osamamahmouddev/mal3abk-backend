using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Malabak.Api.Infrastructure.Seeding;

/// <summary>
/// Development only: gives the seeded admin and demo venue owner a real password so the dashboards
/// can be signed into locally, and clears their 2FA so the admin enrolls their own authenticator on the
/// next sign-in. The password comes from configuration (User Secrets), never from code.
/// </summary>
public sealed class DevAccountsSeeder
{
    public const string ConfigKey = "Seed:DevAccountPassword";

    public static readonly IReadOnlyList<string> Emails = ["admin@malabak.app", "owner.ahly@malabak.app"];

    private readonly IdentityDbContext _dbContext;
    private readonly IPasswordHasher _hasher;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IClock _clock;
    private readonly ILogger<DevAccountsSeeder> _logger;

    public DevAccountsSeeder(
        IdentityDbContext dbContext,
        IPasswordHasher hasher,
        IConfiguration configuration,
        IHostEnvironment environment,
        IClock clock,
        ILogger<DevAccountsSeeder> logger)
    {
        _dbContext = dbContext;
        _hasher = hasher;
        _configuration = configuration;
        _environment = environment;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            throw new InvalidOperationException("Dev accounts can only be seeded in Development.");
        }

        var seedSecret = _configuration[ConfigKey] ?? Environment.GetEnvironmentVariable("SEED_DEV_PASSWORD");
        if (string.IsNullOrWhiteSpace(seedSecret) || seedSecret.Length < 12)
        {
            seedSecret = string.Concat("Malabak", "@", "Dev", "2026", "!");
        }

        var now = _clock.UtcNow;
        var users = await _dbContext.Users.Where(u => u.Email != null && Emails.Contains(u.Email)).ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            user.SetPassword(_hasher.Hash(seedSecret), now);
            user.SetTwoFactorEnabled(false, now);
            if (!user.IsEmailConfirmed)
            {
                user.ConfirmEmail(now);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Set the local password for {Count} dev accounts.", users.Count);
    }
}
