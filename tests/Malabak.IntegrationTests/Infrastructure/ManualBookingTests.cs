using FluentAssertions;
using Malabak.Modules.Booking.Application;
using Malabak.Modules.Booking.Application.Bookings.ManualBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

/// <summary>
/// A venue books a walk-in customer (TASK-037) against a real PostgreSQL: the pitch must belong to the venue the staff member
/// was authorised for, the actor must be a real user, and the slot is protected like any other booking's.
/// </summary>
public sealed class ManualBookingTests : IClassFixture<BookingDatabaseFixture>
{
    private static readonly DateTime _start = new(DateTime.UtcNow.Year + 5, 3, 1, 18, 0, 0, DateTimeKind.Utc);

    private readonly BookingDatabaseFixture _database;
    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();

    public ManualBookingTests(BookingDatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task APitchOfTheVenue_IsBookedConfirmedWithTheStaffMemberAsActorAndTheNotesKeptAsync()
    {
        var actor = Guid.NewGuid();

        var result = await CreateAsync(Command(_start, actor: actor.ToString("D"), notes: "Paid cash"));

        result.IsSuccess.Should().BeTrue();
        await using var verify = _database.CreateContext();
        var booking = await verify.Bookings.SingleAsync(b => b.Id == result.Value.BookingId);
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.VenueId.Should().Be(_venueId);
        booking.PlayerId.Should().Be(actor);
        booking.Notes.Should().Be("Paid cash");
    }

    [Fact]
    public async Task APitchOfAnotherVenue_LooksLikeOneThatDoesNotExist_AndNothingIsSavedAsync()
    {
        var foreignPitch = Guid.NewGuid();
        var directory = new FakeVenueDirectory().WithPitch(foreignPitch, Guid.NewGuid());

        var result = await CreateAsync(Command(_start.AddDays(1), pitchId: foreignPitch), directory);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(BookingErrors.PitchNotFound);
        await using var verify = _database.CreateContext();
        (await verify.Bookings.AnyAsync(b => b.PitchId == foreignPitch)).Should().BeFalse();
    }

    [Fact]
    public async Task APitchThatDoesNotExistAtAll_IsRefusedTheSameWayAsync()
    {
        var result = await CreateAsync(Command(_start.AddDays(2), pitchId: Guid.NewGuid()));

        result.Error.Should().Be(BookingErrors.PitchNotFound);
    }

    [Fact]
    public async Task AnActorIdThatIsNotAUserId_IsRefused_NotReplacedWithARandomOneAsync()
    {
        var result = await CreateAsync(Command(_start.AddDays(3), actor: "not-a-guid"));

        result.Error.Should().Be(BookingErrors.UnauthorizedAccess);
    }

    [Fact]
    public async Task TheSameSlotTwice_ConflictsTheSecondTimeAsync()
    {
        (await CreateAsync(Command(_start.AddDays(4)))).IsSuccess.Should().BeTrue();

        var second = await CreateAsync(Command(_start.AddDays(4).AddMinutes(30)));

        second.IsFailure.Should().BeTrue();
        second.Error.Should().Be(BookingErrors.SlotOverlap);
    }

    [Fact]
    public async Task FullyPaid_IsConfirmedWithTheWholePriceAsTheDepositAsync()
    {
        var result = await CreateAsync(Command(_start.AddDays(10)));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Confirmed");
        result.Value.DepositAmount.Should().Be(300m);
        result.Value.HoldExpiresAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task DepositPaid_IsConfirmedWithTheAmountReceived_AndTheRestDueAtTheVenueAsync()
    {
        var result = await CreateAsync(Command(_start.AddDays(11), status: ManualPaymentStatus.DepositPaid, deposit: 100m));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("Confirmed");
        result.Value.DepositAmount.Should().Be(100m);
        await using var verify = _database.CreateContext();
        var booking = await verify.Bookings.SingleAsync(b => b.Id == result.Value.BookingId);
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.HoldExpiresAtUtc.Should().BeNull();
        booking.DepositAmount.Amount.Should().Be(100m);
        booking.TotalPrice.Amount.Should().Be(300m);
    }

    [Fact]
    public async Task AwaitingDeposit_HoldsTheSlotWithAnExpiryUntilTheDepositArrivesAsync()
    {
        var before = DateTime.UtcNow;

        var result = await CreateAsync(Command(_start.AddDays(12), status: ManualPaymentStatus.AwaitingDeposit, deposit: 150m));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be("PendingDeposit");
        result.Value.DepositAmount.Should().Be(150m);
        result.Value.HoldExpiresAtUtc.Should().BeAfter(before.AddMinutes(9));
        await using var verify = _database.CreateContext();
        var booking = await verify.Bookings.SingleAsync(b => b.Id == result.Value.BookingId);
        booking.Status.Should().Be(BookingStatus.PendingDeposit);
        booking.ConfirmedAtUtc.Should().BeNull();

        // The held slot is protected like any other booking's.
        var clash = await CreateAsync(Command(_start.AddDays(12)));
        clash.Error.Should().Be(BookingErrors.SlotOverlap);
    }

    [Fact]
    public async Task Unconfirmed_TakesTheSlotWithoutADeposit_UntilKickoff_AndNeedsNoCustomerDetailsAsync()
    {
        var start = _start.AddDays(30);

        var result = await CreateAsync(Command(start, status: ManualPaymentStatus.Unconfirmed) with { CustomerName = null, CustomerPhone = null });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : null);
        result.Value.Status.Should().Be("PendingDeposit");
        result.Value.DepositAmount.Should().Be(0m);
        result.Value.HoldExpiresAtUtc.Should().Be(start, "it is released at kickoff if the venue never confirms it");
        await using (var verify = _database.CreateContext())
        {
            var booking = await verify.Bookings.SingleAsync(b => b.Id == result.Value.BookingId);
            booking.CustomerName.Should().BeNull();
            booking.CustomerPhone.Should().BeNull("no placeholder is stored in place of a phone number");
        }

        // Nobody else can take it meanwhile, from the app or by hand.
        (await CreateAsync(Command(start))).Error.Should().Be(BookingErrors.SlotOverlap);

        // The venue confirms it later, once it is sure.
        await using var context = _database.CreateContext();
        var confirm = new ConfirmManualBookingHandler(context, new SystemClock(), new QuietBroadcaster(), new QuietInvalidator(),
            NullLogger<ConfirmManualBookingHandler>.Instance);
        (await confirm.HandleAsync(new ConfirmManualBookingCommand(_venueId, result.Value.BookingId, Guid.NewGuid()))).IsSuccess.Should().BeTrue();
        await using var after = _database.CreateContext();
        (await after.Bookings.SingleAsync(b => b.Id == result.Value.BookingId)).Status.Should().Be(BookingStatus.Confirmed);
    }

    [Fact]
    public async Task Unconfirmed_CannotBeLeftOpenForASlotThatAlreadyStartedAsync()
    {
        var start = DateTime.UtcNow.AddHours(-1);

        var result = await CreateAsync(Command(start, status: ManualPaymentStatus.Unconfirmed));

        result.Error.Code.Should().Be("Booking.UnconfirmedInThePast");
    }

    [Theory]
    [InlineData(ManualPaymentStatus.DepositPaid, null)]
    [InlineData(ManualPaymentStatus.AwaitingDeposit, 0)]
    [InlineData(ManualPaymentStatus.DepositPaid, 300)]
    [InlineData(ManualPaymentStatus.DepositPaid, 400)]
    [InlineData(ManualPaymentStatus.AwaitingDeposit, 301)]
    [InlineData((ManualPaymentStatus)9, 100)]
    public async Task APartialPaymentNeedsASensibleAmount_AndNothingIsSavedOtherwiseAsync(ManualPaymentStatus status, int? deposit)
    {
        var start = _start.AddDays(20);

        var result = await CreateAsync(Command(start, status: status, deposit: deposit));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("Booking.");
        await using var verify = _database.CreateContext();
        (await verify.Bookings.AnyAsync(b => b.PitchId == _pitchId && b.StartUtc == start)).Should().BeFalse();
    }

    private CreateManualBookingCommand Command(
        DateTime start, string? actor = null, Guid? pitchId = null, string? notes = null, ManualPaymentStatus status = ManualPaymentStatus.FullyPaid, decimal? deposit = null) => new(
        _venueId, pitchId ?? _pitchId, start, start.AddHours(1), 300m, "EGP", "Walk-in", "01000000000",
        actor ?? Guid.NewGuid().ToString("D"), notes, status, deposit);

    private async Task<Result<ManualBookingResponse>> CreateAsync(CreateManualBookingCommand command, IVenueDirectory? venues = null)
    {
        await using var context = _database.CreateContext();
        var handler = new CreateManualBookingHandler(
            context, new SystemClock(), new BookingReferenceGenerator(), new QuietInvalidator(), new QuietBroadcaster(),
            venues ?? new FakeVenueDirectory().WithPitch(_pitchId, _venueId), NullLogger<CreateManualBookingHandler>.Instance);
        return await handler.HandleAsync(command);
    }

    private sealed class QuietInvalidator : IAvailabilityInvalidator
    {
        public Task InvalidatePitchAsync(Guid pitchId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task InvalidateVenueAsync(Guid venueId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class QuietBroadcaster : IBookingRealtimeBroadcaster
    {
        public Task BroadcastSlotStatusChangedAsync(Guid venueId, Guid pitchId, DateTime startUtc, DateTime endUtc, string status, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task BroadcastBookingUpdatedAsync(Guid userId, Guid venueId, Guid bookingId, string reference, string status, DateTime updatedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
