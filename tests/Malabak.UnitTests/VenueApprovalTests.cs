using FluentAssertions;
using Malabak.Modules.Venues.Application.Approvals;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Services;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class VenueApprovalTests
{
    private static readonly DateTime _now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();
    private readonly FixedClock _clock = new();

    private Venue NewVenue() =>
        Venue.Create(Guid.NewGuid(), "Club", "club", null, _ownerId, "Addr", "Cairo", "Maadi", null, null, "+201000000001", null, null, _now);

    [Fact]
    public void NewVenue_IsDraft_NotApproved_NotSearchable()
    {
        var venue = NewVenue();

        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.Draft);
        venue.IsApproved.Should().BeFalse();
        venue.IsSearchable.Should().BeFalse();
    }

    [Fact]
    public void HappyPath_Submit_Approve_Publish_MakesSearchable()
    {
        var venue = NewVenue();

        venue.SubmitForApproval(_ownerId, "Ready", _now).IsSuccess.Should().BeTrue();
        venue.IsSearchable.Should().BeFalse();
        venue.Approve(_adminId, null, _now).IsSuccess.Should().BeTrue();
        venue.IsSearchable.Should().BeFalse("approval alone doesn't publish");
        venue.SetPublished(true, _now);

        venue.IsSearchable.Should().BeTrue();
    }

    [Fact]
    public void PublishedFlag_WithoutApproval_IsRejected()
    {
        var venue = NewVenue();
        venue.SubmitForApproval(_ownerId, null, _now);

        var act = () => venue.SetPublished(true, _now);

        act.Should().Throw<InvalidOperationException>();
        venue.IsSearchable.Should().BeFalse();
    }

    [Fact]
    public void NeedsInfoLoop_RequestInfo_ThenResubmit_ThenApprove()
    {
        var venue = NewVenue();
        venue.SubmitForApproval(_ownerId, null, _now);

        var request = venue.RequestMoreInfo(_adminId, "Please add pitch photos", _now);
        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.NeedsInfo);
        var reply = venue.SubmitForApproval(_ownerId, "Photos added", _now);

        request.Value.Message.Should().Be("Please add pitch photos");
        reply.Value.FromStatus.Should().Be(VenueApprovalStatus.NeedsInfo);
        venue.Approve(_adminId, null, _now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Reject_RequiresReason_AndAllowsResubmission()
    {
        var venue = NewVenue();
        venue.SubmitForApproval(_ownerId, null, _now);

        venue.Reject(_adminId, " ", _now).Error.Should().Be(VenueApprovalErrors.MessageRequired);
        venue.Reject(_adminId, "Address does not exist", _now).IsSuccess.Should().BeTrue();
        venue.SubmitForApproval(_ownerId, "Fixed the address", _now).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(VenueApprovalDecision.Approve)]
    [InlineData(VenueApprovalDecision.Reject)]
    [InlineData(VenueApprovalDecision.RequestInfo)]
    public void AdminDecisions_OnDraftVenue_AreConflicts(VenueApprovalDecision decision)
    {
        var venue = NewVenue();

        var result = decision switch
        {
            VenueApprovalDecision.Approve => venue.Approve(_adminId, null, _now),
            VenueApprovalDecision.Reject => venue.Reject(_adminId, "x", _now),
            _ => venue.RequestMoreInfo(_adminId, "x", _now)
        };

        result.Error.Code.Should().Be("VenueApproval.Conflict");
    }

    [Fact]
    public void Submit_WhenAlreadySubmittedOrApproved_IsConflict()
    {
        var venue = NewVenue();
        venue.SubmitForApproval(_ownerId, null, _now);

        venue.SubmitForApproval(_ownerId, null, _now).Error.Code.Should().Be("VenueApproval.Conflict");
        venue.Approve(_adminId, null, _now);
        venue.SubmitForApproval(_ownerId, null, _now).Error.Code.Should().Be("VenueApproval.Conflict");
    }

    [Fact]
    public async Task SubmitHandler_WithoutPitches_Fails_ThenSucceedsAndRecordsHistoryAsync()
    {
        await using var db = NewDb();
        var venue = NewVenue();
        db.Venues.Add(venue);
        await db.SaveChangesAsync();
        var handler = new SubmitVenueForApprovalHandler(new VenueAccessService(db), new NoopAudit(), db, _clock);

        var noPitch = await handler.HandleAsync(new SubmitVenueForApprovalCommand(venue.Id, _ownerId, null));
        db.Pitches.Add(Pitch.Create(Guid.NewGuid(), venue.Id, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        await db.SaveChangesAsync();
        var submitted = await handler.HandleAsync(new SubmitVenueForApprovalCommand(venue.Id, _ownerId, "Please review"));

        noPitch.Error.Should().Be(VenueApprovalErrors.NoPitches);
        submitted.Value.ApprovalStatus.Should().Be("Submitted");
        submitted.Value.History.Should().ContainSingle().Which.Message.Should().Be("Please review");
    }

    [Fact]
    public async Task SubmitHandler_ByNonOwner_ReturnsNotFoundAsync()
    {
        await using var db = NewDb();
        var venue = NewVenue();
        db.Venues.Add(venue);
        await db.SaveChangesAsync();

        var result = await new SubmitVenueForApprovalHandler(new VenueAccessService(db), new NoopAudit(), db, _clock)
            .HandleAsync(new SubmitVenueForApprovalCommand(venue.Id, Guid.NewGuid(), null));

        result.Error.Code.Should().Be("Venue.NotFound");
    }

    [Fact]
    public async Task AdminHandlers_ListPending_AndDecideAsync()
    {
        await using var db = NewDb();
        var venue = NewVenue();
        db.Venues.Add(venue);
        db.ApprovalEvents.Add(venue.SubmitForApproval(_ownerId, null, _now).Value);
        await db.SaveChangesAsync();

        var pending = await new GetPendingVenueApprovalsHandler(db).HandleAsync(new GetPendingVenueApprovalsQuery(1, 20));
        var decided = await new DecideVenueApprovalHandler(new NoopAudit(), db, _clock)
            .HandleAsync(new DecideVenueApprovalCommand(venue.Id, _adminId, VenueApprovalDecision.Approve, "Looks good"));
        var pendingAfter = await new GetPendingVenueApprovalsHandler(db).HandleAsync(new GetPendingVenueApprovalsQuery(1, 20));

        pending.Value.Should().ContainSingle().Which.VenueId.Should().Be(venue.Id);
        decided.Value.ApprovalStatus.Should().Be("Approved");
        decided.Value.History.Should().HaveCount(2);
        pendingAfter.Value.Should().BeEmpty();
    }

    private static VenuesDbContext NewDb() =>
        new(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class NoopAudit : IVenueAuditLogger
    {
        public Task LogAsync(string action, string entityName, Guid entityId, Guid userId, object? metadata = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => _now.AddHours(3);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    }
}
