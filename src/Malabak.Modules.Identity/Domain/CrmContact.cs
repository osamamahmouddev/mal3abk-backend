using Malabak.SharedKernel;

namespace Malabak.Modules.Identity.Domain;

/// <summary>Where a venue relationship stands, from first contact to a venue taking bookings.</summary>
public enum CrmStage
{
    New = 0,
    Contacted = 1,
    Interested = 2,
    Onboarding = 3,
    Active = 4,
    Lost = 5
}

public enum CrmSource
{
    Manual = 0,
    Waitlist = 1,
    Referral = 2,
    Event = 3
}

public enum CrmNoteKind
{
    Note = 0,
    Call = 1,
    StageChange = 2
}

/// <summary>
/// A venue owner (or a venue the team wants to bring on) followed by the platform team. The phone number is personal data:
/// stored encrypted (AES-256-GCM) with an HMAC blind index to find duplicates, per SECURITY.md, never in plain text.
/// </summary>
public sealed class CrmContact
{
    public const int MaxNameLength = 120;
    public const int MaxVenueNameLength = 160;
    public const int MaxAreaLength = 120;
    public const int MaxRegionLength = 60;
    public const int MaxLostReasonLength = 300;

    private readonly List<CrmNote> _notes = [];

    private CrmContact()
    {
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? VenueName { get; private set; }
    public string? Area { get; private set; }

    /// <summary>The territory the team works it from (e.g. «حلوان», «مايو»); the board is split by it. Null until someone sets it.</summary>
    public string? Region { get; private set; }
    public byte[] PhoneEncrypted { get; private set; } = [];
    public string PhoneBlindIndex { get; private set; } = string.Empty;
    public CrmStage Stage { get; private set; }
    public CrmSource Source { get; private set; }
    public string? LostReason { get; private set; }
    public DateTime? NextFollowUpAtUtc { get; private set; }
    public Guid? AssignedToUserId { get; private set; }
    public Guid? WaitlistEntryId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime StageChangedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    public IReadOnlyCollection<CrmNote> Notes => _notes.AsReadOnly();

    public static Result<CrmContact> Create(
        Guid id, CrmContactDetails details, byte[] phoneEncrypted, string phoneBlindIndex, CrmSource source, Guid? waitlistEntryId,
        Guid createdByUserId, DateTime nowUtc)
    {
        var contact = new CrmContact
        {
            Id = id,
            PhoneEncrypted = phoneEncrypted,
            PhoneBlindIndex = phoneBlindIndex,
            Stage = CrmStage.New,
            Source = Enum.IsDefined(source) ? source : CrmSource.Manual,
            WaitlistEntryId = waitlistEntryId,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            StageChangedAtUtc = nowUtc,
        };
        var applied = contact.Apply(details, nowUtc);
        return applied.IsFailure ? Result.Failure<CrmContact>(applied.Error) : Result.Success(contact);
    }

    public Result Update(CrmContactDetails details, DateTime nowUtc) => Apply(details, nowUtc);

    public void MoveToTrash(DateTime nowUtc)
    {
        IsDeleted = true;
        DeletedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public void RestoreFromTrash(DateTime nowUtc)
    {
        IsDeleted = false;
        DeletedAtUtc = null;
        UpdatedAtUtc = nowUtc;
    }

    public void ReplacePhone(byte[] phoneEncrypted, string phoneBlindIndex, DateTime nowUtc)
    {
        PhoneEncrypted = phoneEncrypted;
        PhoneBlindIndex = phoneBlindIndex;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Moves the contact to another stage and keeps the move on its timeline (a lost contact says why).</summary>
    public Result MoveTo(CrmStage stage, string? reason, Guid actorUserId, DateTime nowUtc)
    {
        if (!Enum.IsDefined(stage))
        {
            return Result.Failure(CrmErrors.InvalidStage);
        }

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (stage == CrmStage.Lost && cleanReason is null)
        {
            return Result.Failure(CrmErrors.LostReasonRequired);
        }

        if (cleanReason?.Length > MaxLostReasonLength)
        {
            return Result.Failure(CrmErrors.TextTooLong);
        }

        if (stage == Stage)
        {
            return Result.Success();
        }

        var from = Stage;
        Stage = stage;
        LostReason = stage == CrmStage.Lost ? cleanReason : null;
        StageChangedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        // Nobody chases a lost or an active venue: their follow-up date is cleared with the move.
        if (stage is CrmStage.Lost or CrmStage.Active)
        {
            NextFollowUpAtUtc = null;
        }

        var text = cleanReason is null ? $"{from} → {stage}" : $"{from} → {stage}: {cleanReason}";
        _notes.Add(CrmNote.Create(Id, CrmNoteKind.StageChange, text, actorUserId, nowUtc).Value);
        return Result.Success();
    }

    public Result<CrmNote> AddNote(CrmNoteKind kind, string? text, Guid authorUserId, DateTime nowUtc)
    {
        if (kind == CrmNoteKind.StageChange)
        {
            return Result.Failure<CrmNote>(CrmErrors.InvalidNoteKind);
        }

        var note = CrmNote.Create(Id, kind, text, authorUserId, nowUtc);
        if (note.IsSuccess)
        {
            _notes.Add(note.Value);
            UpdatedAtUtc = nowUtc;
        }

        return note;
    }

    private Result Apply(CrmContactDetails details, DateTime nowUtc)
    {
        var name = details.Name?.Trim() ?? string.Empty;
        var venueName = string.IsNullOrWhiteSpace(details.VenueName) ? null : details.VenueName.Trim();
        var area = string.IsNullOrWhiteSpace(details.Area) ? null : details.Area.Trim();
        var region = string.IsNullOrWhiteSpace(details.Region) ? null : details.Region.Trim();

        if (name.Length is 0 or > MaxNameLength)
        {
            return Result.Failure(CrmErrors.NameRequired);
        }

        if (venueName?.Length > MaxVenueNameLength || area?.Length > MaxAreaLength || region?.Length > MaxRegionLength)
        {
            return Result.Failure(CrmErrors.TextTooLong);
        }

        if (details.NextFollowUpAtUtc is { } followUp && (followUp < nowUtc.AddYears(-1) || followUp > nowUtc.AddYears(2)))
        {
            return Result.Failure(CrmErrors.InvalidFollowUp);
        }

        Name = name;
        VenueName = venueName;
        Area = area;
        Region = region;
        NextFollowUpAtUtc = details.NextFollowUpAtUtc;
        AssignedToUserId = details.AssignedToUserId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }
}

/// <summary>The fields the team edits on a contact (the phone number is handled apart because it is encrypted).</summary>
public sealed record CrmContactDetails(
    string? Name, string? VenueName, string? Area, DateTime? NextFollowUpAtUtc, Guid? AssignedToUserId, string? Region = null);

/// <summary>One line on a contact's timeline: a note, a logged call, or a stage move. Never edited once written.</summary>
public sealed class CrmNote
{
    public const int MaxTextLength = 2000;

    private CrmNote()
    {
    }

    public Guid Id { get; private set; }
    public Guid ContactId { get; private set; }
    public CrmNoteKind Kind { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public Guid AuthorUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    internal static Result<CrmNote> Create(Guid contactId, CrmNoteKind kind, string? text, Guid authorUserId, DateTime nowUtc)
    {
        var clean = text?.Trim() ?? string.Empty;
        if (clean.Length == 0)
        {
            return Result.Failure<CrmNote>(CrmErrors.NoteRequired);
        }

        if (clean.Length > MaxTextLength)
        {
            return Result.Failure<CrmNote>(CrmErrors.TextTooLong);
        }

        return Result.Success(new CrmNote
        {
            Id = Guid.CreateVersion7(),
            ContactId = contactId,
            Kind = Enum.IsDefined(kind) ? kind : CrmNoteKind.Note,
            Text = clean,
            AuthorUserId = authorUserId,
            CreatedAtUtc = nowUtc,
        });
    }
}

public static class CrmErrors
{
    public static readonly Error NotFound = Error.NotFound("Crm.NotFound", "The contact was not found.");
    public static readonly Error NameRequired = Error.Validation("Crm.NameRequired", "The contact name is required (at most 120 characters).");
    public static readonly Error TextTooLong = Error.Validation("Crm.TextTooLong", "One of the fields is too long.");
    public static readonly Error InvalidFollowUp = Error.Validation("Crm.InvalidFollowUp", "The follow-up date is out of range.");
    public static readonly Error InvalidStage = Error.Validation("Crm.InvalidStage", "Unknown stage.");
    public static readonly Error LostReasonRequired = Error.Validation("Crm.LostReasonRequired", "Say why the venue is not interested.");
    public static readonly Error NoteRequired = Error.Validation("Crm.NoteRequired", "Write the note.");
    public static readonly Error InvalidNoteKind = Error.Validation("Crm.InvalidNoteKind", "A stage change is recorded by moving the contact.");
    public static readonly Error DuplicatePhone = Error.Conflict("Crm.DuplicatePhone", "A contact with this phone number already exists.");
    public static readonly Error DuplicateInTrash = Error.Conflict("Crm.DuplicateInTrash", "A contact with this phone number is in the trash: restore it instead.");
}
