using FluentAssertions;
using Malabak.Modules.Venues.Application.Approvals;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.Modules.Venues.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Edge-case test suite for Venue Onboarding, Approval Lifecycle, Suspension, Reactivation,
/// Ownership Transfer, and Player Search Visibility (P3).
/// </summary>
public sealed class VenueOnboardingAndLifecycleEdgeCaseTests
{
    private static readonly DateTime _now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _adminId = Guid.NewGuid();

    private Venue CreateDraftVenue(Guid? ownerId = null) =>
        Venue.Create(Guid.NewGuid(), "El Ahly Club", "el-ahly-club", "Historic club", ownerId ?? _ownerId,
            "Gezira St", "Cairo", "Zamalek", 30.0444, 31.2357, "+201000000001", ["Parking", "Showers"], null, _now);

    [Fact]
    public void DraftVenue_HasExpectedInitialState_AndCannotBePublishedDirectly()
    {
        var venue = CreateDraftVenue();

        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.Draft);
        venue.IsApproved.Should().BeFalse();
        venue.IsPublished.Should().BeFalse();
        venue.IsSearchable.Should().BeFalse();

        // Attempting to publish before approval must throw InvalidOperationException
        var act = () => venue.SetPublished(true, _now);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cannot publish a venue that is not approved*");
    }

    [Fact]
    public void FullApprovalLifecycle_Draft_Submitted_NeedsInfo_Submitted_Approved_Published()
    {
        var venue = CreateDraftVenue();

        // 1. Submit for approval
        var sub1 = venue.SubmitForApproval(_ownerId, "Initial submission", _now);
        sub1.IsSuccess.Should().BeTrue();
        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.Submitted);
        venue.IsApproved.Should().BeFalse();
        venue.IsSearchable.Should().BeFalse();

        // 2. Admin requests more info
        var info = venue.RequestMoreInfo(_adminId, "Upload commercial register", _now);
        info.IsSuccess.Should().BeTrue();
        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.NeedsInfo);
        venue.IsApproved.Should().BeFalse();
        venue.IsSearchable.Should().BeFalse();

        // 3. Resubmit
        var sub2 = venue.SubmitForApproval(_ownerId, "Commercial register uploaded", _now);
        sub2.IsSuccess.Should().BeTrue();
        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.Submitted);

        // 4. Admin approves
        var approved = venue.Approve(_adminId, "Approved by compliance team", _now);
        approved.IsSuccess.Should().BeTrue();
        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.Approved);
        venue.IsApproved.Should().BeTrue();
        venue.IsPublished.Should().BeFalse("Approval alone does not publish until owner publishes");
        venue.IsSearchable.Should().BeFalse();

        // 5. Owner publishes
        venue.SetPublished(true, _now);
        venue.IsPublished.Should().BeTrue();
        venue.IsSearchable.Should().BeTrue("Approved and published venue is searchable");
    }

    [Fact]
    public void RejectionAndResubmission_RequiresReason_AndAllowsResubmission()
    {
        var venue = CreateDraftVenue();
        venue.SubmitForApproval(_ownerId, null, _now);

        // Rejection without reason fails
        var emptyReject = venue.Reject(_adminId, "   ", _now);
        emptyReject.IsFailure.Should().BeTrue();
        emptyReject.Error.Should().Be(VenueApprovalErrors.MessageRequired);

        // Valid rejection
        var reject = venue.Reject(_adminId, "Invalid venue address provided", _now);
        reject.IsSuccess.Should().BeTrue();
        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.Rejected);
        venue.IsApproved.Should().BeFalse();
        venue.IsSearchable.Should().BeFalse();

        // Resubmission from Rejected state is allowed
        var resubmit = venue.SubmitForApproval(_ownerId, "Updated address with Google Maps link", _now);
        resubmit.IsSuccess.Should().BeTrue();
        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.Submitted);
    }

    [Fact]
    public void SuspensionAndReactivation_PreservesAndRestoresPublishedState()
    {
        var venue = CreateDraftVenue();
        venue.SubmitForApproval(_ownerId, null, _now);
        venue.Approve(_adminId, null, _now);
        venue.SetPublished(true, _now);
        venue.IsSearchable.Should().BeTrue();

        // 1. Suspend with missing reason -> fails
        var emptySuspend = venue.Suspend(_adminId, "", _now);
        emptySuspend.IsFailure.Should().BeTrue();
        emptySuspend.Error.Should().Be(VenueApprovalErrors.MessageRequired);

        // 2. Valid suspension
        var suspend = venue.Suspend(_adminId, "Payment fraud investigation", _now);
        suspend.IsSuccess.Should().BeTrue();
        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.Suspended);
        venue.IsApproved.Should().BeFalse();
        venue.IsPublished.Should().BeFalse("Suspension must unpublish the venue immediately");
        venue.PublishedBeforeSuspension.Should().BeTrue("Published flag prior to suspension is remembered");
        venue.IsSearchable.Should().BeFalse("Suspended venue is never searchable by players");

        // 3. Reactivate with missing reason -> fails
        var emptyReactivate = venue.Reactivate(_adminId, "  ", _now);
        emptyReactivate.IsFailure.Should().BeTrue();
        emptyReactivate.Error.Should().Be(VenueApprovalErrors.MessageRequired);

        // 4. Valid reactivation restores published state
        var reactivate = venue.Reactivate(_adminId, "Investigation cleared, account in good standing", _now);
        reactivate.IsSuccess.Should().BeTrue();
        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.Approved);
        venue.IsApproved.Should().BeTrue();
        venue.IsPublished.Should().BeTrue("Reactivation restores previously published venue to published state");
        venue.PublishedBeforeSuspension.Should().BeFalse("Memory flag is cleared after restoration");
        venue.IsSearchable.Should().BeTrue("Reactivated published venue is searchable again");
    }

    [Fact]
    public void Suspension_WhenVenueWasUnpublished_ReactivationKeepsItUnpublished()
    {
        var venue = CreateDraftVenue();
        venue.SubmitForApproval(_ownerId, null, _now);
        venue.Approve(_adminId, null, _now);
        // Kept unpublished
        venue.IsPublished.Should().BeFalse();

        var suspend = venue.Suspend(_adminId, "Unpaid platform subscription", _now);
        suspend.IsSuccess.Should().BeTrue();
        venue.PublishedBeforeSuspension.Should().BeFalse();

        var reactivate = venue.Reactivate(_adminId, "Subscription paid", _now);
        reactivate.IsSuccess.Should().BeTrue();
        venue.IsApproved.Should().BeTrue();
        venue.IsPublished.Should().BeFalse("Reactivation does not publish an unpublished venue");
        venue.IsSearchable.Should().BeFalse();
    }

    [Theory]
    [InlineData(VenueApprovalStatus.Draft)]
    [InlineData(VenueApprovalStatus.Submitted)]
    [InlineData(VenueApprovalStatus.NeedsInfo)]
    [InlineData(VenueApprovalStatus.Rejected)]
    [InlineData(VenueApprovalStatus.Suspended)]
    public void Suspend_FromNonApprovedStates_IsRejected(VenueApprovalStatus state)
    {
        var venue = SetupVenueInState(state);

        var result = venue.Suspend(_adminId, "Violation", _now);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VenueApproval.Conflict");
    }

    [Theory]
    [InlineData(VenueApprovalStatus.Draft)]
    [InlineData(VenueApprovalStatus.Submitted)]
    [InlineData(VenueApprovalStatus.NeedsInfo)]
    [InlineData(VenueApprovalStatus.Approved)]
    [InlineData(VenueApprovalStatus.Rejected)]
    public void Reactivate_FromNonSuspendedStates_IsRejected(VenueApprovalStatus state)
    {
        var venue = SetupVenueInState(state);

        var result = venue.Reactivate(_adminId, "Reinstated", _now);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VenueApproval.Conflict");
    }

    [Theory]
    [InlineData(VenueApprovalStatus.Draft)]
    [InlineData(VenueApprovalStatus.NeedsInfo)]
    [InlineData(VenueApprovalStatus.Approved)]
    [InlineData(VenueApprovalStatus.Rejected)]
    [InlineData(VenueApprovalStatus.Suspended)]
    public void AdminApprovalDecisions_FromNonSubmittedStates_AreRejected(VenueApprovalStatus state)
    {
        var venue = SetupVenueInState(state);

        venue.Approve(_adminId, null, _now).IsFailure.Should().BeTrue();
        venue.Reject(_adminId, "Reason", _now).IsFailure.Should().BeTrue();
        venue.RequestMoreInfo(_adminId, "Need info", _now).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData(VenueApprovalStatus.Submitted)]
    [InlineData(VenueApprovalStatus.Approved)]
    [InlineData(VenueApprovalStatus.Suspended)]
    public void SubmitForApproval_FromInvalidStates_IsRejected(VenueApprovalStatus state)
    {
        var venue = SetupVenueInState(state);

        var result = venue.SubmitForApproval(_ownerId, "Please review", _now);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("VenueApproval.Conflict");
    }

    [Fact]
    public void MessageLengthBounds_EnforcedAcrossActions()
    {
        var venue = CreateDraftVenue();
        var tooLongMessage = new string('A', VenueApprovalEvent.MaxMessageLength + 1);

        // Submit with too long message
        var subTooLong = venue.SubmitForApproval(_ownerId, tooLongMessage, _now);
        subTooLong.IsFailure.Should().BeTrue();
        subTooLong.Error.Should().Be(VenueApprovalErrors.MessageTooLong);

        venue.SubmitForApproval(_ownerId, "Valid submit", _now);

        // Reject with too long message
        var rejectTooLong = venue.Reject(_adminId, tooLongMessage, _now);
        rejectTooLong.IsFailure.Should().BeTrue();
        rejectTooLong.Error.Should().Be(VenueApprovalErrors.MessageTooLong);

        // RequestInfo with too long message
        var infoTooLong = venue.RequestMoreInfo(_adminId, tooLongMessage, _now);
        infoTooLong.IsFailure.Should().BeTrue();
        infoTooLong.Error.Should().Be(VenueApprovalErrors.MessageTooLong);

        // Admin note bounds
        var tooLongAdminNote = new string('X', Venue.MaxAdminNoteLength + 1);
        var actNote = () => venue.SetAdminNote(tooLongAdminNote, _now);
        actNote.Should().Throw<ArgumentException>().WithMessage("*Admin note too long*");
    }

    [Fact]
    public void TransferOwnership_ValidatesNewOwner_RequiresReason_AndRecordsAudit()
    {
        var venue = CreateDraftVenue();
        var newOwnerId = Guid.NewGuid();

        // Cannot transfer to empty Guid
        venue.TransferOwnership(Guid.Empty, _adminId, "Sold club", _now).IsFailure.Should().BeTrue();

        // Cannot transfer to same owner
        venue.TransferOwnership(_ownerId, _adminId, "Sold club", _now).IsFailure.Should().BeTrue();

        // Reason required
        venue.TransferOwnership(newOwnerId, _adminId, "   ", _now).IsFailure.Should().BeTrue();

        // Valid transfer
        var result = venue.TransferOwnership(newOwnerId, _adminId, "Venue sold to new sports management company", _now);
        result.IsSuccess.Should().BeTrue();
        venue.OwnerId.Should().Be(newOwnerId);
        result.Value.Action.Should().Be(VenueApprovalAction.OwnershipTransferred);
        result.Value.ActorUserId.Should().Be(_adminId);
        result.Value.Message.Should().Be("Venue sold to new sports management company");
    }

    private Venue SetupVenueInState(VenueApprovalStatus targetState)
    {
        var venue = CreateDraftVenue();
        if (targetState == VenueApprovalStatus.Draft) return venue;

        venue.SubmitForApproval(_ownerId, null, _now);
        if (targetState == VenueApprovalStatus.Submitted) return venue;

        if (targetState == VenueApprovalStatus.NeedsInfo)
        {
            venue.RequestMoreInfo(_adminId, "Needs info", _now);
            return venue;
        }

        if (targetState == VenueApprovalStatus.Rejected)
        {
            venue.Reject(_adminId, "Rejected reason", _now);
            return venue;
        }

        if (targetState is VenueApprovalStatus.Approved or VenueApprovalStatus.Suspended)
        {
            venue.Approve(_adminId, "Approved note", _now);
            if (targetState == VenueApprovalStatus.Suspended)
            {
                venue.Suspend(_adminId, "Suspended reason", _now);
            }
            return venue;
        }

        throw new ArgumentOutOfRangeException(nameof(targetState), targetState, "Unsupported test state");
    }
}
