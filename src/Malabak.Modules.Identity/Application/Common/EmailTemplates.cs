using System.Net;

namespace Malabak.Modules.Identity.Application.Common;

/// <summary>A rendered email: plain text for every client plus an HTML version.</summary>
public sealed record RenderedEmail(string Subject, string Text, string Html);

/// <summary>
/// Account email templates. Bilingual in one message (Arabic first, then English) because a user's language isn't
/// known when we email them. Every dynamic value is HTML-encoded so a venue name can't inject markup into the HTML part.
/// </summary>
public static class EmailTemplates
{
    public static RenderedEmail EmailVerification(string link) => Build(
        "تأكيد بريدك الإلكتروني | Verify your email",
        "اضغط على الرابط لتأكيد بريدك الإلكتروني في ملعبك (صالح 24 ساعة):",
        "Confirm your Malabak email address (link valid for 24 hours):",
        link,
        "إذا لم تنشئ حساباً، تجاهل هذه الرسالة.",
        "If you didn't create an account, ignore this email.");

    public static RenderedEmail PasswordReset(string link) => Build(
        "إعادة تعيين كلمة المرور | Reset your password",
        "طلبت إعادة تعيين كلمة المرور. الرابط صالح لمدة 30 دقيقة ولمرة واحدة:",
        "You asked to reset your password. The link works once and expires in 30 minutes:",
        link,
        "إذا لم تطلب ذلك، تجاهل الرسالة؛ كلمة مرورك لم تتغير.",
        "If this wasn't you, ignore this email — your password hasn't changed.");

    public static RenderedEmail StaffInvitation(string venueName, string role, string link) => Build(
        $"دعوة للانضمام إلى {venueName} | Invitation to join {venueName}",
        $"تمت دعوتك للانضمام إلى {venueName} على ملعبك بدور {role}. الدعوة صالحة 48 ساعة:",
        $"You've been invited to join {venueName} on Malabak as {role}. The invitation is valid for 48 hours:",
        link,
        "يجب قبول الدعوة من الحساب المرتبط بهذا البريد.",
        "Accept it while signed in with the account that uses this email address.");

    public static RenderedEmail NewDeviceSignIn(string device, DateTime atUtc) => Build(
        "تسجيل دخول جديد إلى حسابك | New sign-in to your account",
        $"تم تسجيل الدخول إلى حسابك من {device} في {atUtc:yyyy-MM-dd HH:mm} UTC.",
        $"Your account was signed in from {device} at {atUtc:yyyy-MM-dd HH:mm} UTC.",
        link: null,
        "إذا لم تكن أنت، سجّل الخروج من كل الأجهزة من الإعدادات وتواصل مع الدعم.",
        "If this wasn't you, sign out all devices from settings and contact support.");

    public static RenderedEmail EmailChangeCode(string code) => Build(
        "كود تأكيد البريد الجديد | Confirm your new email",
        $"استخدم الكود {code} لتأكيد بريدك الإلكتروني الجديد في ملعبك. الكود صالح 15 دقيقة.",
        $"Use the code {code} to confirm your new Malabak email address. It expires in 15 minutes.",
        link: null,
        "إذا لم تطلب ذلك، تجاهل الرسالة؛ لن يتغير شيء.",
        "If you didn't ask for this, ignore this email; nothing will change.");

    public static RenderedEmail EmailChanged(string newEmailMasked) => Build(
        "تم تغيير بريد حسابك | Your account email was changed",
        $"تم تغيير البريد الإلكتروني لحسابك في ملعبك إلى {newEmailMasked}، وتم تسجيل الخروج من باقي الأجهزة.",
        $"Your Malabak account email was changed to {newEmailMasked}, and your other devices were signed out.",
        link: null,
        "إذا لم تكن أنت، تواصل مع الدعم فوراً.",
        "If this wasn't you, contact support right away.");

    private static RenderedEmail Build(string subject, string introAr, string introEn, string? link, string footerAr, string footerEn)
    {
        var text = string.Join("\n\n", new[] { introAr, link, footerAr, "—", introEn, link, footerEn }.Where(s => s is not null));

        var html = $"""
            <div dir="rtl" lang="ar" style="font-family:Tajawal,Arial,sans-serif">
              <p>{Encode(introAr)}</p>{LinkHtml(link)}<p style="color:#666">{Encode(footerAr)}</p>
            </div>
            <hr>
            <div dir="ltr" lang="en" style="font-family:Inter,Arial,sans-serif">
              <p>{Encode(introEn)}</p>{LinkHtml(link)}<p style="color:#666">{Encode(footerEn)}</p>
            </div>
            """;

        return new RenderedEmail(subject.ReplaceLineEndings(" "), text, html);
    }

    private static string LinkHtml(string? link) =>
        link is null ? string.Empty : $"<p><a href=\"{Encode(link)}\">{Encode(link)}</a></p>";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
