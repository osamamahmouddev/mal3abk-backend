using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Malabak.Modules.Identity.Application.Common;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Matches.Application.Challenges.RespondToChallenge;
using Malabak.Modules.Matches.Application.Challenges.SendChallenge;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Application.Matches.ResolveDispute;
using Malabak.Modules.Matches.Application.Matches.SubmitResult;
using Malabak.Modules.Matches.Application.Matches.VerifyResult;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Matches.Infrastructure;
using Malabak.Modules.Matches.Infrastructure.Jobs;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Teams.Application.Teams.CreateTeam;
using Malabak.Modules.Teams.Application.Teams.Invitations.AcceptTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.Invitations.CreateTeamInvitation;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.ApproveJoinRequest;
using Malabak.Modules.Teams.Application.Teams.JoinRequests.RequestToJoinTeam;
using Malabak.Modules.Teams.Application.Teams.Members.LeaveTeam;
using Malabak.Modules.Teams.Application.Teams.Queries.GetTeamLeaderboard;
using Malabak.Modules.Teams.Domain;
using Malabak.Modules.Teams.Infrastructure;
using Malabak.Modules.Teams.Infrastructure.Services;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Events;
using Malabak.SharedKernel.Ranking;
using Malabak.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests;

public sealed class CommunityIntegrationAndAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc));

    public CommunityIntegrationAndAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private static TeamsDbContext CreateInMemoryTeamsDb()
    {
        var options = new DbContextOptionsBuilder<TeamsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new TeamsDbContext(options);
    }

    private static MatchesDbContext CreateInMemoryMatchesDb()
    {
        var options = new DbContextOptionsBuilder<MatchesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new MatchesDbContext(options);
    }

    private sealed class CapturingNotificationPublisher : INotificationPublisher
    {
        public List<NotificationRequest> Published { get; } = [];

        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            Published.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDomainEventDispatcher : IDomainEventDispatcher
    {
        public List<IDomainEvent> DispatchedEvents { get; } = [];

        public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            DispatchedEvents.Add(domainEvent);
            return Task.CompletedTask;
        }

        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        {
            DispatchedEvents.AddRange(domainEvents);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCaptaincyChecker : ITeamCaptaincyChecker
    {
        public Dictionary<Guid, Guid> TeamCaptains { get; } = [];
        public Dictionary<Guid, string> TeamNames { get; } = [];

        public Task<bool> IsCaptainOrViceCaptainAsync(Guid teamId, Guid playerId, CancellationToken cancellationToken = default)
            => Task.FromResult(TeamCaptains.TryGetValue(teamId, out var cap) && cap == playerId);

        public Task<Guid?> GetCaptainPlayerIdAsync(Guid teamId, CancellationToken cancellationToken = default)
            => Task.FromResult(TeamCaptains.TryGetValue(teamId, out var cap) ? (Guid?)cap : null);

        public Task<bool> CanBeChallengedAsync(Guid teamId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<string?> GetTeamNameAsync(Guid teamId, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(TeamNames.TryGetValue(teamId, out var name) ? name : "Test Team");
    }

    // =========================================================================
    // 1. AUTHORIZATION TESTS (Checklist item 1)
    // =========================================================================

    [Theory]
    [InlineData("/api/v1/teams")]
    [InlineData("/api/v1/matches")]
    [InlineData("/api/v1/admin/moderation/reports")]
    [InlineData("/api/v1/admin/moderation/matches/disputes")]
    public async Task ProtectedEndpoints_WhenAnonymous_Return401UnauthorizedAsync(string endpoint)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(endpoint);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminOnlyEndpoints_WhenPlayerToken_Return401Or403Async()
    {
        var tokenService = _factory.Services.GetRequiredService<ITokenService>();
        var player = User.CreatePlayer(Guid.NewGuid(), "+201009998877", "Player", DateTime.UtcNow);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenService.GenerateTokens(player).AccessToken);

        // Match disputes admin queue
        var disputesResponse = await client.GetAsync("/api/v1/admin/moderation/matches/disputes");
        disputesResponse.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

        // Fair play reports admin queue
        var reportsResponse = await client.GetAsync("/api/v1/admin/moderation/reports");
        reportsResponse.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);

        // Recalculate rankings admin trigger
        var recalcResponse = await client.PostAsync("/api/v1/admin/moderation/rankings/recalculate", null);
        recalcResponse.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CaptainOnlyActions_WhenNonCaptainExecutes_AreRejectedWithAuthorizationErrorsAsync()
    {
        using var teamsDb = CreateInMemoryTeamsDb();
        using var matchesDb = CreateInMemoryMatchesDb();

        var captainId = Guid.NewGuid();
        var impostorPlayerId = Guid.NewGuid();

        var team = Team.Create(Guid.NewGuid(), "Captaincy Team", "cap-team", "Cairo", "Cairo", captainId, "123456", _clock.UtcNow).Value;
        teamsDb.Teams.Add(team);
        await teamsDb.SaveChangesAsync();

        var checker = new FakeCaptaincyChecker();
        checker.TeamCaptains[team.Id] = captainId;

        // 1. Invite code rotation by impostor
        var inviteHandler = new CreateTeamInvitationHandler(teamsDb, _clock, new TeamInviteCodeGenerator());
        var inviteResult = await inviteHandler.HandleAsync(new CreateTeamInvitationCommand(team.Id, impostorPlayerId));
        inviteResult.IsSuccess.Should().BeFalse();
        inviteResult.Error.Code.Should().Be(TeamErrors.NotCaptainOrViceCaptain.Code);

        // 2. Challenge dispatch by impostor
        var sendChallengeHandler = new SendChallengeHandler(matchesDb, checker, new CapturingNotificationPublisher(), _clock);
        var challengeResult = await sendChallengeHandler.HandleAsync(new SendChallengeCommand(
            ChallengerTeamId: team.Id,
            ChallengedTeamId: Guid.NewGuid(),
            CallerPlayerId: impostorPlayerId,
            ProposedDateUtc: _clock.UtcNow.AddDays(2),
            DurationMinutes: 60,
            ProposedPitchId: null,
            ProposedVenueId: null,
            ProposedVenueName: null,
            Notes: null));
        challengeResult.IsSuccess.Should().BeFalse();
        challengeResult.Error.Code.Should().Be(ChallengeErrors.NotAuthorizedCaptain.Code);

        // 3. Result submission by impostor
        var submitResultHandler = new SubmitMatchResultHandler(matchesDb, checker, new CapturingNotificationPublisher(), _clock);
        var submitResult = await submitResultHandler.HandleAsync(new SubmitMatchResultCommand(
            MatchId: Guid.NewGuid(),
            SubmittingTeamId: team.Id,
            HomeScore: 2,
            AwayScore: 1,
            CallerPlayerId: impostorPlayerId));
        submitResult.IsSuccess.Should().BeFalse();
        submitResult.Error.Code.Should().Be(MatchErrors.NotAuthorizedCaptain.Code);
    }

    // =========================================================================
    // 2. RESULT INTEGRITY TESTS (Checklist item 2)
    // =========================================================================

    [Fact]
    public async Task ResultIntegrity_NonParticipant_DuplicateSubmission_AndSelfVerification_AreStrictlyBlockedAsync()
    {
        using var matchesDb = CreateInMemoryMatchesDb();
        var checker = new FakeCaptaincyChecker();
        var publisher = new CapturingNotificationPublisher();

        var teamHomeId = Guid.NewGuid();
        var teamAwayId = Guid.NewGuid();
        var thirdPartyTeamId = Guid.NewGuid();

        var captainHome = Guid.NewGuid();
        var captainAway = Guid.NewGuid();
        var captainThird = Guid.NewGuid();

        checker.TeamCaptains[teamHomeId] = captainHome;
        checker.TeamCaptains[teamAwayId] = captainAway;
        checker.TeamCaptains[thirdPartyTeamId] = captainThird;

        var match = Match.Schedule(
            null, teamHomeId, teamAwayId, _clock.UtcNow.AddHours(2), 60, null, null, null, null, null, _clock.UtcNow).Value;
        matchesDb.Matches.Add(match);
        await matchesDb.SaveChangesAsync();

        var submitHandler = new SubmitMatchResultHandler(matchesDb, checker, publisher, _clock);

        // A. Third party team attempts to submit score -> Fails with NotAuthorizedCaptain
        var thirdPartyResult = await submitHandler.HandleAsync(new SubmitMatchResultCommand(
            match.Id, thirdPartyTeamId, 2, 0, captainThird));
        thirdPartyResult.IsSuccess.Should().BeFalse();
        thirdPartyResult.Error.Code.Should().Be(MatchErrors.NotAuthorizedCaptain.Code);

        // B. Home captain submits score (3-1) -> Succeeds
        var firstSubmission = await submitHandler.HandleAsync(new SubmitMatchResultCommand(
            match.Id, teamHomeId, 3, 1, captainHome));
        firstSubmission.IsSuccess.Should().BeTrue();

        // C. Home captain attempts duplicate submission -> Fails with ResultAlreadySubmitted
        var duplicateSubmission = await submitHandler.HandleAsync(new SubmitMatchResultCommand(
            match.Id, teamHomeId, 3, 2, captainHome));
        duplicateSubmission.IsSuccess.Should().BeFalse();
        duplicateSubmission.Error.Code.Should().Be(MatchErrors.ResultAlreadySubmitted.Code);

        // D. Submitting team attempts to verify its own score -> Fails with CannotVerifyOwnResult
        var recorder = new TeamMatchResultRecorder(CreateInMemoryTeamsDb(), new Glicko2RankingStrategy(), _clock);
        var verifyHandler = new VerifyMatchResultHandler(matchesDb, checker, recorder, publisher, _clock);

        var selfVerify = await verifyHandler.HandleAsync(new VerifyMatchResultCommand(
            match.Id, teamHomeId, MatchVerificationAction.Confirm, null, captainHome));
        selfVerify.IsSuccess.Should().BeFalse();
        selfVerify.Error.Code.Should().Be(MatchErrors.CannotVerifyOwnResult.Code);
    }

    // =========================================================================
    // 3. RANKING & MODERATION TESTS (Checklist item 3)
    // =========================================================================

    [Fact]
    public async Task RankingTests_DisputedResultsExcluded_TimeoutAutoConfirms_AndRecalculationReplaysAccuratelyAsync()
    {
        using var teamsDb = CreateInMemoryTeamsDb();
        using var matchesDb = CreateInMemoryMatchesDb();
        var checker = new FakeCaptaincyChecker();

        var captainA = Guid.NewGuid();
        var captainB = Guid.NewGuid();

        var teamA = Team.Create(Guid.NewGuid(), "Ranking Alpha", "rank-alpha", "Cairo", "Cairo", captainA, "111111", _clock.UtcNow).Value;
        var teamB = Team.Create(Guid.NewGuid(), "Ranking Beta", "rank-beta", "Cairo", "Cairo", captainB, "222222", _clock.UtcNow).Value;
        teamsDb.Teams.AddRange(teamA, teamB);
        await teamsDb.SaveChangesAsync();

        checker.TeamCaptains[teamA.Id] = captainA;
        checker.TeamCaptains[teamB.Id] = captainB;

        // 1. Schedule match and submit result
        var match = Match.Schedule(
            null, teamA.Id, teamB.Id, _clock.UtcNow.AddHours(-30), 60, null, null, null, null, null, _clock.UtcNow.AddDays(-2)).Value;
        match.SubmitResult(2, 0, teamA.Id, captainA, _clock.UtcNow.AddHours(-28));

        // 2. Disputed match -> Verify result is excluded from rankings
        match.VerifyResult(MatchVerificationAction.Dispute, teamB.Id, captainB, "Offside and illegitimate goal", _clock.UtcNow.AddHours(-27));
        matchesDb.Matches.Add(match);
        await matchesDb.SaveChangesAsync();

        // Team stats must remain untouched (0 matches played, 0 points, default 1500 rating)
        var freshTeamA = await teamsDb.Teams.FindAsync(teamA.Id);
        var freshTeamB = await teamsDb.Teams.FindAsync(teamB.Id);
        freshTeamA!.MatchesPlayed.Should().Be(0);
        freshTeamA.Points.Should().Be(0);
        freshTeamA.Rating.Should().Be(1500.0);
        freshTeamB!.MatchesPlayed.Should().Be(0);

        // 3. Auto-confirmation sweep: pending result older than 24h
        var match2 = Match.Schedule(
            null, teamA.Id, teamB.Id, _clock.UtcNow.AddHours(-30), 60, null, null, null, null, null, _clock.UtcNow.AddDays(-2)).Value;
        match2.SubmitResult(1, 0, teamA.Id, captainA, _clock.UtcNow.AddHours(-25));
        matchesDb.Matches.Add(match2);
        await matchesDb.SaveChangesAsync();

        var timeoutJob = new MatchResultTimeoutJob(matchesDb, new FakeDomainEventDispatcher(), _clock, NullLogger<MatchResultTimeoutJob>.Instance);
        var autoConfirmed = await timeoutJob.ExecuteSweepAsync();
        autoConfirmed.Should().Be(1);

        var updatedMatch2 = await matchesDb.Matches.FindAsync(match2.Id);
        updatedMatch2!.ResultStatus.Should().Be(ResultVerificationStatus.AutoConfirmed);

        // 4. Nightly Recalculator: Replay all confirmed matches sequentially
        var recalculator = new TeamRankingRecalculator(teamsDb, new Glicko2RankingStrategy(), _clock, NullLogger<TeamRankingRecalculator>.Instance);
        var confirmedRecord = new ConfirmedMatchRecord(match2.Id, teamA.Id, teamB.Id, 1, 0, _clock.UtcNow.AddHours(-1));

        var recalcResult = await recalculator.RecalculateAllAsync([confirmedRecord]);
        recalcResult.IsSuccess.Should().BeTrue();

        var replayedTeamA = await teamsDb.Teams.FindAsync(teamA.Id);
        replayedTeamA!.MatchesPlayed.Should().Be(1);
        replayedTeamA.Wins.Should().Be(1);
        replayedTeamA.Points.Should().Be(3);
        replayedTeamA.Rating.Should().BeGreaterThan(1500.0);
    }

    // =========================================================================
    // 4. FLOWS D-F END-TO-END TESTS (Checklist item 4)
    // =========================================================================

    [Fact]
    public async Task FlowD_TeamCreation_InviteCode_JoinRequest_AndMembershipLifecycleAsync()
    {
        using var teamsDb = CreateInMemoryTeamsDb();
        var inviteGenerator = new TeamInviteCodeGenerator();
        var slugGenerator = new TeamSlugGenerator();

        var captainId = Guid.NewGuid();
        var memberId1 = Guid.NewGuid();
        var memberId2 = Guid.NewGuid();

        // Step 1: Create Team
        var createHandler = new CreateTeamHandler(teamsDb, _clock, slugGenerator, inviteGenerator);
        var createCmd = new CreateTeamCommand(
            CaptainId: captainId,
            Name: "Al Ahly Youth",
            Description: "Official amateur youth roster",
            City: "Cairo",
            Governorate: "Cairo",
            District: "Nasr City");

        var teamResult = await createHandler.HandleAsync(createCmd);
        teamResult.IsSuccess.Should().BeTrue();
        var teamId = teamResult.Value.Id;

        // Step 2: Second player joins via 6-character invite code
        var acceptInviteHandler = new AcceptTeamInvitationHandler(teamsDb, _clock);
        var joinResult = await acceptInviteHandler.HandleAsync(new AcceptTeamInvitationCommand(
            teamResult.Value.InviteCode!,
            memberId1));
        joinResult.IsSuccess.Should().BeTrue();
        joinResult.Value.Members.Should().HaveCount(2);

        // Step 3: Third player submits Join Request
        var joinReqHandler = new RequestToJoinTeamHandler(teamsDb, _clock);
        var reqResult = await joinReqHandler.HandleAsync(new RequestToJoinTeamCommand(
            teamId,
            memberId2,
            "Midfielder with 5 years amateur tournament experience."));
        reqResult.IsSuccess.Should().BeTrue();

        // Step 4: Captain approves Join Request
        var approveReqHandler = new ApproveJoinRequestHandler(teamsDb, _clock);
        var approveResult = await approveReqHandler.HandleAsync(new ApproveJoinRequestCommand(
            teamId,
            reqResult.Value.Id,
            captainId));
        approveResult.IsSuccess.Should().BeTrue();

        // Step 5: Verify team roster has 3 active members
        var freshTeam = await teamsDb.Teams.Include(t => t.Members).FirstAsync(t => t.Id == teamId);
        freshTeam.Members.Count(m => m.IsActive).Should().Be(3);

        // Step 6: Member leaves team
        var leaveHandler = new LeaveTeamHandler(teamsDb, _clock);
        var leaveResult = await leaveHandler.HandleAsync(new LeaveTeamCommand(teamId, memberId1));
        leaveResult.IsSuccess.Should().BeTrue();

        var afterLeaveTeam = await teamsDb.Teams.Include(t => t.Members).FirstAsync(t => t.Id == teamId);
        afterLeaveTeam.Members.Count(m => m.IsActive).Should().Be(2);
    }

    [Fact]
    public async Task FlowE_ChallengeLifecycle_CounterProposal_Acceptance_AndMatchSchedulingAsync()
    {
        using var matchesDb = CreateInMemoryMatchesDb();
        var checker = new FakeCaptaincyChecker();
        var publisher = new CapturingNotificationPublisher();

        var teamChallengerId = Guid.NewGuid();
        var teamChallengedId = Guid.NewGuid();
        var captainChallenger = Guid.NewGuid();
        var captainChallenged = Guid.NewGuid();

        checker.TeamCaptains[teamChallengerId] = captainChallenger;
        checker.TeamCaptains[teamChallengedId] = captainChallenged;
        checker.TeamNames[teamChallengerId] = "Zamalek Tigers";
        checker.TeamNames[teamChallengedId] = "Pyramids Eagles";

        // Step 1: Send initial challenge for Friday 18:00
        var sendHandler = new SendChallengeHandler(matchesDb, checker, publisher, _clock);
        var proposedDate = _clock.UtcNow.AddDays(2);
        var sendResult = await sendHandler.HandleAsync(new SendChallengeCommand(
            ChallengerTeamId: teamChallengerId,
            ChallengedTeamId: teamChallengedId,
            CallerPlayerId: captainChallenger,
            ProposedDateUtc: proposedDate,
            DurationMinutes: 60,
            ProposedPitchId: null,
            ProposedVenueId: null,
            ProposedVenueName: null,
            Notes: "Friendly weekend match"));

        sendResult.IsSuccess.Should().BeTrue();
        var challengeId = sendResult.Value.Id;
        sendResult.Value.Status.Should().Be(ChallengeStatus.Pending);

        // Step 2: Opponent counter-proposes Saturday 20:00
        var respondHandler = new RespondToChallengeHandler(matchesDb, checker, publisher, _clock);
        var counterDate = proposedDate.AddDays(1).AddHours(2);
        var counterResult = await respondHandler.HandleAsync(new RespondToChallengeCommand(
            ChallengeId: challengeId,
            RespondingTeamId: teamChallengedId,
            CallerPlayerId: captainChallenged,
            Action: ChallengeResponseAction.CounterPropose,
            NewProposedDateUtc: counterDate,
            Notes: "Saturday evening fits our squad better"));

        counterResult.IsSuccess.Should().BeTrue();
        counterResult.Value.Status.Should().Be(ChallengeStatus.CounterProposed);
        counterResult.Value.ProposedDateUtc.Should().Be(counterDate);

        // Step 3: Challenger accepts counter-proposal -> Triggers match scheduling
        var acceptResult = await respondHandler.HandleAsync(new RespondToChallengeCommand(
            ChallengeId: challengeId,
            RespondingTeamId: teamChallengerId,
            CallerPlayerId: captainChallenger,
            Action: ChallengeResponseAction.Accept));

        acceptResult.IsSuccess.Should().BeTrue();
        acceptResult.Value.Status.Should().Be(ChallengeStatus.Accepted);

        // Step 4: Verify Match is created and scheduled
        var createdMatch = await matchesDb.Matches.FirstOrDefaultAsync(m => m.ChallengeId == challengeId);
        createdMatch.Should().NotBeNull();
        createdMatch!.HomeTeamId.Should().Be(teamChallengerId);
        createdMatch.AwayTeamId.Should().Be(teamChallengedId);
        createdMatch.ScheduledStartUtc.Should().Be(counterDate);
        createdMatch.Status.Should().Be(MatchStatus.Scheduled);
    }

    [Fact]
    public async Task FlowF_MatchResult_Dispute_AdminResolution_AndLeaderboardUpdateAsync()
    {
        using var teamsDb = CreateInMemoryTeamsDb();
        using var matchesDb = CreateInMemoryMatchesDb();
        var cache = new MemoryLeaderboardCache(_clock);
        var checker = new FakeCaptaincyChecker();
        var publisher = new CapturingNotificationPublisher();

        var captainHome = Guid.NewGuid();
        var captainAway = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var teamHome = Team.Create(Guid.NewGuid(), "Cairo Stars", "cairo-stars", "Cairo", "Cairo", captainHome, "111111", _clock.UtcNow).Value;
        var teamAway = Team.Create(Guid.NewGuid(), "Giza Kings", "giza-kings", "Cairo", "Cairo", captainAway, "222222", _clock.UtcNow).Value;
        teamsDb.Teams.AddRange(teamHome, teamAway);
        await teamsDb.SaveChangesAsync();

        checker.TeamCaptains[teamHome.Id] = captainHome;
        checker.TeamCaptains[teamAway.Id] = captainAway;
        checker.TeamNames[teamHome.Id] = "Cairo Stars";
        checker.TeamNames[teamAway.Id] = "Giza Kings";

        // Step 1: Match scheduled
        var match = Match.Schedule(
            null, teamHome.Id, teamAway.Id, _clock.UtcNow.AddHours(-2), 60, null, null, null, null, null, _clock.UtcNow.AddDays(-1)).Value;
        matchesDb.Matches.Add(match);
        await matchesDb.SaveChangesAsync();

        // Step 2: Home captain submits false score (3-0)
        var submitHandler = new SubmitMatchResultHandler(matchesDb, checker, publisher, _clock);
        var submitResult = await submitHandler.HandleAsync(new SubmitMatchResultCommand(
            match.Id, teamHome.Id, 3, 0, captainHome));
        submitResult.IsSuccess.Should().BeTrue();

        // Step 3: Away captain disputes score ("Score was 2-2, illegal goal claimed")
        var recorder = new TeamMatchResultRecorder(teamsDb, new Glicko2RankingStrategy(), _clock, publisher);
        var verifyHandler = new VerifyMatchResultHandler(matchesDb, checker, recorder, publisher, _clock);
        var disputeResult = await verifyHandler.HandleAsync(new VerifyMatchResultCommand(
            match.Id, teamAway.Id, MatchVerificationAction.Dispute, "Match actually ended 2-2", captainAway));
        disputeResult.IsSuccess.Should().BeTrue();

        var disputedMatch = await matchesDb.Matches.FindAsync(match.Id);
        disputedMatch!.ResultStatus.Should().Be(ResultVerificationStatus.Disputed);

        // Step 4: Admin resolves dispute -> Sets official score to 2-2 draw
        var recalculator = new TeamRankingRecalculator(teamsDb, new Glicko2RankingStrategy(), _clock, NullLogger<TeamRankingRecalculator>.Instance);
        var rankingJob = new RankingRecalculationJob(matchesDb, recalculator, _clock, NullLogger<RankingRecalculationJob>.Instance);

        var resolveHandler = new ResolveMatchDisputeHandler(
            matchesDb, checker, rankingJob, publisher, _clock, NullLogger<ResolveMatchDisputeHandler>.Instance);

        var resolveCmd = new ResolveMatchDisputeCommand(
            MatchId: match.Id,
            AdminUserId: adminId,
            Action: MatchDisputeResolutionAction.SetOfficialScore,
            ResolutionNotes: "Video evidence confirmed 2-2 final score.",
            OfficialHomeScore: 2,
            OfficialAwayScore: 2);

        var resolveResult = await resolveHandler.HandleAsync(resolveCmd);
        resolveResult.IsSuccess.Should().BeTrue();

        var resolvedMatch = await matchesDb.Matches.FindAsync(match.Id);
        resolvedMatch!.ResultStatus.Should().Be(ResultVerificationStatus.Confirmed);
        resolvedMatch.HomeScore.Should().Be(2);
        resolvedMatch.AwayScore.Should().Be(2);

        // Step 5: Verify both teams received 1 point for the draw in Leaderboard
        var leaderboardHandler = new GetTeamLeaderboardHandler(teamsDb, cache);
        var leaderboardResult = await leaderboardHandler.HandleAsync(new GetTeamLeaderboardQuery(City: "Cairo"));

        leaderboardResult.IsSuccess.Should().BeTrue();
        leaderboardResult.Value.Items.Should().HaveCount(2);

        var cairoStarsEntry = leaderboardResult.Value.Items.First(t => t.TeamId == teamHome.Id);
        var gizaKingsEntry = leaderboardResult.Value.Items.First(t => t.TeamId == teamAway.Id);

        cairoStarsEntry.Points.Should().Be(1);
        cairoStarsEntry.Draws.Should().Be(1);
        cairoStarsEntry.MatchesPlayed.Should().Be(1);

        gizaKingsEntry.Points.Should().Be(1);
        gizaKingsEntry.Draws.Should().Be(1);
        gizaKingsEntry.MatchesPlayed.Should().Be(1);
    }
}
