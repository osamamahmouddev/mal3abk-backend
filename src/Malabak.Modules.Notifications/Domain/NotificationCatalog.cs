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
    public const string SecuritySettings = "malabak://settings/security";

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
        [NotificationTypes.VenueInfoRequested] = new(NotificationCategory.Venues,
            "مطلوب معلومات إضافية", "نحتاج معلومات إضافية عن {venueName}: {message}",
            "More information needed", "We need more information about {venueName}: {message}",
            d => DeepLinks.From(d, "venueId", DeepLinks.VenueManage)),
        [NotificationTypes.StaffInvitationAccepted] = new(NotificationCategory.Venues,
            "انضم موظف جديد", "انضم موظف جديد إلى {venueName} بدور {role}.",
            "New staff member", "A new staff member joined {venueName} as {role}.",
            d => DeepLinks.From(d, "venueId", DeepLinks.VenueManage)),
        [NotificationTypes.NewDeviceSignIn] = new(NotificationCategory.Security,
            "تسجيل دخول من جهاز جديد", "تم تسجيل الدخول إلى حسابك من {device}. إذا لم تكن أنت، سجّل الخروج من كل الأجهزة.",
            "New device sign-in", "Your account was signed in from {device}. If this wasn't you, sign out all devices.",
            _ => DeepLinks.SecuritySettings),
        [NotificationTypes.TeamChallengeReceived] = new(NotificationCategory.Teams,
            "تحدي جديد", "فريق {teamName} يتحداك!",
            "New challenge", "{teamName} challenged your team!",
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
            d => DeepLinks.From(d, "matchId", DeepLinks.Match))
    };

    public static bool TryGet(string type, out NotificationTemplate template) => _templates.TryGetValue(type, out template!);

    public static IEnumerable<string> Types => _templates.Keys;

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
