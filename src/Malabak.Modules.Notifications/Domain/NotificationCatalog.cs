using Malabak.SharedKernel.Contracts;

namespace Malabak.Modules.Notifications.Domain;

public sealed record NotificationTemplate(
    NotificationCategory Category,
    string TitleAr,
    string BodyAr,
    string TitleEn,
    string BodyEn,
    Func<IReadOnlyDictionary<string, string>, string?> DeepLink);

/// <summary>Builds app deep links (malabak://…) from notification data.</summary>
public static class DeepLinks
{
    public static string Booking(string bookingId) => $"malabak://booking/{Uri.EscapeDataString(bookingId)}";
    public static string Venue(string venueId) => $"malabak://venue/{Uri.EscapeDataString(venueId)}";
    public static string VenueManage(string venueId) => $"malabak://venue-manager/venue/{Uri.EscapeDataString(venueId)}";
    public static string Team(string teamId) => $"malabak://team/{Uri.EscapeDataString(teamId)}";
    public static string Match(string matchId) => $"malabak://match/{Uri.EscapeDataString(matchId)}";
    public static string Challenge(string challengeId) => $"malabak://challenge/{Uri.EscapeDataString(challengeId)}";
    public const string SecuritySettings = "malabak://settings/security";
    public const string Billing = "malabak://venue-manager/billing";
    public static string SupportTicket(string ticketId) => $"malabak://support/tickets/{Uri.EscapeDataString(ticketId)}";

    internal static string? From(IReadOnlyDictionary<string, string> data, string key, Func<string, string> build) =>
        data.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? build(value) : null;
}

/// <summary>Arabic (primary) and English templates; placeholders are {name} keys from the notification data.</summary>
public static class NotificationCatalog
{
    private static readonly IReadOnlyDictionary<string, NotificationTemplate> _templates = new Dictionary<string, NotificationTemplate>(StringComparer.Ordinal)
    {
        [NotificationTypes.BookingConfirmed] = new(NotificationCategory.Bookings,
            "تم تأكيد حجزك", "تم تأكيد حجزك في {venueName} يوم {date}.",
            "Booking confirmed", "Your booking at {venueName} on {date} is confirmed.",
            d => DeepLinks.From(d, "bookingId", DeepLinks.Booking)),
        [NotificationTypes.BookingRejected] = new(NotificationCategory.Bookings,
            "تم رفض الحجز", "رفض {venueName} حجزك. السبب: {reason}",
            "Booking rejected", "{venueName} rejected your booking. Reason: {reason}",
            d => DeepLinks.From(d, "bookingId", DeepLinks.Booking)),
        [NotificationTypes.BookingExpired] = new(NotificationCategory.Bookings,
            "انتهت مهلة الحجز", "انتهت مهلة رفع إيصال الدفع لحجزك في {venueName}.",
            "Booking expired", "The payment window for your booking at {venueName} has ended.",
            d => DeepLinks.From(d, "bookingId", DeepLinks.Booking)),
        [NotificationTypes.PaymentProofSubmitted] = new(NotificationCategory.Payments,
            "إيصال دفع جديد", "تم رفع إيصال دفع لحجز في {venueName} وينتظر المراجعة.",
            "New payment proof", "A payment proof for a booking at {venueName} is waiting for review.",
            d => DeepLinks.From(d, "bookingId", DeepLinks.Booking)),
        [NotificationTypes.BookingReminder24h] = new(NotificationCategory.Bookings,
            "تذكير بحجزك غدًا", "حجزك في {venueName} يوم {date}. نراك في الملعب!",
            "Your booking is tomorrow", "Your booking at {venueName} is on {date}. See you on the pitch!",
            d => DeepLinks.From(d, "bookingId", DeepLinks.Booking)),
        [NotificationTypes.BookingReminder2h] = new(NotificationCategory.Bookings,
            "حجزك بعد ساعتين", "يبدأ حجزك في {venueName} يوم {date} بعد ساعتين تقريبًا.",
            "Your booking starts in 2 hours", "Your booking at {venueName} on {date} starts in about 2 hours.",
            d => DeepLinks.From(d, "bookingId", DeepLinks.Booking)),
        [NotificationTypes.VenueApproved] = new(NotificationCategory.Venues,
            "تمت الموافقة على ملعبك", "تمت الموافقة على {venueName}. يمكنك نشره الآن ليظهر للاعبين.",
            "Venue approved", "{venueName} was approved. Publish it to make it visible to players.",
            d => DeepLinks.From(d, "venueId", DeepLinks.VenueManage)),
        [NotificationTypes.VenueRejected] = new(NotificationCategory.Venues,
            "لم تتم الموافقة على ملعبك", "لم تتم الموافقة على {venueName}. السبب: {reason}",
            "Venue not approved", "{venueName} wasn't approved. Reason: {reason}",
            d => DeepLinks.From(d, "venueId", DeepLinks.VenueManage)),
        [NotificationTypes.VenueSuspended] = new(NotificationCategory.Venues,
            "تم إيقاف ملعبك مؤقتاً", "تم إيقاف {venueName} ولن يظهر للاعبين. السبب: {reason}",
            "Venue suspended", "{venueName} was suspended and is hidden from players. Reason: {reason}",
            d => DeepLinks.From(d, "venueId", DeepLinks.VenueManage)),
        [NotificationTypes.VenueReactivated] = new(NotificationCategory.Venues,
            "تمت إعادة تفعيل ملعبك", "تمت إعادة تفعيل {venueName}.",
            "Venue reactivated", "{venueName} is active again.",
            d => DeepLinks.From(d, "venueId", DeepLinks.VenueManage)),
        [NotificationTypes.VenueOwnershipReceived] = new(NotificationCategory.Venues,
            "أصبحت مالك ملعب", "تم نقل ملكية {venueName} إلى حسابك.",
            "You now own a venue", "Ownership of {venueName} was moved to your account.",
            d => DeepLinks.From(d, "venueId", DeepLinks.VenueManage)),
        [NotificationTypes.VenueInfoRequested] = new(NotificationCategory.Venues,
            "مطلوب استكمال بيانات الملعب", "طلب استكمال بيانات {venueName}: {message}",
            "Additional information needed", "Additional information needed for {venueName}: {message}",
            d => DeepLinks.From(d, "venueId", DeepLinks.VenueManage)),
        [NotificationTypes.StaffInvitationAccepted] = new(NotificationCategory.Venues,
            "انضم موظف جديد", "انضم موظف جديد إلى {venueName} بدور {role}.",
            "New staff member", "A new staff member joined {venueName} as {role}.",
            d => DeepLinks.From(d, "venueId", DeepLinks.VenueManage)),
        [NotificationTypes.ImpersonationStarted] = new(NotificationCategory.Security,
            "دخل فريق الدعم على حسابك", "دخل أحد أفراد فريق دعم ملعبك على حسابك للمساعدة. السبب: {reason}. لو مش انت اللي طلبت ده تواصل معنا.",
            "Support accessed your account", "A Malabak support agent signed in to your account to help. Reason: {reason}. Contact us if you did not ask for this.",
            _ => DeepLinks.SecuritySettings),
        [NotificationTypes.NewDeviceSignIn] = new(NotificationCategory.Security,
            "تسجيل دخول من جهاز جديد", "تم تسجيل الدخول إلى حسابك من {device}. إذا لم تكن أنت، سجّل الخروج من كل الأجهزة.",
            "New device sign-in", "Your account was signed in from {device}. If this wasn't you, sign out all devices.",
            _ => DeepLinks.SecuritySettings),
        [NotificationTypes.TeamInvitationReceived] = new(NotificationCategory.Teams,
            "دعوة للانضمام إلى فريق", "تمت دعوتك للانضمام إلى فريق {teamName}. كود الدعوة: {inviteCode}",
            "Team invitation", "You have been invited to join {teamName}. Invite code: {inviteCode}",
            d => DeepLinks.From(d, "teamId", DeepLinks.Team)),
        [NotificationTypes.TeamChallengeReceived] = new(NotificationCategory.Teams,
            "تحدي جديد", "فريق {teamName} يتحداك!",
            "New challenge", "{teamName} challenged your team!",
            d => DeepLinks.From(d, "challengeId", DeepLinks.Challenge) ?? DeepLinks.From(d, "teamId", DeepLinks.Team)),
        [NotificationTypes.TeamChallengeAccepted] = new(NotificationCategory.Teams,
            "تم قبول التحدي!", "وافق فريق {teamName} على التحدي وتمت جدولة المباراة يوم {matchDate}.",
            "Challenge accepted!", "{teamName} accepted your challenge! Match is scheduled for {matchDate}.",
            d => DeepLinks.From(d, "matchId", DeepLinks.Match) ?? DeepLinks.From(d, "teamId", DeepLinks.Team)),
        [NotificationTypes.TeamChallengeDeclined] = new(NotificationCategory.Teams,
            "تم رفض التحدي", "اعتذر فريق {teamName} عن قبول التحدي. السبب: {reason}",
            "Challenge declined", "{teamName} declined your challenge. Reason: {reason}",
            d => DeepLinks.From(d, "teamId", DeepLinks.Team)),
        [NotificationTypes.TeamChallengeCountered] = new(NotificationCategory.Teams,
            "موعد بديل للتحدي", "اقترح فريق {teamName} موعداً بديلاً للتحدي يوم {newDate}.",
            "Challenge counter-proposal", "{teamName} proposed an alternative time for the challenge on {newDate}.",
            d => DeepLinks.From(d, "challengeId", DeepLinks.Challenge) ?? DeepLinks.From(d, "teamId", DeepLinks.Team)),
        [NotificationTypes.TeamRankingUpdated] = new(NotificationCategory.Teams,
            "تحديث تصنيف الفريق", "تم تحديث تصنيف فريقك {teamName} ليصبح {newRating} نقطة.",
            "Team ranking updated", "Your team {teamName} rating was updated to {newRating} pts.",
            d => DeepLinks.From(d, "teamId", DeepLinks.Team)),
        [NotificationTypes.MatchScheduled] = new(NotificationCategory.Teams,
            "مباراة جديدة مجدولة", "تم تحديد موعد مباراة جديدة لفريقك يوم {matchDate}.",
            "Match scheduled", "A new match has been scheduled for your team on {matchDate}.",
            d => DeepLinks.From(d, "matchId", DeepLinks.Match)),
        [NotificationTypes.MatchResultSubmitted] = new(NotificationCategory.Teams,
            "نتيجة مباراة بحاجة للتأكيد", "قام كابتن فريق {submittingTeam} بإدخال نتيجة المباراة ({score}). برجاء التأكيد أو الاعتراض.",
            "Match result submitted", "Captain of {submittingTeam} submitted match score ({score}). Please confirm or dispute.",
            d => DeepLinks.From(d, "matchId", DeepLinks.Match)),
        [NotificationTypes.MatchResultConfirmed] = new(NotificationCategory.Teams,
            "تم تأكيد نتيجة المباراة", "تم تأكيد نتيجة مباراتكم ({score}) رسمياً.",
            "Match result confirmed", "Your match result ({score}) has been officially confirmed.",
            d => DeepLinks.From(d, "matchId", DeepLinks.Match)),
        [NotificationTypes.MatchResultDisputed] = new(NotificationCategory.Teams,
            "تم الاعتراض على نتيجة المباراة", "قام كابتن فريق {disputingTeam} بالاعتراض على نتيجة المباراة وتم تحويلها للمراجعة.",
            "Match result disputed", "{disputingTeam} disputed the match result and it has been routed to moderation.",
            d => DeepLinks.From(d, "matchId", DeepLinks.Match)),
        [NotificationTypes.MatchDisputeResolved] = new(NotificationCategory.Teams,
            "تم الفصل في نتيجة المباراة المتنازع عليها", "تم الفصل في النزاع حول مباراتكم بواسطة إدارة المنصة: {decision}",
            "Match dispute resolved", "The dispute regarding your match has been resolved by platform administration: {decision}",
            d => DeepLinks.From(d, "matchId", DeepLinks.Match)),
        [NotificationTypes.TeamSuspended] = new(NotificationCategory.Teams,
            "تم إيقاف فريقك", "تم إيقاف {teamName} مؤقتاً. السبب: {reason}",
            "Team suspended", "{teamName} was suspended. Reason: {reason}",
            d => DeepLinks.From(d, "teamId", DeepLinks.Team)),
        [NotificationTypes.TeamReactivated] = new(NotificationCategory.Teams,
            "تمت إعادة تفعيل فريقك", "تمت إعادة تفعيل {teamName}.",
            "Team reactivated", "{teamName} is active again.",
            d => DeepLinks.From(d, "teamId", DeepLinks.Team)),
        [NotificationTypes.MatchResultCorrected] = new(NotificationCategory.Teams,
            "تم تصحيح نتيجة مباراة", "صححت إدارة المنصة نتيجة مباراتكم إلى {score}. السبب: {reason}",
            "Match result corrected", "Platform administration corrected your match result to {score}. Reason: {reason}",
            d => DeepLinks.From(d, "matchId", DeepLinks.Match)),
        [NotificationTypes.SubscriptionExpiring] = new(NotificationCategory.Payments,
            "اشتراكك قرب يخلص", "اشتراك باقة {planName} بينتهي يوم {endDate}. جدّده عشان تقدر تضيف ملاعب وموظفين.",
            "Your subscription ends soon", "Your {planName} subscription ends on {endDate}. Renew it to keep adding pitches and staff.",
            _ => DeepLinks.Billing),
        [NotificationTypes.SubscriptionGraceStarted] = new(NotificationCategory.Payments,
            "فترة سماح للاشتراك", "اشتراكك خلص، وعندك فترة سماح لحد {graceEnd}. الحجوزات شغالة عادي.",
            "Subscription grace period", "Your subscription ended; you have a grace period until {graceEnd}. Bookings keep working.",
            _ => DeepLinks.Billing),
        [NotificationTypes.SubscriptionExpired] = new(NotificationCategory.Payments,
            "انتهى الاشتراك", "اشتراكك انتهى. الحجوزات الحالية شغالة، لكن مش هتقدر تضيف ملاعب أو موظفين لحد التجديد.",
            "Subscription expired", "Your subscription expired. Existing bookings keep working, but you can't add pitches or staff until you renew.",
            _ => DeepLinks.Billing),
        [NotificationTypes.SubscriptionPaymentRecorded] = new(NotificationCategory.Payments,
            "تم تسجيل الدفع", "اتسجل دفع {amount} لاشتراك {planName}، والاشتراك ساري لحد {endDate}. الفاتورة رقم {invoiceNumber}.",
            "Payment recorded", "A payment of {amount} for {planName} was recorded; your subscription runs until {endDate}. Invoice {invoiceNumber}.",
            _ => DeepLinks.Billing),
        // Support conversations are transactional (the person asked for help), so they use the always-on category.
        [NotificationTypes.SupportTicketReplied] = new(NotificationCategory.Security,
            "رد جديد من الدعم", "رد فريق الدعم على تذكرتك {ticketNumber}.",
            "New reply from support", "Support replied to your ticket {ticketNumber}.",
            d => DeepLinks.From(d, "ticketId", DeepLinks.SupportTicket)),
        [NotificationTypes.SupportTicketResolved] = new(NotificationCategory.Security,
            "تم حل تذكرتك", "تم حل تذكرتك {ticketNumber}: {reason}",
            "Ticket resolved", "Your ticket {ticketNumber} was resolved: {reason}",
            d => DeepLinks.From(d, "ticketId", DeepLinks.SupportTicket)),
        [NotificationTypes.PaymentDisputeOpened] = new(NotificationCategory.Payments,
            "تم فتح نزاع على الدفع", "تم فتح نزاع على دفع الحجز {bookingReference} وسيتم مراجعته من فريق ملعبك.",
            "Payment dispute opened", "A payment dispute was opened for booking {bookingReference}; the Malabak team will review it.",
            d => DeepLinks.From(d, "bookingId", DeepLinks.Booking)),
        [NotificationTypes.PaymentDisputeResolved] = new(NotificationCategory.Payments,
            "تم الفصل في النزاع", "تم الفصل في نزاع الحجز {bookingReference}: {outcome}",
            "Payment dispute resolved", "The dispute on booking {bookingReference} was resolved: {outcome}",
            d => DeepLinks.From(d, "bookingId", DeepLinks.Booking)),
        [NotificationTypes.SubscriptionSuspended] = new(NotificationCategory.Payments,
            "تم إيقاف الاشتراك", "تم إيقاف اشتراكك: {reason}. تواصل مع الدعم.",
            "Subscription suspended", "Your subscription was suspended: {reason}. Please contact support.",
            _ => DeepLinks.Billing),
        [NotificationTypes.SubscriptionRequestApproved] = new(NotificationCategory.Payments,
            "تمت ترقية باقتك", "تمت الموافقة على طلب ترقية باقتك إلى {planName} بنجاح.",
            "Plan upgraded", "Your subscription has been upgraded to {planName}.",
            _ => DeepLinks.Billing),
        [NotificationTypes.SubscriptionRequestRejected] = new(NotificationCategory.Payments,
            "طلب ترقية الباقة", "تم رفض طلب ترقية الباقة: {reason}",
            "Plan upgrade rejected", "Your plan upgrade request was rejected: {reason}",
            _ => DeepLinks.Billing),
        [NotificationTypes.SubscriptionRequestInstructions] = new(NotificationCategory.Payments,
            "تعليمات سداد الباقة", "{instructions}",
            "Payment instructions", "{instructions}",
            _ => DeepLinks.Billing),
        [NotificationTypes.SubscriptionPaymentReported] = new(NotificationCategory.Payments,
            "تم إرسال إيصال السداد", "قام المالك {ownerName} بإرسال إيصال سداد لطلب ترقية باقة {planName}.",
            "Payment receipt submitted", "Owner {ownerName} submitted payment receipt for {planName} plan upgrade.",
            _ => DeepLinks.Billing)
    };

    public static bool TryGet(string type, out NotificationTemplate template) => _templates.TryGetValue(type, out template!);

    public static IEnumerable<string> Types => _templates.Keys;

    /// <summary>
    /// Which app a notification type belongs to. Security notices, support replies and payment-dispute outcomes concern the
    /// whole account (the ticket or dispute may have been opened from either app), so they show in every app.
    /// Unknown types default to the player app, where they have always appeared.
    /// </summary>
    public static IReadOnlyCollection<NotificationSurface> SurfacesOf(string type) => type switch
    {
        _ when type.StartsWith("security.", StringComparison.Ordinal)
            || type.StartsWith("support.", StringComparison.Ordinal)
            || type == NotificationTypes.PaymentDisputeResolved => [NotificationSurface.Player, NotificationSurface.Venue, NotificationSurface.Admin],
        NotificationTypes.PaymentProofSubmitted or NotificationTypes.PaymentDisputeOpened => [NotificationSurface.Venue],
        _ when type.StartsWith("venue.", StringComparison.Ordinal)
            || type.StartsWith("staff.", StringComparison.Ordinal)
            || type.StartsWith("subscription.", StringComparison.Ordinal) => [NotificationSurface.Venue],
        _ => [NotificationSurface.Player]
    };

    /// <summary>Every known type that appears in <paramref name="surface"/>.</summary>
    public static IReadOnlyList<string> TypesFor(NotificationSurface surface) =>
        _templates.Keys.Where(type => SurfacesOf(type).Contains(surface)).ToList();

    /// <summary>Fills {placeholders}; missing values render empty rather than leaking the raw key.</summary>
    public static string Render(string text, IReadOnlyDictionary<string, string> data)
    {
        var result = text;
        foreach (var (key, value) in data)
        {
            result = result.Replace("{" + key + "}", value, StringComparison.Ordinal);
        }

        return System.Text.RegularExpressions.Regex.Replace(result, "\\{[a-zA-Z]+\\}", string.Empty,
            System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
    }
}
