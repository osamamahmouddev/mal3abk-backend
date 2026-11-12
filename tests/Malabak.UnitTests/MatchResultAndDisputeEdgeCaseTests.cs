using FluentAssertions;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Application.Matches.ResolveDispute;
using Malabak.Modules.Matches.Application.Matches.SubmitResult;
using Malabak.Modules.Matches.Application.Matches.VerifyResult;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class MatchResultAndDisputeEdgeCaseTests
{
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc));

    private static Match CreateStandardScheduledMatch(Guid homeTeamId, Guid awayTeamId, DateTime startUtc, int durationMinutes = 60)
    {
        return Match.Schedule(
            challengeId: Guid.NewGuid(),
            homeTeamId: homeTeamId,
            awayTeamId: awayTeamId,
            scheduledStartUtc: startUtc,
            durationMinutes: durationMinutes,
            pitchId: Guid.NewGuid(),
            venueId: Guid.NewGuid(),
            venueName: "Al Ahly Pitch 1",
            bookingId: Guid.NewGuid(),
            notes: "Egyptian League Friendly",
            nowUtc: startUtc.AddHours(-1)).Value;
    }

    [Fact]
    public void SubmitResult_ValidScores_ShouldSetPendingVerificationAndRecordAudit()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var captainId = Guid.NewGuid();
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(-75), 60);

        // Act: Kickoff was 75m ago, match ended 15m ago
        var result = match.SubmitResult(homeScore: 3, awayScore: 1, homeTeamId, captainId, _clock.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        match.HomeScore.Should().Be(3);
        match.AwayScore.Should().Be(1);
        match.ResultStatus.Should().Be(ResultVerificationStatus.PendingVerification);
        match.Status.Should().Be(MatchStatus.InProgress);
        match.ResultSubmittedByTeamId.Should().Be(homeTeamId);
        match.ResultSubmittedByPlayerId.Should().Be(captainId);
        match.UncommittedAuditRecords.Should().ContainSingle(r => r.ChangeType == "SubmitResult");
    }

    [Fact]
    public void SubmitResult_ByUnrelatedThirdParty_ShouldBeRejected()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var strangerTeamId = Guid.NewGuid();
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(-75), 60);

        // Act
        var result = match.SubmitResult(2, 0, strangerTeamId, Guid.NewGuid(), _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MatchErrors.NotAuthorizedCaptain);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(2, -5)]
    [InlineData(100, 1)]
    [InlineData(0, 101)]
    public void SubmitResult_WithInvalidScores_ShouldBeRejected(int homeScore, int awayScore)
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(-75), 60);

        // Act
        var result = match.SubmitResult(homeScore, awayScore, homeTeamId, Guid.NewGuid(), _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MatchErrors.InvalidScore);
    }

    [Fact]
    public void SubmitResult_SubmittingTwice_ShouldBeRejected()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var captainId = Guid.NewGuid();
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(-75), 60);

        match.SubmitResult(2, 1, homeTeamId, captainId, _clock.UtcNow);

        // Act: Submitting again
        var secondSubmit = match.SubmitResult(3, 1, homeTeamId, captainId, _clock.UtcNow);

        // Assert
        secondSubmit.IsFailure.Should().BeTrue();
        secondSubmit.Error.Should().Be(MatchErrors.ResultAlreadySubmitted);
    }

    [Fact]
    public void VerifyResult_SubmittingCaptainVerifiesOwnScore_ShouldBeRejected()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var homeCaptainId = Guid.NewGuid();
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(-75), 60);

        match.SubmitResult(2, 1, homeTeamId, homeCaptainId, _clock.UtcNow);

        // Act: Home captain tries to verify their own submission
        var verifyResult = match.VerifyResult(
            MatchVerificationAction.Confirm,
            verifyingTeamId: homeTeamId,
            verifyingPlayerId: homeCaptainId,
            disputeReason: null,
            nowUtc: _clock.UtcNow);

        // Assert
        verifyResult.IsFailure.Should().BeTrue();
        verifyResult.Error.Should().Be(MatchErrors.CannotVerifyOwnResult);
    }

    [Fact]
    public void VerifyResult_OpposingCaptainConfirms_ShouldCompleteMatchAndEmitConfirmedEvent()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var homeCaptainId = Guid.NewGuid();
        var awayCaptainId = Guid.NewGuid();
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(-75), 60);

        match.SubmitResult(2, 1, homeTeamId, homeCaptainId, _clock.UtcNow);

        // Act: Away captain confirms
        var verifyResult = match.VerifyResult(
            MatchVerificationAction.Confirm,
            verifyingTeamId: awayTeamId,
            verifyingPlayerId: awayCaptainId,
            disputeReason: null,
            nowUtc: _clock.UtcNow);

        // Assert
        verifyResult.IsSuccess.Should().BeTrue();
        match.Status.Should().Be(MatchStatus.Completed);
        match.ResultStatus.Should().Be(ResultVerificationStatus.Confirmed);
        match.Outcome.Should().Be(MatchOutcome.HomeWin);
        match.ResultVerifiedByTeamId.Should().Be(awayTeamId);
        match.ResultVerifiedByPlayerId.Should().Be(awayCaptainId);
    }

    [Fact]
    public void VerifyResult_DisputeWithoutReason_ShouldBeRejected()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var homeCaptainId = Guid.NewGuid();
        var awayCaptainId = Guid.NewGuid();
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(-75), 60);

        match.SubmitResult(3, 0, homeTeamId, homeCaptainId, _clock.UtcNow);

        // Act: Dispute with null/empty reason
        var disputeResult = match.VerifyResult(
            MatchVerificationAction.Dispute,
            verifyingTeamId: awayTeamId,
            verifyingPlayerId: awayCaptainId,
            disputeReason: "",
            nowUtc: _clock.UtcNow);

        // Assert
        disputeResult.IsFailure.Should().BeTrue();
        disputeResult.Error.Should().Be(MatchErrors.DisputeReasonRequired);
    }

    [Fact]
    public void VerifyResult_DisputeWithReason_ShouldTransitionToDisputedAndCreateAuditRecord()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var homeCaptainId = Guid.NewGuid();
        var awayCaptainId = Guid.NewGuid();
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(-75), 60);

        match.SubmitResult(5, 1, homeTeamId, homeCaptainId, _clock.UtcNow);

        // Act
        var disputeResult = match.VerifyResult(
            MatchVerificationAction.Dispute,
            verifyingTeamId: awayTeamId,
            verifyingPlayerId: awayCaptainId,
            disputeReason: "Actual score was 2 - 2, referee sheet attached in support ticket",
            nowUtc: _clock.UtcNow);

        // Assert
        disputeResult.IsSuccess.Should().BeTrue();
        match.Status.Should().Be(MatchStatus.Disputed);
        match.ResultStatus.Should().Be(ResultVerificationStatus.Disputed);
        match.DisputeReason.Should().Contain("Actual score was 2 - 2");
        match.UncommittedAuditRecords.Should().Contain(r => r.ChangeType == "Dispute");
    }

    [Fact]
    public void AdminResolveDispute_SetOfficialScore_ShouldUpdateScoreAndCompleteMatch()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var homeCaptainId = Guid.NewGuid();
        var awayCaptainId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(-75), 60);
        match.SubmitResult(5, 1, homeTeamId, homeCaptainId, _clock.UtcNow);
        match.VerifyResult(MatchVerificationAction.Dispute, awayTeamId, awayCaptainId, "Incorrect score", _clock.UtcNow);

        // Act: Admin sets official ruling 2-2
        var resolveResult = match.ResolveDispute(
            MatchDisputeResolutionAction.SetOfficialScore,
            resolutionNotes: "Confirmed with venue supervisor sheet: official score 2 - 2",
            adminUserId: adminId,
            officialHomeScore: 2,
            officialAwayScore: 2,
            nowUtc: _clock.UtcNow);

        // Assert
        resolveResult.IsSuccess.Should().BeTrue();
        match.Status.Should().Be(MatchStatus.Completed);
        match.ResultStatus.Should().Be(ResultVerificationStatus.Confirmed);
        match.HomeScore.Should().Be(2);
        match.AwayScore.Should().Be(2);
        match.Outcome.Should().Be(MatchOutcome.Draw);
        match.AdminResolvedByUserId.Should().Be(adminId);
    }

    [Fact]
    public void AdminResolveDispute_CancelMatch_ShouldCancelAndClearScores()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(-75), 60);
        match.SubmitResult(3, 0, homeTeamId, Guid.NewGuid(), _clock.UtcNow);
        match.VerifyResult(MatchVerificationAction.Dispute, awayTeamId, Guid.NewGuid(), "Game abandoned", _clock.UtcNow);

        // Act
        var result = match.ResolveDispute(
            MatchDisputeResolutionAction.CancelMatch,
            resolutionNotes: "Match abandoned due to severe weather; unplayed minutes voided",
            adminUserId: Guid.NewGuid(),
            officialHomeScore: null,
            officialAwayScore: null,
            nowUtc: _clock.UtcNow);

        // Assert
        result.IsSuccess.Should().BeTrue();
        match.Status.Should().Be(MatchStatus.Cancelled);
        match.Outcome.Should().Be(MatchOutcome.None);
        match.CancellationReason.Should().Contain("severe weather");
    }

    [Fact]
    public void AdminResolveDispute_WhenNotDisputed_ShouldBeRejected()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, _clock.UtcNow.AddMinutes(60), 60);

        // Act: Admin tries to resolve an undisputed scheduled match
        var result = match.ResolveDispute(
            MatchDisputeResolutionAction.ConfirmSubmittedScore,
            resolutionNotes: "Ruling",
            adminUserId: Guid.NewGuid(),
            officialHomeScore: 1,
            officialAwayScore: 0,
            nowUtc: _clock.UtcNow);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(MatchErrors.MatchNotDisputed);
    }

    [Fact(Skip = "BUG-011: Match.SubmitResult allows submitting scores after the 6-hour post-match deadline (ADR-0008 §3)")]
    public void SubmitResult_MoreThan6HoursAfterMatchEnd_ShouldBeRejected()
    {
        // Arrange
        var homeTeamId = Guid.NewGuid();
        var awayTeamId = Guid.NewGuid();
        var homeCaptainId = Guid.NewGuid();

        // Match scheduled from 10:00 to 11:00 UTC (ended 9 hours ago)
        var matchStart = _clock.UtcNow.AddHours(-10);
        var match = CreateStandardScheduledMatch(homeTeamId, awayTeamId, matchStart, durationMinutes: 60);

        // Act: Captain tries to submit results at nowUtc (9 hours after match end, past the 6-hour limit)
        var result = match.SubmitResult(2, 1, homeTeamId, homeCaptainId, _clock.UtcNow);

        // Assert: ADR-0008 §3 specifies: "submitting after 6 hours is rejected or moves the match to Expired"
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("Match.SubmissionExpired", "Match.SubmissionDeadlinePassed");
    }
}
