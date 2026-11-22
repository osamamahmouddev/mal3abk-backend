using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Application.Bookings.PlayerQueries;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Customers;

/// <summary>
/// One customer of the venue. Registered players are keyed by account; walk-in (manual) customers by an HMAC of their
/// phone, so the key can go in a URL without exposing the number. <c>Phone</c> is only shown for walk-in customers, whose
/// number the staff entered themselves; a player's phone stays in their account.
/// </summary>
public sealed record CustomerSummaryDto(
    string CustomerKey,
    string Name,
    string? Phone,
    bool IsRegisteredPlayer,
    int TotalBookings,
    int Completed,
    int Upcoming,
    int Cancelled,
    int NoShows,
    decimal TotalSpent,
    string Currency,
    DateTime FirstBookingUtc,
    DateTime LastBookingUtc);

public sealed record CustomerBookingDto(
    Guid BookingId, string Reference, Guid PitchId, DateTime StartUtc, DateTime EndUtc, BookingStatus Status, decimal TotalPrice, string Currency);

public sealed record CustomerNoteDto(Guid Id, string Text, Guid AuthorUserId, string AuthorName, DateTime CreatedAtUtc, bool CanDelete);

public sealed record CustomerDetailDto(CustomerSummaryDto Customer, IReadOnlyList<CustomerBookingDto> Bookings, IReadOnlyList<CustomerNoteDto> Notes);

public static class CustomerErrors
{
    public static readonly Error NotFound = new("Customer.NotFound", "Customer not found at this venue.");
    public static readonly Error InvalidNote = new("Customer.InvalidNote", "Write a note of up to 1000 characters.");
    public static readonly Error NoteNotFound = new("Customer.NoteNotFound", "Note not found.");
    public static readonly Error CannotDeleteNote = new("Customer.CannotDeleteNote", "Only the note's author or a venue manager can delete it.");
}

/// <summary>
/// Venue customer directory (TASK-503): everyone who booked at the venue, with history counts, no-shows (marked by staff)
/// and cancellations, plus staff-only notes. Every query is scoped to the venue, and a customer only exists for a venue
/// they booked at, so one venue can never look up another venue's customers.
/// </summary>
public sealed class VenueCustomerService
{
    public const string PlayerPrefix = "player:";
    public const string PhonePrefix = "phone:";
    public const int MaxHistory = 50;

    private static readonly BookingStatus[] _spent = [BookingStatus.Confirmed, BookingStatus.Completed];
    private static readonly BookingStatus[] _upcoming = [BookingStatus.PendingDeposit, BookingStatus.ProofSubmitted, BookingStatus.UnderReview, BookingStatus.Confirmed];

    private readonly IBookingDbContext _dbContext;
    private readonly IUserAccountsContract _users;
    private readonly IBlindIndexService _blindIndex;
    private readonly IFieldCipher _cipher;
    private readonly IClock _clock;

    public VenueCustomerService(IBookingDbContext dbContext, IUserAccountsContract users, IBlindIndexService blindIndex, IFieldCipher cipher, IClock clock)
    {
        _dbContext = dbContext;
        _users = users;
        _blindIndex = blindIndex;
        _cipher = cipher;
        _clock = clock;
    }

    public async Task<PagedList<CustomerSummaryDto>> ListAsync(Guid venueId, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 20 : pageSize;

        var customers = await AllCustomersAsync(venueId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            customers = customers
                .Where(c => c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || (c.Phone?.Contains(term, StringComparison.Ordinal) ?? false))
                .ToList();
        }

        var items = customers.OrderByDescending(c => c.LastBookingUtc).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedList<CustomerSummaryDto>(items, customers.Count, page, pageSize);
    }

    public async Task<Result<CustomerDetailDto>> GetAsync(
        Guid venueId, string customerKey, Guid viewerId, bool canDeleteAnyNote, CancellationToken cancellationToken = default)
    {
        var customer = (await AllCustomersAsync(venueId, cancellationToken)).Find(c => c.CustomerKey == customerKey);
        if (customer is null)
        {
            return Result.Failure<CustomerDetailDto>(CustomerErrors.NotFound);
        }

        var bookings = await CustomerBookings(venueId, customer)
            .OrderByDescending(b => b.StartUtc)
            .Take(MaxHistory)
            .Select(b => new CustomerBookingDto(b.Id, b.BookingReference, b.PitchId, b.StartUtc, b.EndUtc, b.Status, b.TotalPrice.Amount, b.TotalPrice.Currency))
            .ToListAsync(cancellationToken);

        var notes = await _dbContext.CustomerNotes.AsNoTracking()
            .Where(n => n.VenueId == venueId && n.CustomerKey == customerKey && !n.IsDeleted)
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var authors = notes.Count == 0
            ? new Dictionary<Guid, string>()
            : await _users.GetFullNamesAsync(notes.Select(n => n.AuthorUserId).Distinct().ToList(), cancellationToken);

        var noteDtos = new List<CustomerNoteDto>(notes.Count);
        foreach (var note in notes)
        {
            var text = await _cipher.DecryptAsync(note.TextCiphertext, NoteContext(note.Id), cancellationToken);
            noteDtos.Add(new CustomerNoteDto(note.Id, text, note.AuthorUserId, authors.GetValueOrDefault(note.AuthorUserId, ""), note.CreatedAtUtc,
                canDeleteAnyNote || note.AuthorUserId == viewerId));
        }

        return Result.Success(new CustomerDetailDto(customer, bookings, noteDtos));
    }

    public async Task<Result<CustomerNoteDto>> AddNoteAsync(
        Guid venueId, string customerKey, Guid authorId, string text, CancellationToken cancellationToken = default)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > VenueCustomerNote.MaxLength)
        {
            return Result.Failure<CustomerNoteDto>(CustomerErrors.InvalidNote);
        }

        if (!await CustomerExistsAsync(venueId, customerKey, cancellationToken))
        {
            return Result.Failure<CustomerNoteDto>(CustomerErrors.NotFound);
        }

        var note = VenueCustomerNote.Create(VenueCustomerNote.NewId(), venueId, customerKey, authorId, _clock.UtcNow);
        note.SetCiphertext(await _cipher.EncryptAsync(trimmed, NoteContext(note.Id), cancellationToken));
        _dbContext.CustomerNotes.Add(note);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var author = (await _users.GetFullNamesAsync([authorId], cancellationToken)).GetValueOrDefault(authorId, "");
        return Result.Success(new CustomerNoteDto(note.Id, trimmed, authorId, author, note.CreatedAtUtc, CanDelete: true));
    }

    public async Task<Result> DeleteNoteAsync(
        Guid venueId, string customerKey, Guid noteId, Guid userId, bool canDeleteAnyNote, CancellationToken cancellationToken = default)
    {
        var note = await _dbContext.CustomerNotes
            .FirstOrDefaultAsync(n => n.Id == noteId && n.VenueId == venueId && n.CustomerKey == customerKey && !n.IsDeleted, cancellationToken);
        if (note is null)
        {
            return Result.Failure(CustomerErrors.NoteNotFound);
        }

        if (!canDeleteAnyNote && note.AuthorUserId != userId)
        {
            return Result.Failure(CustomerErrors.CannotDeleteNote);
        }

        note.MarkDeleted(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Key of a walk-in customer: HMAC blind index of the normalized phone (never the number itself).</summary>
    public string PhoneKey(string phone)
    {
        var normalized = _users.NormalizePhone(phone) ?? phone.Trim();
        return PhonePrefix + Convert.ToHexString(_blindIndex.Compute(normalized, BlindIndexPurpose.VenueCustomerPhone))[..32].ToLowerInvariant();
    }

    public static FieldEncryptionContext NoteContext(Guid noteId) => new("venue_customer_notes", "text", noteId);

    private IQueryable<Domain.Booking> CustomerBookings(Guid venueId, CustomerSummaryDto customer)
    {
        var bookings = _dbContext.Bookings.AsNoTracking().Where(b => b.VenueId == venueId && !b.IsDeleted && b.Status != BookingStatus.Draft);
        if (customer.IsRegisteredPlayer)
        {
            var playerId = Guid.Parse(customer.CustomerKey[PlayerPrefix.Length..]);
            return bookings.Where(b => !b.IsManualBooking && b.PlayerId == playerId);
        }

        return bookings.Where(b => b.IsManualBooking && b.CustomerPhone == customer.Phone);
    }

    private async Task<bool> CustomerExistsAsync(Guid venueId, string customerKey, CancellationToken cancellationToken)
    {
        if (customerKey.StartsWith(PlayerPrefix))
        {
            if (Guid.TryParse(customerKey[PlayerPrefix.Length..], out var playerId))
            {
                return await _dbContext.Bookings.AsNoTracking().AnyAsync(
                    b => b.VenueId == venueId && !b.IsDeleted && b.Status != BookingStatus.Draft && !b.IsManualBooking && b.PlayerId == playerId,
                    cancellationToken);
            }
            return false;
        }

        if (customerKey.StartsWith(PhonePrefix))
        {
            var phones = await _dbContext.Bookings.AsNoTracking()
                .Where(b => b.VenueId == venueId && !b.IsDeleted && b.Status != BookingStatus.Draft && b.IsManualBooking && b.CustomerPhone != null)
                .Select(b => b.CustomerPhone!)
                .Distinct()
                .ToListAsync(cancellationToken);

            return phones.Any(p => PhoneKey(p) == customerKey);
        }

        return false;
    }

    private async Task<List<CustomerSummaryDto>> AllCustomersAsync(Guid venueId, CancellationToken cancellationToken)
    {
        var nowUtc = _clock.UtcNow;
        var bookings = _dbContext.Bookings.AsNoTracking().Where(b => b.VenueId == venueId && !b.IsDeleted && b.Status != BookingStatus.Draft);

        var players = await bookings.Where(b => !b.IsManualBooking)
            .GroupBy(b => b.PlayerId)
            .Select(g => new Group(
                g.Key, null, null, g.Count(),
                g.Count(b => b.Status == BookingStatus.Completed),
                g.Count(b => b.StartUtc > nowUtc && _upcoming.Contains(b.Status)),
                g.Count(b => b.Status == BookingStatus.Cancelled),
                g.Count(b => b.Status == BookingStatus.NoShow),
                g.Sum(b => _spent.Contains(b.Status) ? b.TotalPrice.Amount : 0m),
                g.Max(b => b.TotalPrice.Currency) ?? Money.DefaultCurrency,
                g.Min(b => b.StartUtc),
                g.Max(b => b.StartUtc)))
            .ToListAsync(cancellationToken);

        var walkIns = await bookings.Where(b => b.IsManualBooking && b.CustomerPhone != null)
            .GroupBy(b => b.CustomerPhone!)
            .Select(g => new Group(
                null, g.Key, g.Max(b => b.CustomerName), g.Count(),
                g.Count(b => b.Status == BookingStatus.Completed),
                g.Count(b => b.StartUtc > nowUtc && _upcoming.Contains(b.Status)),
                g.Count(b => b.Status == BookingStatus.Cancelled),
                g.Count(b => b.Status == BookingStatus.NoShow),
                g.Sum(b => _spent.Contains(b.Status) ? b.TotalPrice.Amount : 0m),
                g.Max(b => b.TotalPrice.Currency) ?? Money.DefaultCurrency,
                g.Min(b => b.StartUtc),
                g.Max(b => b.StartUtc)))
            .ToListAsync(cancellationToken);

        var names = players.Count == 0
            ? new Dictionary<Guid, string>()
            : await _users.GetFullNamesAsync(players.Select(p => p.PlayerId!.Value).ToList(), cancellationToken);

        return
        [
            .. players.Select(p =>
            {
                var id = p.PlayerId!.Value;
                return Summary(p, $"{PlayerPrefix}{id:N}", names.GetValueOrDefault(id, ""), null, isPlayer: true);
            }),
            .. walkIns.Select(w => Summary(w, PhoneKey(w.Phone!), w.Name ?? "", w.Phone, isPlayer: false)),
        ];
    }

    private static CustomerSummaryDto Summary(Group g, string key, string name, string? phone, bool isPlayer) => new(
        key, name, phone, isPlayer, g.Total, g.Completed, g.Upcoming, g.Cancelled, g.NoShows, g.Spent, g.Currency, g.First, g.Last);

    private sealed record Group(
        Guid? PlayerId, string? Phone, string? Name, int Total, int Completed, int Upcoming, int Cancelled, int NoShows, decimal Spent,
        string Currency, DateTime First, DateTime Last);
}
