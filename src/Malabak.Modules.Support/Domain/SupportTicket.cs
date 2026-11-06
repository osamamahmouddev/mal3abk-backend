using System.Security.Cryptography;
using Malabak.SharedKernel;

namespace Malabak.Modules.Support.Domain;

public enum TicketCategory
{
    Booking = 0,
    Deposit = 1,
    Refund = 2,
    VenueComplaint = 3,
    Account = 4,
    TeamMatchDispute = 5,
    Subscription = 6,
    Other = 7
}

public enum TicketPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public enum TicketStatus
{
    Open = 0,
    InProgress = 1,
    WaitingOnRequester = 2,
    Resolved = 3,
    Closed = 4
}

/// <summary>Who opened the ticket: a player from the app, or a venue owner/staff member from the dashboard.</summary>
public enum TicketRequesterKind
{
    Player = 0,
    Venue = 1
}

public enum TicketAuthorKind
{
    Requester = 0,
    Agent = 1
}

/// <summary>
/// A support ticket (TASK-706): a conversation between a requester and support agents, with internal notes agents only see,
/// assignment, priority and a mandatory resolution reason. Closed tickets are final; resolved ones reopen when the requester
/// writes again.
/// </summary>
public sealed class SupportTicket
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 4000;
    public const int MaxReasonLength = 1000;
    private const string NumberAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly List<TicketMessage> _messages = [];

    private SupportTicket()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Short reference people can read out on the phone, e.g. SUP-7KQ2MX.</summary>
    public string Number { get; private set; } = string.Empty;

    public Guid RequesterId { get; private set; }
    public TicketRequesterKind RequesterKind { get; private set; }
    public TicketCategory Category { get; private set; }
    public TicketPriority Priority { get; private set; }
    public TicketStatus Status { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public Guid? RelatedBookingId { get; private set; }
    public Guid? RelatedVenueId { get; private set; }
    public Guid? RelatedUserId { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public string? ResolutionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public IReadOnlyCollection<TicketMessage> Messages => _messages.AsReadOnly();

    public static Result<SupportTicket> Open(
        Guid requesterId, TicketRequesterKind kind, TicketCategory category, string? subject, string? body,
        Guid? relatedBookingId, Guid? relatedVenueId, Guid? relatedUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > MaxSubjectLength || !Enum.IsDefined(category) || !Enum.IsDefined(kind))
        {
            return Result.Failure<SupportTicket>(SupportErrors.Invalid);
        }

        if (category == TicketCategory.VenueComplaint && relatedVenueId is null)
        {
            return Result.Failure<SupportTicket>(SupportErrors.VenueRequired);
        }

        var ticket = new SupportTicket
        {
            Id = Guid.CreateVersion7(),
            Number = NewNumber(),
            RequesterId = requesterId,
            RequesterKind = kind,
            Category = category,
            Priority = DefaultPriority(category),
            Status = TicketStatus.Open,
            Subject = subject.Trim(),
            RelatedBookingId = relatedBookingId,
            RelatedVenueId = relatedVenueId,
            RelatedUserId = relatedUserId,
            CreatedAtUtc = nowUtc,
            LastActivityAtUtc = nowUtc
        };

        var first = ticket.AddMessage(requesterId, TicketAuthorKind.Requester, body, isInternal: false, nowUtc);
        return first.IsFailure ? Result.Failure<SupportTicket>(first.Error) : Result.Success(ticket);
    }

    /// <summary>Money problems come first: deposit and refund tickets start at high priority.</summary>
    public static TicketPriority DefaultPriority(TicketCategory category) =>
        category is TicketCategory.Deposit or TicketCategory.Refund ? TicketPriority.High : TicketPriority.Normal;

    public Result<TicketMessage> RequesterReply(Guid requesterId, string? body, DateTime nowUtc)
    {
        if (requesterId != RequesterId)
        {
            return Result.Failure<TicketMessage>(SupportErrors.NotFound);
        }

        if (Status == TicketStatus.Closed)
        {
            return Result.Failure<TicketMessage>(SupportErrors.Closed);
        }

        var message = AddMessage(requesterId, TicketAuthorKind.Requester, body, isInternal: false, nowUtc);
        if (message.IsSuccess && Status is TicketStatus.WaitingOnRequester or TicketStatus.Resolved)
        {
            // The requester answered or came back: the ticket needs an agent again.
            Status = AssigneeId is null ? TicketStatus.Open : TicketStatus.InProgress;
            ResolvedAtUtc = null;
            ResolutionReason = null;
        }

        return message;
    }

    /// <summary>An agent reply (visible to the requester) or an internal note (agents only).</summary>
    public Result<TicketMessage> AgentReply(Guid agentId, string? body, bool isInternal, DateTime nowUtc)
    {
        if (Status == TicketStatus.Closed)
        {
            return Result.Failure<TicketMessage>(SupportErrors.Closed);
        }

        var message = AddMessage(agentId, TicketAuthorKind.Agent, body, isInternal, nowUtc);
        if (message.IsSuccess && !isInternal && Status is TicketStatus.Open or TicketStatus.InProgress)
        {
            Status = TicketStatus.WaitingOnRequester;
        }

        return message;
    }

    public Result Assign(Guid? assigneeId, DateTime nowUtc)
    {
        if (Status == TicketStatus.Closed)
        {
            return Result.Failure(SupportErrors.Closed);
        }

        AssigneeId = assigneeId;
        if (assigneeId is not null && Status == TicketStatus.Open)
        {
            Status = TicketStatus.InProgress;
        }

        LastActivityAtUtc = nowUtc;
        return Result.Success();
    }

    public Result SetPriority(TicketPriority priority, DateTime nowUtc)
    {
        if (!Enum.IsDefined(priority))
        {
            return Result.Failure(SupportErrors.Invalid);
        }

        if (Status == TicketStatus.Closed)
        {
            return Result.Failure(SupportErrors.Closed);
        }

        Priority = priority;
        LastActivityAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Resolve(string? reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
        {
            return Result.Failure(SupportErrors.ReasonRequired);
        }

        if (Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            return Result.Failure(SupportErrors.InvalidTransition);
        }

        Status = TicketStatus.Resolved;
        ResolutionReason = reason.Trim();
        ResolvedAtUtc = nowUtc;
        LastActivityAtUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>Final: no more replies. Closing an unresolved ticket also needs a reason.</summary>
    public Result Close(string? reason, DateTime nowUtc)
    {
        if (Status == TicketStatus.Closed)
        {
            return Result.Failure(SupportErrors.InvalidTransition);
        }

        if (Status != TicketStatus.Resolved)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
            {
                return Result.Failure(SupportErrors.ReasonRequired);
            }

            ResolutionReason = reason.Trim();
            ResolvedAtUtc = nowUtc;
        }

        Status = TicketStatus.Closed;
        LastActivityAtUtc = nowUtc;
        return Result.Success();
    }

    private Result<TicketMessage> AddMessage(Guid authorId, TicketAuthorKind author, string? body, bool isInternal, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > MaxBodyLength)
        {
            return Result.Failure<TicketMessage>(SupportErrors.InvalidMessage);
        }

        var message = new TicketMessage(Guid.CreateVersion7(), Id, authorId, author, body.Trim(), isInternal, nowUtc);
        _messages.Add(message);
        LastActivityAtUtc = nowUtc;
        return Result.Success(message);
    }

    private static string NewNumber() =>
        "SUP-" + new string(RandomNumberGenerator.GetItems<char>(NumberAlphabet, 6));
}

public sealed class TicketMessage
{
    private TicketMessage()
    {
    }

    internal TicketMessage(Guid id, Guid ticketId, Guid authorId, TicketAuthorKind authorKind, string body, bool isInternal, DateTime createdAtUtc)
    {
        Id = id;
        TicketId = ticketId;
        AuthorId = authorId;
        AuthorKind = authorKind;
        Body = body;
        IsInternal = isInternal;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid AuthorId { get; private set; }
    public TicketAuthorKind AuthorKind { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public bool IsInternal { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

public static class SupportErrors
{
    public static readonly Error Invalid = Error.Validation("Support.Invalid", "Choose a category and enter a subject (up to 200 characters).");
    public static readonly Error InvalidMessage = Error.Validation("Support.InvalidMessage", "Write a message of up to 4000 characters.");
    public static readonly Error VenueRequired = Error.Validation("Support.VenueRequired", "Choose the venue the complaint is about.");
    public static readonly Error ReasonRequired = Error.Validation("Support.ReasonRequired", "A resolution reason is required.");
    public static readonly Error NotFound = Error.NotFound("Support.NotFound", "Ticket not found.");
    public static readonly Error Closed = Error.Conflict("Support.Closed", "This ticket is closed. Open a new ticket instead.");
    public static readonly Error InvalidTransition = Error.Conflict("Support.InvalidTransition", "The ticket cannot move to that status.");
    public static readonly Error InvalidFilter = Error.Validation("Support.InvalidFilter", "Invalid ticket filter.");
    public static readonly Error TooManyOpen = Error.Conflict("Support.TooManyOpen", "You already have many open tickets. Please wait for a reply.");
}
