using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.Waitlist;

public sealed class WaitlistService : IWaitlistService
{
    private const string _tableName = "waitlist_entries";
    private const string _phoneColumn = "phone";

    private readonly IdentityDbContext _dbContext;
    private readonly IPhoneNumberNormalizer _phoneNormalizer;
    private readonly IBlindIndexService _blindIndex;
    private readonly IFieldCipher _fieldCipher;
    private readonly IClock _clock;
    private readonly ILogger<WaitlistService> _logger;

    public WaitlistService(
        IdentityDbContext dbContext,
        IPhoneNumberNormalizer phoneNormalizer,
        IBlindIndexService blindIndex,
        IFieldCipher fieldCipher,
        IClock clock,
        ILogger<WaitlistService> logger)
    {
        _dbContext = dbContext;
        _phoneNormalizer = phoneNormalizer;
        _blindIndex = blindIndex;
        _fieldCipher = fieldCipher;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<WaitlistJoinResult>> JoinAsync(
        string rawPhone,
        WaitlistAudience audience,
        string? chosenPlan,
        string? ipAddress,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        var normResult = _phoneNormalizer.Normalize(rawPhone);
        if (normResult.IsFailure)
        {
            return Result.Failure<WaitlistJoinResult>(normResult.Error);
        }

        var normalizedPhone = normResult.Value;
        var blindIndex = Convert.ToHexString(
            _blindIndex.Compute(normalizedPhone, BlindIndexPurpose.WaitlistPhone)).ToLowerInvariant();

        var now = _clock.UtcNow;

        var existing = await _dbContext.WaitlistEntries
            .FirstOrDefaultAsync(w => w.PhoneBlindIndex == blindIndex, cancellationToken);

        if (existing is not null)
        {
            var encContext = new FieldEncryptionContext(_tableName, _phoneColumn, existing.Id);
            var encrypted = await _fieldCipher.EncryptAsync(normalizedPhone, encContext, cancellationToken);
            existing.UpdatePlanAndAudience(audience, chosenPlan, encrypted, now);

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Waitlist entry updated for blind index {BlindIndexPrefix}...", blindIndex[..8]);

            return Result.Success(new WaitlistJoinResult(existing.Id, false, "تم تحديث بياناتك في قائمة الانتظار بنجاح."));
        }

        var newId = Guid.CreateVersion7();
        var newContext = new FieldEncryptionContext(_tableName, _phoneColumn, newId);
        var cipherBytes = await _fieldCipher.EncryptAsync(normalizedPhone, newContext, cancellationToken);

        var entry = WaitlistEntry.Create(
            newId,
            audience,
            chosenPlan,
            cipherBytes,
            blindIndex,
            ipAddress,
            userAgent,
            now);

        await _dbContext.WaitlistEntries.AddAsync(entry, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("New waitlist entry created {EntryId} for audience {Audience}", entry.Id, audience);

        return Result.Success(new WaitlistJoinResult(entry.Id, true, "تم تسجيلك في قائمة الانتظار بنجاح."));
    }

    public async Task<Result<WaitlistPagedResult>> ListAsync(
        WaitlistAudience? audience,
        string? plan,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _dbContext.WaitlistEntries.AsNoTracking();

        if (audience.HasValue)
        {
            query = query.Where(w => w.Audience == audience.Value);
        }

        if (!string.IsNullOrWhiteSpace(plan))
        {
            query = query.Where(w => w.ChosenPlan == plan.Trim());
        }

        var total = await query.CountAsync(cancellationToken);
        var entries = await query
            .OrderByDescending(w => w.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<WaitlistDto>(entries.Count);
        foreach (var entry in entries)
        {
            string phone;
            try
            {
                var context = new FieldEncryptionContext(_tableName, _phoneColumn, entry.Id);
                phone = await _fieldCipher.DecryptAsync(entry.PhoneEncrypted, context, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt phone for waitlist entry {Id}", entry.Id);
                phone = "[ENCRYPTED]";
            }

            items.Add(new WaitlistDto(
                entry.Id,
                entry.Audience,
                entry.ChosenPlan,
                phone,
                entry.ConsentRecordedAtUtc,
                entry.CreatedAtUtc));
        }

        return Result.Success(new WaitlistPagedResult(items, total, page, pageSize));
    }
}
