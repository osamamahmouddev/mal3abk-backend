using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Application.Crm;

public sealed record CrmContactItem(
    Guid Id,
    string Name,
    string? VenueName,
    string? Area,
    string Phone,
    string Stage,
    string Source,
    DateTime? NextFollowUpAtUtc,
    Guid? AssignedToUserId,
    string? AssignedToName,
    DateTime StageChangedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime CreatedAtUtc,
    string? LostReason);

public sealed record CrmNoteItem(Guid Id, string Kind, string Text, Guid AuthorUserId, string? AuthorName, DateTime CreatedAtUtc);

public sealed record CrmContactDetail(CrmContactItem Contact, IReadOnlyList<CrmNoteItem> Notes);

public sealed record CrmContactPage(IReadOnlyList<CrmContactItem> Items, int Page, int PageSize, int Total);

public sealed record CrmSummary(IReadOnlyDictionary<string, int> ByStage, int FollowUpsDue, int NewThisWeek, int WaitlistNotImported);

public sealed record CrmContactInput(string? Name, string? VenueName, string? Area, string? Phone, DateTime? NextFollowUpAtUtc, Guid? AssignedToUserId,
    CrmSource Source = CrmSource.Manual);

public sealed record CrmImportResult(int Imported, int AlreadyInCrm);

/// <summary>
/// The platform team's simple CRM for venues: who they talk to, where each one stands, what was said and when to call back.
/// Phone numbers are encrypted at rest and found by blind index only; nothing personal is written to the logs.
/// </summary>
public sealed class CrmService
{
    public const int MaxPageSize = 100;
    private const string _table = "crm_contacts";
    private const string _phoneColumn = "phone";

    private readonly IdentityDbContext _dbContext;
    private readonly IPhoneNumberNormalizer _phones;
    private readonly IBlindIndexService _blindIndex;
    private readonly IFieldCipher _cipher;
    private readonly IClock _clock;
    private readonly IAuditLog _audit;
    private readonly IUserAccountsContract _users;

    public CrmService(
        IdentityDbContext dbContext, IPhoneNumberNormalizer phones, IBlindIndexService blindIndex, IFieldCipher cipher, IClock clock,
        IAuditLog audit, IUserAccountsContract users)
    {
        _dbContext = dbContext;
        _phones = phones;
        _blindIndex = blindIndex;
        _cipher = cipher;
        _clock = clock;
        _audit = audit;
        _users = users;
    }

    public static FieldEncryptionContext PhoneContext(Guid contactId) => new(_table, _phoneColumn, contactId);

    public async Task<CrmSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var dueBefore = EndOfCairoToday();
        var byStage = await _dbContext.CrmContacts.AsNoTracking()
            .GroupBy(c => c.Stage)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var due = await _dbContext.CrmContacts.AsNoTracking()
            .CountAsync(c => c.NextFollowUpAtUtc != null && c.NextFollowUpAtUtc < dueBefore, cancellationToken);
        var fresh = await _dbContext.CrmContacts.AsNoTracking().CountAsync(c => c.CreatedAtUtc >= now.AddDays(-7), cancellationToken);
        var imported = _dbContext.CrmContacts.IgnoreQueryFilters().Where(c => c.WaitlistEntryId != null).Select(c => c.WaitlistEntryId);
        var waiting = await _dbContext.WaitlistEntries.AsNoTracking()
            .CountAsync(w => w.Audience == WaitlistAudience.Venue && !imported.Contains(w.Id), cancellationToken);

        var stages = Enum.GetValues<CrmStage>().ToDictionary(s => s.ToString(), s => byStage.FirstOrDefault(x => x.Key == s)?.Count ?? 0);
        return new CrmSummary(stages, due, fresh, waiting);
    }

    public async Task<Result<CrmContactPage>> ListAsync(
        CrmStage? stage, string? search, bool dueOnly, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > MaxPageSize || search?.Length > 100)
        {
            return Result.Failure<CrmContactPage>(Error.Validation("Crm.InvalidFilter", "Invalid filter."));
        }

        var dueBefore = EndOfCairoToday();
        var query = _dbContext.CrmContacts.AsNoTracking();
        if (stage.HasValue)
        {
            query = query.Where(c => c.Stage == stage.Value);
        }

        if (dueOnly)
        {
            query = query.Where(c => c.NextFollowUpAtUtc != null && c.NextFollowUpAtUtc < dueBefore);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // A phone number is matched exactly through its blind index; anything else by name, venue or area.
            var normalized = _phones.Normalize(search.Trim());
            if (normalized.IsSuccess)
            {
                var index = BlindIndexOf(normalized.Value);
                query = query.Where(c => c.PhoneBlindIndex == index);
            }
            else
            {
                var term = $"%{search.Trim()}%";
                query = query.Where(c => EF.Functions.ILike(c.Name, term) || (c.VenueName != null && EF.Functions.ILike(c.VenueName, term))
                    || (c.Area != null && EF.Functions.ILike(c.Area, term)));
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(c => c.NextFollowUpAtUtc == null)
            .ThenBy(c => c.NextFollowUpAtUtc)
            .ThenByDescending(c => c.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return Result.Success(new CrmContactPage(await ToItemsAsync(rows, cancellationToken), page, pageSize, total));
    }

    public async Task<Result<CrmContactDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.CrmContacts.AsNoTracking().Include(c => c.Notes).FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return Result.Failure<CrmContactDetail>(CrmErrors.NotFound);
        }

        var item = (await ToItemsAsync([contact], cancellationToken))[0];
        var authors = await _users.GetFullNamesAsync(contact.Notes.Select(n => n.AuthorUserId).Distinct().ToList(), cancellationToken);
        var notes = contact.Notes
            .OrderByDescending(n => n.CreatedAtUtc)
            .Select(n => new CrmNoteItem(n.Id, n.Kind.ToString(), n.Text, n.AuthorUserId, authors.GetValueOrDefault(n.AuthorUserId), n.CreatedAtUtc))
            .ToList();
        return Result.Success(new CrmContactDetail(item, notes));
    }

    public async Task<Result<CrmContactDetail>> CreateAsync(CrmContactInput input, Guid adminId, CancellationToken cancellationToken = default)
    {
        var phone = _phones.Normalize(input.Phone ?? string.Empty);
        if (phone.IsFailure)
        {
            return Result.Failure<CrmContactDetail>(phone.Error);
        }

        var index = BlindIndexOf(phone.Value);
        var existing = await _dbContext.CrmContacts.IgnoreQueryFilters()
            .Where(c => c.PhoneBlindIndex == index)
            .Select(c => new { c.IsDeleted })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<CrmContactDetail>(existing.IsDeleted ? CrmErrors.DuplicateInTrash : CrmErrors.DuplicatePhone);
        }

        var id = Guid.CreateVersion7();
        var now = _clock.UtcNow;
        var created = CrmContact.Create(id, Details(input), await _cipher.EncryptAsync(phone.Value, PhoneContext(id), cancellationToken), index,
            input.Source, null, adminId, now);
        if (created.IsFailure)
        {
            return Result.Failure<CrmContactDetail>(created.Error);
        }

        _dbContext.CrmContacts.Add(created.Value);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two people added the same number at the same moment: the unique index keeps one.
            return Result.Failure<CrmContactDetail>(CrmErrors.DuplicatePhone);
        }

        await AuditAsync("admin.crm.contact_created", id, adminId, null, new { Stage = CrmStage.New.ToString(), input.Source }, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<CrmContactDetail>> UpdateAsync(Guid id, CrmContactInput input, Guid adminId, CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.CrmContacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return Result.Failure<CrmContactDetail>(CrmErrors.NotFound);
        }

        var now = _clock.UtcNow;
        var updated = contact.Update(Details(input), now);
        if (updated.IsFailure)
        {
            return Result.Failure<CrmContactDetail>(updated.Error);
        }

        if (!string.IsNullOrWhiteSpace(input.Phone))
        {
            var phone = _phones.Normalize(input.Phone);
            if (phone.IsFailure)
            {
                return Result.Failure<CrmContactDetail>(phone.Error);
            }

            var index = BlindIndexOf(phone.Value);
            if (index != contact.PhoneBlindIndex)
            {
                if (await _dbContext.CrmContacts.IgnoreQueryFilters().AnyAsync(c => c.PhoneBlindIndex == index && c.Id != id, cancellationToken))
                {
                    return Result.Failure<CrmContactDetail>(CrmErrors.DuplicatePhone);
                }

                contact.ReplacePhone(await _cipher.EncryptAsync(phone.Value, PhoneContext(id), cancellationToken), index, now);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<CrmContactDetail>> MoveAsync(Guid id, CrmStage stage, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.CrmContacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return Result.Failure<CrmContactDetail>(CrmErrors.NotFound);
        }

        var before = contact.Stage;
        var moved = contact.MoveTo(stage, reason, adminId, _clock.UtcNow);
        if (moved.IsFailure)
        {
            return Result.Failure<CrmContactDetail>(moved.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (before != contact.Stage)
        {
            await AuditAsync("admin.crm.stage_changed", id, adminId, new { Stage = before.ToString() }, new { Stage = contact.Stage.ToString() }, cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<Result<CrmContactDetail>> AddNoteAsync(Guid id, CrmNoteKind kind, string? text, Guid adminId, CancellationToken cancellationToken = default)
    {
        var contact = await _dbContext.CrmContacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return Result.Failure<CrmContactDetail>(CrmErrors.NotFound);
        }

        var note = contact.AddNote(kind, text, adminId, _clock.UtcNow);
        if (note.IsFailure)
        {
            return Result.Failure<CrmContactDetail>(note.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    /// <summary>Venue owners who left their number on the landing page become new contacts, once each.</summary>
    public async Task<CrmImportResult> ImportWaitlistAsync(Guid adminId, CancellationToken cancellationToken = default)
    {
        var imported = _dbContext.CrmContacts.IgnoreQueryFilters().Where(c => c.WaitlistEntryId != null).Select(c => c.WaitlistEntryId);
        var entries = await _dbContext.WaitlistEntries.AsNoTracking()
            .Where(w => w.Audience == WaitlistAudience.Venue && !imported.Contains(w.Id))
            .OrderBy(w => w.CreatedAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        var existing = (await _dbContext.CrmContacts.IgnoreQueryFilters().Select(c => c.PhoneBlindIndex).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var now = _clock.UtcNow;
        var added = 0;
        var skipped = 0;
        foreach (var entry in entries)
        {
            string phone;
            try
            {
                phone = await _cipher.DecryptAsync(entry.PhoneEncrypted, new FieldEncryptionContext("waitlist_entries", "phone", entry.Id), cancellationToken);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                skipped++;
                continue;
            }

            var index = BlindIndexOf(phone);
            if (!existing.Add(index))
            {
                skipped++;
                continue;
            }

            var id = Guid.CreateVersion7();
            var contact = CrmContact.Create(id, new CrmContactDetails("من قائمة الانتظار", null, null, null, null),
                await _cipher.EncryptAsync(phone, PhoneContext(id), cancellationToken), index, CrmSource.Waitlist, entry.Id, adminId, now).Value;
            _dbContext.CrmContacts.Add(contact);
            added++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (added > 0)
        {
            await AuditAsync("admin.crm.waitlist_imported", Guid.Empty, adminId, null, new { Imported = added }, cancellationToken);
        }

        return new CrmImportResult(added, skipped);
    }

    /// <summary>"Due" means today or earlier in Cairo, where the team works.</summary>
    private DateTime EndOfCairoToday() =>
        TimeZoneInfo.ConvertTimeToUtc(_clock.CairoToday.AddDays(1).ToDateTime(TimeOnly.MinValue), _clock.CairoTimeZone);

    private static CrmContactDetails Details(CrmContactInput input) =>
        new(input.Name, input.VenueName, input.Area, input.NextFollowUpAtUtc, input.AssignedToUserId);

    private string BlindIndexOf(string normalizedPhone) =>
        Convert.ToHexString(_blindIndex.Compute(normalizedPhone, BlindIndexPurpose.CrmPhone)).ToLowerInvariant();

    private async Task<IReadOnlyList<CrmContactItem>> ToItemsAsync(IReadOnlyList<CrmContact> rows, CancellationToken cancellationToken)
    {
        var assignees = rows.Where(r => r.AssignedToUserId.HasValue).Select(r => r.AssignedToUserId!.Value).Distinct().ToList();
        var names = assignees.Count == 0 ? new Dictionary<Guid, string>() : await _users.GetFullNamesAsync(assignees, cancellationToken);
        var items = new List<CrmContactItem>(rows.Count);
        foreach (var c in rows)
        {
            string phone;
            try
            {
                phone = await _cipher.DecryptAsync(c.PhoneEncrypted, PhoneContext(c.Id), cancellationToken);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                phone = string.Empty;
            }

            items.Add(new CrmContactItem(c.Id, c.Name, c.VenueName, c.Area, phone, c.Stage.ToString(), c.Source.ToString(), c.NextFollowUpAtUtc,
                c.AssignedToUserId, c.AssignedToUserId is { } a ? names.GetValueOrDefault(a) : null, c.StageChangedAtUtc, c.UpdatedAtUtc,
                c.CreatedAtUtc, c.LostReason));
        }

        return items;
    }

    private Task AuditAsync(string action, Guid id, Guid adminId, object? before, object? after, CancellationToken cancellationToken) =>
        _audit.RecordAsync(new AuditEntry(action, "CrmContact", id.ToString("D"), adminId, AuditActorType.Admin, Before: before, After: after),
            cancellationToken);
}
