using FluentAssertions;
using Malabak.Modules.Support.Domain;
using Malabak.Modules.Support.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class SupportTicketTests
{
    private static readonly DateTime _now = new(2026, 11, 7, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _player = Guid.NewGuid();
    private readonly Guid _agent = Guid.NewGuid();
    private readonly Recorder _recorder = new();
    private readonly Clock _clock = new();

    [Fact]
    public async Task OpeningATicket_ValidatesInput_AndMoneyTicketsStartHighAsync()
    {
        await using var db = NewDb();
        var service = Service(db);

        (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Nonsense", "s", "b", null, null))).Error.Should().Be(SupportErrors.Invalid);
        (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("booking", "Help", "  ", null, null))).Error.Should().Be(SupportErrors.InvalidMessage);
        (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("VenueComplaint", "Dirty", "Bad", null, null))).Error.Should().Be(SupportErrors.VenueRequired);

        var refund = (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("refund", "Refund missing", "I cancelled 3 days ago", Guid.NewGuid(), null))).Value;

        refund.Ticket.Priority.Should().Be(nameof(TicketPriority.High));
        refund.Ticket.Status.Should().Be(nameof(TicketStatus.Open));
        refund.Ticket.Number.Should().MatchRegex("^SUP-[A-Z2-9]{6}$");
        refund.Messages.Should().ContainSingle(m => m.AuthorKind == nameof(TicketAuthorKind.Requester));
    }

    [Fact]
    public async Task Requesters_OnlySeeTheirOwnTickets_AndNeverInternalNotesAsync()
    {
        await using var db = NewDb();
        var service = Service(db);
        var ticket = (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Account", "Can't sign in", "OTP never arrives", null, null))).Value;

        await service.ReplyAsAgentAsync(ticket.Ticket.Id, _agent, "Carrier issue suspected, check SMS logs", isInternal: true);
        await service.ReplyAsAgentAsync(ticket.Ticket.Id, _agent, "Please try again now", isInternal: false);

        var mine = (await service.GetMineAsync(ticket.Ticket.Id, _player)).Value;
        mine.Messages.Should().HaveCount(2).And.OnlyContain(m => !m.IsInternal);
        mine.Ticket.Status.Should().Be(nameof(TicketStatus.WaitingOnRequester));
        (await service.GetAsync(ticket.Ticket.Id)).Value.Messages.Should().HaveCount(3);

        (await service.GetMineAsync(ticket.Ticket.Id, Guid.NewGuid())).Error.Should().Be(SupportErrors.NotFound);
        (await service.ReplyAsRequesterAsync(ticket.Ticket.Id, Guid.NewGuid(), "hi")).Error.Should().Be(SupportErrors.NotFound);
        _recorder.Notifications.Should().ContainSingle(n => n.Type == NotificationTypes.SupportTicketReplied && n.UserId == _player,
            "internal notes never notify the requester");
    }

    [Fact]
    public async Task Lifecycle_AssignResolveReopenClose_IsAuditedAsync()
    {
        await using var db = NewDb();
        var service = Service(db);
        var id = (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Deposit", "Proof rejected", "I paid", Guid.NewGuid(), null))).Value.Ticket.Id;

        (await service.AssignAsync(id, _agent, _agent)).Value.Ticket.Status.Should().Be(nameof(TicketStatus.InProgress));
        (await service.SetPriorityAsync(id, "urgent", _agent)).Value.Ticket.Priority.Should().Be(nameof(TicketPriority.Urgent));
        (await service.ResolveAsync(id, " ", _agent)).Error.Should().Be(SupportErrors.ReasonRequired);
        var resolved = (await service.ResolveAsync(id, "Deposit confirmed with the venue", _agent)).Value;
        resolved.ResolutionReason.Should().Be("Deposit confirmed with the venue");
        _recorder.Notifications.Should().Contain(n => n.Type == NotificationTypes.SupportTicketResolved && n.Data["reason"] == "Deposit confirmed with the venue");

        var reopened = (await service.ReplyAsRequesterAsync(id, _player, "Still not showing in the app")).Value;
        reopened.Ticket.Status.Should().Be(nameof(TicketStatus.InProgress), "it is still assigned");
        reopened.ResolutionReason.Should().BeNull();

        (await service.CloseAsync(id, null, _agent)).Error.Should().Be(SupportErrors.ReasonRequired);
        (await service.CloseAsync(id, "Duplicate of SUP-ABCDEF", _agent)).Value.Ticket.Status.Should().Be(nameof(TicketStatus.Closed));
        (await service.ReplyAsRequesterAsync(id, _player, "hello?")).Error.Should().Be(SupportErrors.Closed);
        _recorder.Audits.Select(a => a.Action).Should().Contain(["support.ticket.assigned", "support.ticket.priority_changed", "support.ticket.resolved", "support.ticket.closed"]);
        _recorder.Audits.Should().OnlyContain(a => a.EntityType == "SupportTicket");
    }

    [Fact]
    public async Task Queue_ShowsUrgentFirst_FiltersWork_AndVenueComplaintsAreListedAsync()
    {
        await using var db = NewDb();
        var service = Service(db);
        var venue = Guid.NewGuid();
        var low = (await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Other", "Idea", "Dark mode", null, null))).Value.Ticket.Id;
        await service.OpenAsync(_player, TicketRequesterKind.Player, new OpenTicketInput("Refund", "Refund", "Where is it", null, null));
        await service.OpenAsync(Guid.NewGuid(), TicketRequesterKind.Player, new OpenTicketInput("VenueComplaint", "Lights off", "Half the match in the dark", null, venue));
        await service.SetPriorityAsync(low, "Low", _agent);

        var queue = (await service.SearchAsync(new TicketFilter(null, null, null, null, false, null, null, null, null))).Value;
        queue.Items.Select(t => t.Priority).Should().Equal(nameof(TicketPriority.High), nameof(TicketPriority.Normal), nameof(TicketPriority.Low));
        (await service.SearchAsync(new TicketFilter(null, "venuecomplaint", null, null, true, null, venue, null, "lights"))).Value.Total.Should().Be(1);
        (await service.SearchAsync(new TicketFilter("Flying", null, null, null, false, null, null, null, null))).Error.Should().Be(SupportErrors.InvalidFilter);

        (await service.GetComplaintsAsync(venue, 10)).Should().ContainSingle(c => c.Subject == "Lights off");
        (await service.ListMineAsync(_player, 1, 20)).Total.Should().Be(2);
    }

    [Fact]
    public async Task ARequester_CannotFloodTheQueueAsync()
    {
        await using var db = NewDb();
        var service = Service(db);
        for (var i = 0; i < SupportTicketService.MaxOpenTicketsPerRequester; i++)
        {
            (await service.OpenAsync(_player, TicketRequesterKind.Venue, new OpenTicketInput("Other", $"Ticket {i}", "body", null, null))).IsSuccess.Should().BeTrue();
        }

        (await service.OpenAsync(_player, TicketRequesterKind.Venue, new OpenTicketInput("Other", "One more", "body", null, null))).Error.Should().Be(SupportErrors.TooManyOpen);
    }

    private SupportTicketService Service(SupportDbContext db) => new(db, _recorder, _recorder, _clock);

    private static SupportDbContext NewDb() => new(new DbContextOptionsBuilder<SupportDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Recorder : IAuditLog, INotificationPublisher
    {
        public List<AuditEntry> Audits { get; } = [];
        public List<NotificationRequest> Notifications { get; } = [];

        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Audits.Add(entry);
            return Task.CompletedTask;
        }

        public Task PublishAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            Notifications.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class Clock : IClock
    {
        private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
