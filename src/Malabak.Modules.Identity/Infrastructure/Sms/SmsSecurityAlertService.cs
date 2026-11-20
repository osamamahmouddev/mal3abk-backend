using System.Globalization;
using Malabak.Modules.Identity.Application.Common;

namespace Malabak.Modules.Identity.Infrastructure.Sms;

/// <summary>
/// Renders security alerts as SMS text and sends them through the cost-controlled <see cref="ISmsGateway"/>.
/// The body carries recovery and invitation links, so it is never logged; the gateway records the outcome
/// against the masked number only.
/// </summary>
public sealed class SmsSecurityAlertService : ISmsAlertService
{
    private readonly ISmsGateway _gateway;

    public SmsSecurityAlertService(ISmsGateway gateway)
    {
        _gateway = gateway;
    }

    public Task SendSecurityAlertAsync(string phoneNumber, SecurityAlert alert, CancellationToken cancellationToken = default) =>
        _gateway.SendAsync(phoneNumber, BuildBody(alert), SmsTemplates.SecurityAlert, SmsChannel.Sms, cancellationToken);

    public static string BuildBody(SecurityAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return alert.Kind switch
        {
            SecurityAlertKind.PhoneChanged =>
                $"ملعبك: تم تغيير رقم حسابك إلى {PhoneNumberMasker.Mask(alert.NewPhoneNumber ?? string.Empty)}. لو مش إنت، استرجع رقمك خلال 24 ساعة: {alert.RecoveryLink}\n" +
                $"Malabak: your account number was changed. Not you? Restore it within 24 hours: {alert.RecoveryLink}",
            SecurityAlertKind.PhoneRestored =>
                "ملعبك: تم استرجاع رقم حسابك وتسجيل الخروج من كل الأجهزة الأخرى.\n" +
                "Malabak: your number was restored and all other devices were signed out.",
            SecurityAlertKind.NewDeviceSignIn =>
                $"ملعبك: تسجيل دخول جديد من {alert.DeviceName} الساعة {FormatUtc(alert.OccurredAtUtc)} UTC. لو مش إنت، اقفل كل الأجهزة من البروفايل.\n" +
                $"Malabak: new sign-in from {alert.DeviceName} at {FormatUtc(alert.OccurredAtUtc)} UTC. Not you? Sign out all devices from your profile.",
            SecurityAlertKind.StaffInvitation =>
                $"ملعبك: اتدعيت للانضمام إلى {alert.VenueName} بدور {alert.Role}. اقبل الدعوة خلال 48 ساعة: {alert.InvitationLink}\n" +
                $"Malabak: you're invited to join {alert.VenueName} as {alert.Role}. Accept within 48 hours: {alert.InvitationLink}",
            SecurityAlertKind.AccountActivation =>
                $"ملعبك: أهلاً بيك! فعّل حساب ملعبك من اللينك ده: {alert.ActivationLink}\n" +
                $"Malabak: welcome! Activate your venue account: {alert.ActivationLink}",
            _ => throw new ArgumentOutOfRangeException(nameof(alert), alert.Kind, "Unknown security alert kind."),
        };
    }

    private static string FormatUtc(DateTime? value) =>
        value?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
}
