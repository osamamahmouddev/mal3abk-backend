using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Malabak.IntegrationTests.Infrastructure;
using Malabak.IntegrationTests.Infrastructure.Builders;
using Malabak.Modules.Booking.Application.Admin;
using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Matches.Application.Common;
using Malabak.Modules.Matches.Domain;
using Malabak.Modules.Teams.Application.Common;
using Malabak.Modules.Venues.Application.Approvals;
using Malabak.Modules.Venues.Application.Pitches;
using Malabak.Modules.Venues.Application.Schedules;
using Malabak.Modules.Venues.Application.Venues;
using Malabak.Modules.Venues.Domain;
using Xunit;
using static Malabak.Api.Endpoints.TeamEndpoints;
using static Malabak.Api.Endpoints.VenueManagerEndpoints;

namespace Malabak.IntegrationTests;

[Collection("IntegrationTests")]
public sealed class EndToEndBackendTests : IClassFixture<BookingApiFactory>
{
    private static int _slotCounter = 100;
    private readonly BookingApiFactory _factory;

    public EndToEndBackendTests(BookingApiFactory factory)
    {
        _factory = factory;
    }

    private static DateTime NextSlotUtc()
    {
        var n = Interlocked.Increment(ref _slotCounter);
        return new DateTime(DateTime.UtcNow.Year + 5, 6, 1, 10, 0, 0, DateTimeKind.Utc).AddHours(n * 3);
    }

    [Fact]
    public async Task E2E_01_VenueFullLifecycle_CreationPitchesSchedulesApprovalAndPublishingAsync()
    {
        // 1. Create Venue Owner, Admin, and Player
        var (_, _, _, ownerClient) = await _factory.CreateVenueOwnerAsync();
        var (_, adminClient) = await _factory.CreateAdminAsync();
        var (_, playerClient) = await _factory.CreatePlayerAsync();

        var unique = Guid.NewGuid().ToString("N")[..8];
        var venueName1 = $"نادي النصر الرياضي {unique}";
        var venueName2 = $"مجمع ستاد الأبطال {unique}";

        // 2. Owner creates Venue 1
        var createVenueReq1 = new CreateVenueApiRequest(
            Name: venueName1,
            Description: "نادي رياضي متكامل بملاعب نجيل صناعي ممتازة وإضاءة ليلية",
            Address: "شارع الطيران، مدينة نصر",
            City: "Cairo",
            Area: "Nasr City",
            Latitude: 30.0561,
            Longitude: 31.3301,
            PhoneNumber: "+201012345678",
            Facilities: ["Parking", "LockerRooms", "Shower", "Cafeteria"],
            Photos: ["https://assets.malabak.app/venues/v1.jpg"]);

        var v1Res = await ownerClient.PostAsJsonAsync("/api/v1/venue-manager/venues", createVenueReq1);
        v1Res.StatusCode.Should().Be(HttpStatusCode.Created, await v1Res.Content.ReadAsStringAsync());
        var v1Dto = await v1Res.Content.ReadFromJsonAsync<VenueDto>();
        v1Dto.Should().NotBeNull();
        v1Dto!.Name.Should().Be(venueName1);
        v1Dto.City.Should().Be("Cairo");
        v1Dto.Area.Should().Be("Nasr City");
        v1Dto.PhoneNumber.Should().Be("+201012345678");
        var venueId = v1Dto.Id;

        // 3. Owner creates Venue 2
        var createVenueReq2 = new CreateVenueApiRequest(
            Name: venueName2,
            Description: "أفضل ملاعب في الجيزة والدقي",
            Address: "شارع مصدق، الدقي",
            City: "Giza",
            Area: "Dokki",
            Latitude: 30.0381,
            Longitude: 31.2112,
            PhoneNumber: "+201098765432",
            Facilities: ["Floodlights", "Cafeteria"],
            Photos: []);

        var v2Res = await ownerClient.PostAsJsonAsync("/api/v1/venue-manager/venues", createVenueReq2);
        v2Res.StatusCode.Should().Be(HttpStatusCode.Created, await v2Res.Content.ReadAsStringAsync());

        // 4. Owner adds 2 Pitches to Venue 1
        var pitchReq1 = new CreatePitchApiRequest("الملعب الرئيسي A", "ArtificialTurf", 10, 40m, 20m, []);
        var p1Res = await ownerClient.PostAsJsonAsync($"/api/v1/venue-manager/venues/{venueId}/pitches", pitchReq1);
        p1Res.StatusCode.Should().Be(HttpStatusCode.Created, await p1Res.Content.ReadAsStringAsync());
        var p1Dto = await p1Res.Content.ReadFromJsonAsync<PitchDto>();
        p1Dto.Should().NotBeNull();
        p1Dto!.Name.Should().Be("الملعب الرئيسي A");
        p1Dto.Capacity.Should().Be(10);

        var pitchReq2 = new CreatePitchApiRequest("الملعب الفرعي B", "NaturalGrass", 14, 60m, 35m, []);
        var p2Res = await ownerClient.PostAsJsonAsync($"/api/v1/venue-manager/venues/{venueId}/pitches", pitchReq2);
        p2Res.StatusCode.Should().Be(HttpStatusCode.Created, await p2Res.Content.ReadAsStringAsync());

        // 5. Owner sets weekly operating hours for Venue 1
        var days = new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };
        var scheduleItems = days.Select(d => new ScheduleItemApiRequest(
            DayOfWeek: d,
            OpenTime: new TimeOnly(9, 0),
            CloseTime: new TimeOnly(23, 0),
            SlotDurationMinutes: 60,
            IsActive: true)).ToList();

        var schedRes = await ownerClient.PutAsJsonAsync($"/api/v1/venue-manager/venues/{venueId}/schedules", new UpdateVenueSchedulesApiRequest(scheduleItems));
        schedRes.StatusCode.Should().Be(HttpStatusCode.OK, await schedRes.Content.ReadAsStringAsync());

        // 6. Owner submits Venue 1 for Approval
        var submitRes = await ownerClient.PostAsJsonAsync($"/api/v1/venue-manager/venues/{venueId}/approval/submit", new { message = "جاهز للمعاينة والمراجعة" });
        submitRes.StatusCode.Should().Be(HttpStatusCode.OK, await submitRes.Content.ReadAsStringAsync());

        // 7. Admin checks Pending Approvals
        var pendingRes = await adminClient.GetAsync("/api/v1/admin/venues/pending-approval");
        pendingRes.StatusCode.Should().Be(HttpStatusCode.OK, await pendingRes.Content.ReadAsStringAsync());

        // 8. Admin Approves Venue 1
        var approveRes = await adminClient.PostAsJsonAsync($"/api/v1/admin/venues/{venueId}/approve", new { message = "تمت المعاينة والاعتماد" });
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK, await approveRes.Content.ReadAsStringAsync());
        var approvalResult = await approveRes.Content.ReadFromJsonAsync<VenueApprovalDto>();
        approvalResult.Should().NotBeNull();
        approvalResult!.ApprovalStatus.Should().Be(VenueApprovalStatus.Approved.ToString());

        // 9. Owner Publishes Venue 1
        var pubRes = await ownerClient.PostAsJsonAsync($"/api/v1/venue-manager/venues/{venueId}/publish", new { isPublished = true });
        pubRes.StatusCode.Should().Be(HttpStatusCode.OK, await pubRes.Content.ReadAsStringAsync());

        // 10. Player checks venue profile details publicly
        var venueDetailsRes = await playerClient.GetAsync($"/api/v1/venues/{venueId}");
        venueDetailsRes.StatusCode.Should().Be(HttpStatusCode.OK, await venueDetailsRes.Content.ReadAsStringAsync());
        var venueDetails = await venueDetailsRes.Content.ReadFromJsonAsync<JsonElement>();
        venueDetails.GetProperty("venueId").GetGuid().Should().Be(venueId);
        venueDetails.GetProperty("name").GetString().Should().Be(venueName1);

        // 11. Player explores public venues list
        var exploreRes = await playerClient.GetAsync("/api/v1/venues/explore?city=Cairo");
        exploreRes.StatusCode.Should().Be(HttpStatusCode.OK, await exploreRes.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task E2E_02_BookingFlow_HoldProofSubmissionReviewAndConfirmationAsync()
    {
        var (_, venueId, pitchId, ownerClient) = await _factory.CreateVenueOwnerAsync();
        var (playerId, playerClient) = await _factory.CreatePlayerAsync();
        var (_, adminClient) = await _factory.CreateAdminAsync();

        var slotStart = NextSlotUtc();
        var slotEnd = slotStart.AddHours(1);

        // Seed a booking in ProofSubmitted state
        var booking = new BookingBuilder()
            .ForPlayer(playerId)
            .ForVenue(venueId)
            .ForPitch(pitchId)
            .WithTimeSlot(slotStart, slotEnd)
            .WithPrice(300m, 100m)
            .Build();

        booking.SubmitProof(Guid.NewGuid(), playerId.ToString("D"), DateTime.UtcNow).IsSuccess.Should().BeTrue();
        await _factory.SaveBookingAsync(booking);
        var bookingId = booking.Id;

        // Admin finds booking in deposit review queue
        var queue = await adminClient.GetFromJsonAsync<JsonElement>("/api/v1/admin/bookings/deposit-reviews?pageSize=100");
        queue.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("bookingId").GetGuid()).Should().Contain(bookingId);

        // Admin approves the deposit
        var reviewRes = await adminClient.PostAsJsonAsync($"/api/v1/admin/bookings/{bookingId}/review-deposit", new { approved = true });
        reviewRes.StatusCode.Should().Be(HttpStatusCode.NoContent, await reviewRes.Content.ReadAsStringAsync());

        // Player verifies booking is Confirmed
        var playerBooking = await playerClient.GetFromJsonAsync<JsonElement>($"/api/v1/me/bookings/{bookingId}");
        playerBooking.GetProperty("status").GetInt32().Should().Be((int)BookingStatus.Confirmed);

        // Owner verifies payout statement reflects this booking and deposit share
        var statement = await ownerClient.GetFromJsonAsync<JsonElement>($"/api/v1/venue-manager/venues/{venueId}/payouts");
        statement.GetProperty("bookings").EnumerateArray().Select(b => b.GetProperty("bookingId").GetGuid()).Should().Contain(bookingId);
    }

    [Fact]
    public async Task E2E_03_TeamsAndChallenges_FullCycle_CreationInvitationChallengeAcceptanceAndMatchResultAsync()
    {
        // 1. Create 3 Players
        var (player1Id, player1Client) = await _factory.CreatePlayerAsync();
        var (_, player2Client) = await _factory.CreatePlayerAsync();
        var (player3Id, player3Client) = await _factory.CreatePlayerAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];
        var team1Name = $"فرسان القاهرة {unique}";
        var team2Name = $"صقور الجيزة {unique}";

        // 2. Player 1 creates Team 1 (becomes Captain)
        var createTeam1 = new CreateTeamApiRequest(
            Name: team1Name,
            Description: "فريق كرة قدم خماسي قوي للمنافسات الودية والبطولات",
            City: "القاهرة",
            Governorate: "القاهرة",
            District: "مدينة نصر",
            LogoUrl: null,
            CoverUrl: null,
            IsPublic: true,
            IsRecruiting: true,
            IsOpenForChallenges: true);

        var t1Res = await player1Client.PostAsJsonAsync("/api/v1/teams", createTeam1);
        t1Res.StatusCode.Should().Be(HttpStatusCode.Created, await t1Res.Content.ReadAsStringAsync());
        var t1Dto = await t1Res.Content.ReadFromJsonAsync<TeamDetailDto>();
        t1Dto.Should().NotBeNull();
        t1Dto!.Name.Should().Be(team1Name);
        t1Dto.CaptainId.Should().Be(player1Id);
        var team1Id = t1Dto.Id;

        // 3. Player 2 creates Team 2 (becomes Captain)
        var createTeam2 = new CreateTeamApiRequest(
            Name: team2Name,
            Description: "فريق صقور الجيزة للتحديات والمسابقات",
            City: "الجيزة",
            Governorate: "الجيزة",
            District: "الدقي",
            LogoUrl: null,
            CoverUrl: null,
            IsPublic: true,
            IsRecruiting: true,
            IsOpenForChallenges: true);

        var t2Res = await player2Client.PostAsJsonAsync("/api/v1/teams", createTeam2);
        t2Res.StatusCode.Should().Be(HttpStatusCode.Created, await t2Res.Content.ReadAsStringAsync());
        var t2Dto = await t2Res.Content.ReadFromJsonAsync<TeamDetailDto>();
        t2Dto.Should().NotBeNull();
        var team2Id = t2Dto!.Id;

        // 4. Captain of Team 1 creates an invitation for Player 3
        var inviteRes = await player1Client.PostAsJsonAsync($"/api/v1/teams/{team1Id}/invitations", new CreateInvitationApiRequest(7, player3Id));
        inviteRes.StatusCode.Should().Be(HttpStatusCode.OK, await inviteRes.Content.ReadAsStringAsync());
        var inviteDto = await inviteRes.Content.ReadFromJsonAsync<TeamInvitationDto>();
        inviteDto.Should().NotBeNull();
        var inviteCode = inviteDto!.InviteCode;

        // 5. Player 3 accepts the invitation to join Team 1
        var acceptRes = await player3Client.PostAsJsonAsync("/api/v1/teams/invitations/accept", new AcceptTeamInvitationApiRequest(inviteCode));
        acceptRes.StatusCode.Should().Be(HttpStatusCode.OK, await acceptRes.Content.ReadAsStringAsync());

        // Check Team 1 profile now has 2 members
        var t1Updated = await player1Client.GetFromJsonAsync<TeamDetailDto>($"/api/v1/teams/{team1Id}");
        t1Updated.Should().NotBeNull();
        t1Updated!.Members.Should().HaveCount(2);

        // 6. Captain of Team 1 sends a match challenge to Team 2
        var challengeDate = NextSlotUtc();
        var sendChallenge = new SendChallengeRequest(
            ChallengerTeamId: team1Id,
            ChallengedTeamId: team2Id,
            ProposedDateUtc: challengeDate,
            DurationMinutes: 60,
            ProposedPitchId: null,
            ProposedVenueId: null,
            ProposedVenueName: "مجمع النادي الملكي",
            Notes: "مباراة تحدي ودية - خماسي 60 دقيقة");

        var challRes = await player1Client.PostAsJsonAsync("/api/v1/matches/challenges", sendChallenge);
        challRes.StatusCode.Should().Be(HttpStatusCode.Created, await challRes.Content.ReadAsStringAsync());
        var challDto = await challRes.Content.ReadFromJsonAsync<TeamChallengeDto>();
        challDto.Should().NotBeNull();
        challDto!.Status.Should().Be(ChallengeStatus.Pending);
        var challengeId = challDto.Id;

        // 7. Captain of Team 2 accepts the challenge
        var respondReq = new RespondToChallengeRequest(
            RespondingTeamId: team2Id,
            Action: ChallengeResponseAction.Accept,
            NewProposedDateUtc: null,
            NewDurationMinutes: null,
            NewProposedPitchId: null,
            NewProposedVenueId: null,
            NewProposedVenueName: null,
            Notes: "موافقين ومستعدين للماتش!",
            DeclineReason: null);

        var respRes = await player2Client.PostAsJsonAsync($"/api/v1/matches/challenges/{challengeId}/respond", respondReq);
        respRes.StatusCode.Should().Be(HttpStatusCode.OK, await respRes.Content.ReadAsStringAsync());
        var acceptedDto = await respRes.Content.ReadFromJsonAsync<TeamChallengeDto>();
        acceptedDto.Should().NotBeNull();
        acceptedDto!.Status.Should().Be(ChallengeStatus.Accepted);

        // 8. Teams schedule the match
        var matchDate = NextSlotUtc();
        var schedReq = new ScheduleMatchRequest(
            AwayTeamId: team2Id,
            ScheduledStartUtc: matchDate,
            DurationMinutes: 60,
            PitchId: null,
            VenueId: null,
            VenueName: "ملعب النجوم",
            BookingId: null,
            Notes: "مباراة رسمية ضمن التحدي");

        var matchRes = await player1Client.PostAsJsonAsync($"/api/v1/matches/schedule?homeTeamId={team1Id}", schedReq);
        matchRes.StatusCode.Should().Be(HttpStatusCode.Created, await matchRes.Content.ReadAsStringAsync());
        var matchDto = await matchRes.Content.ReadFromJsonAsync<MatchDto>();
        matchDto.Should().NotBeNull();
        matchDto!.Status.Should().Be(MatchStatus.Scheduled.ToString());
        var matchId = matchDto.Id;

        // 9. Home Captain submits match result: 3 - 2
        var submitResultReq = new SubmitMatchResultRequest(3, 2);
        var subRes = await player1Client.PostAsJsonAsync($"/api/v1/matches/{matchId}/results?teamId={team1Id}", submitResultReq);
        subRes.StatusCode.Should().Be(HttpStatusCode.OK, await subRes.Content.ReadAsStringAsync());
        var afterSubmit = await subRes.Content.ReadFromJsonAsync<MatchDto>();
        afterSubmit.Should().NotBeNull();
        afterSubmit!.ResultStatus.Should().Be(ResultVerificationStatus.PendingVerification.ToString());

        // 10. Away Captain confirms the result
        var verifyReq = new VerifyMatchResultRequest(MatchVerificationAction.Confirm);
        var verifyRes = await player2Client.PostAsJsonAsync($"/api/v1/matches/{matchId}/results/verify?teamId={team2Id}", verifyReq);
        verifyRes.StatusCode.Should().Be(HttpStatusCode.OK, await verifyRes.Content.ReadAsStringAsync());
        var verifiedDto = await verifyRes.Content.ReadFromJsonAsync<MatchDto>();
        verifiedDto.Should().NotBeNull();
        verifiedDto!.Status.Should().Be(MatchStatus.Completed.ToString());
        verifiedDto.Outcome.Should().Be(MatchOutcome.HomeWin.ToString());
        verifiedDto.HomeScore.Should().Be(3);
        verifiedDto.AwayScore.Should().Be(2);

        // 11. Check Leaderboard
        var leaderboardRes = await player1Client.GetAsync("/api/v1/teams/leaderboard?page=1&pageSize=20");
        leaderboardRes.StatusCode.Should().Be(HttpStatusCode.OK, await leaderboardRes.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task E2E_04_MatchDispute_AdminModerationAndResolutionAsync()
    {
        var (_, player1Client) = await _factory.CreatePlayerAsync();
        var (_, player2Client) = await _factory.CreatePlayerAsync();
        var (_, adminClient) = await _factory.CreateAdminAsync();

        var unique = Guid.NewGuid().ToString("N")[..6];

        // Create 2 Teams
        var t1Res = await player1Client.PostAsJsonAsync("/api/v1/teams", new CreateTeamApiRequest(
            $"فريق العاصفة {unique}", "فريق منافس", "القاهرة", "القاهرة", "المعادي", null, null));
        var team1 = (await t1Res.Content.ReadFromJsonAsync<TeamDetailDto>())!;

        var t2Res = await player2Client.PostAsJsonAsync("/api/v1/teams", new CreateTeamApiRequest(
            $"فريق الموج {unique}", "فريق قوي", "القاهرة", "القاهرة", "حلوان", null, null));
        var team2 = (await t2Res.Content.ReadFromJsonAsync<TeamDetailDto>())!;

        // Schedule match
        var matchDate = NextSlotUtc();
        var matchRes = await player1Client.PostAsJsonAsync($"/api/v1/matches/schedule?homeTeamId={team1.Id}", new ScheduleMatchRequest(
            AwayTeamId: team2.Id,
            ScheduledStartUtc: matchDate,
            DurationMinutes: 60,
            VenueName: "ستاد المعادي"));
        var match = (await matchRes.Content.ReadFromJsonAsync<MatchDto>())!;

        // Home team submits result 5 - 0
        var subRes = await player1Client.PostAsJsonAsync($"/api/v1/matches/{match.Id}/results?teamId={team1.Id}", new SubmitMatchResultRequest(5, 0));
        subRes.StatusCode.Should().Be(HttpStatusCode.OK, await subRes.Content.ReadAsStringAsync());

        // Away team disputes result
        var disputeReq = new VerifyMatchResultRequest(MatchVerificationAction.Dispute, "النتيجة الفعلية 2-0 وليس 5-0 بسبب أهداف ملغية");
        var disputeRes = await player2Client.PostAsJsonAsync($"/api/v1/matches/{match.Id}/results/verify?teamId={team2.Id}", disputeReq);
        disputeRes.StatusCode.Should().Be(HttpStatusCode.OK, await disputeRes.Content.ReadAsStringAsync());
        var disputedMatch = (await disputeRes.Content.ReadFromJsonAsync<MatchDto>())!;
        disputedMatch.Status.Should().Be(MatchStatus.Disputed.ToString());

        // Admin resolves dispute with official score 2 - 0
        var resolveReq = new ResolveMatchDisputeRequest(
            Action: MatchDisputeResolutionAction.SetOfficialScore,
            ResolutionNotes: "تمت مراجعة تقرير الحكم واعتماد النتيجة الرسمية 2 - 0",
            OfficialHomeScore: 2,
            OfficialAwayScore: 0);

        var resolveRes = await adminClient.PostAsJsonAsync($"/api/v1/admin/moderation/matches/{match.Id}/disputes/resolve", resolveReq);
        resolveRes.StatusCode.Should().Be(HttpStatusCode.OK, await resolveRes.Content.ReadAsStringAsync());
        var resolvedDto = (await resolveRes.Content.ReadFromJsonAsync<MatchDto>())!;
        resolvedDto.Status.Should().Be(MatchStatus.Completed.ToString());
        resolvedDto.HomeScore.Should().Be(2);
        resolvedDto.AwayScore.Should().Be(0);
        resolvedDto.Outcome.Should().Be(MatchOutcome.HomeWin.ToString());
    }

    [Fact]
    public async Task E2E_05_CancellationAndRefund_WithAdminFinancialSummaryAsync()
    {
        var (_, venueId, pitchId, _) = await _factory.CreateVenueOwnerAsync();
        var (playerId, playerClient) = await _factory.CreatePlayerAsync();
        var (_, adminClient) = await _factory.CreateAdminAsync();

        var slotStart = NextSlotUtc();
        var slotEnd = slotStart.AddHours(1);

        // Create booking with deposit
        var booking = new BookingBuilder()
            .ForPlayer(playerId)
            .ForVenue(venueId)
            .ForPitch(pitchId)
            .WithTimeSlot(slotStart, slotEnd)
            .WithPrice(400m, 100m)
            .Build();

        await _factory.SaveBookingAsync(booking);
        var bookingId = booking.Id;

        // Player cancels booking
        var cancelRes = await playerClient.PostAsJsonAsync($"/api/v1/bookings/{bookingId}/cancel", new { reason = "ظروف طارئة للمشاركين" });
        cancelRes.StatusCode.Should().Be(HttpStatusCode.OK, await cancelRes.Content.ReadAsStringAsync());

        // Verify cancelled in player booking list
        var myBookingsRes = await playerClient.GetAsync("/api/v1/me/bookings?tab=cancelled");
        myBookingsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelledList = await myBookingsRes.Content.ReadFromJsonAsync<JsonElement>();
        cancelledList.GetProperty("items").EnumerateArray().Select(b => b.GetProperty("id").GetGuid()).Should().Contain(bookingId);

        // Admin checks Financial Summary
        var summaryRes = await adminClient.GetAsync("/api/v1/admin/bookings/financial-summary");
        summaryRes.StatusCode.Should().Be(HttpStatusCode.OK, await summaryRes.Content.ReadAsStringAsync());
        var summary = await summaryRes.Content.ReadFromJsonAsync<List<VenueFinancialSummaryItem>>();
        summary.Should().NotBeNull();
        var venueRow = summary!.FirstOrDefault(v => v.VenueId == venueId);
        venueRow.Should().NotBeNull();
        venueRow!.CancelledBookings.Should().BeGreaterThanOrEqualTo(1);
        venueRow.TotalBookings.Should().BeGreaterThanOrEqualTo(1);
    }
}
