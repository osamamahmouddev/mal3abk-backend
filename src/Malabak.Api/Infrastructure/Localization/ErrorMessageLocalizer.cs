using System.Globalization;

namespace Malabak.Api.Infrastructure.Localization;

public sealed class ErrorMessageLocalizer : IErrorMessageLocalizer
{
    private static readonly Dictionary<string, string> _arabicMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["validation_failed"] = "حدث خطأ في التحقق من صحة البيانات المدخلة.",
        ["idempotency_key_required"] = "مفتاح العمليات المتكررة (Idempotency-Key) مطلوب لهذا الطلب.",
        ["idempotency_conflict"] = "يوجد طلب آخر قيد التنفيذ بنفس المفتاح، يرجى المحاولة بعد لحظات.",
        ["idempotency_body_mismatch"] = "تم استخدام هذا المفتاح مسبقاً ببيانات طلب مختلفة.",
        ["invalid_idempotency_key"] = "قيمة مفتاح الـ Idempotency غير صالحة وتتجاوز 256 حرفاً.",
        ["rate_limited"] = "تم تجاوز الحد المسموح به من الطلبات، يرجى الانتظار والمحاولة لاحقاً.",
        ["unauthorized"] = "غير مصرح، يرجى تسجيل الدخول أولاً.",
        ["forbidden"] = "ليس لديك الصلاحية الكافية للوصول إلى هذا المورد.",
        ["not_found"] = "المورد المطلوب غير موجود.",
        ["conflict"] = "تعارض في حالة المورد أو العملية المطلوبة.",
        ["internal_error"] = "حدث خطأ غير متوقع في الخادم، يرجى المحاولة لاحقاً.",
        ["auth_invalid_credentials"] = "بيانات الدخول غير صحيحة.",
        ["auth_user_not_found"] = "المستخدم غير موجود.",
        ["PhoneNumber.Empty"] = "رقم الهاتف مطلوب ولا يمكن أن يكون فارغاً.",
        ["PhoneNumber.InvalidEgyptianNumber"] = "رقم الهاتف غير صالح، يجب أن يكون رقماً مصرياً يبدأ بـ 010 أو 011 أو 012 أو 015.",
        ["PhoneNumber.InvalidFormat"] = "صيغة رقم الهاتف غير صالحة.",
        ["Money.NegativeAmount"] = "المبلغ المالي لا يمكن أن يكون سالباً.",
        ["Money.InvalidCurrency"] = "العملة غير مدعومة، العملة المعتمدة هي الجنيه المصري (EGP).",
        ["TimeSlotRange.InvalidRange"] = "وقت بداية الحجز يجب أن يكون قبل وقت النهاية.",
        ["TimeSlotRange.ZeroOrNegativeDuration"] = "مدة الحجز يجب أن تكون أكبر من صفر.",
        ["TimeSlotRange.ExceedsMaxDuration"] = "مدة الحجز تتجاوز الحد الأقصى المسموح به (6 ساعات)."
    };

    private static readonly Dictionary<string, string> _englishMessages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["validation_failed"] = "One or more validation errors occurred.",
        ["idempotency_key_required"] = "The Idempotency-Key header is required for this request.",
        ["idempotency_conflict"] = "A concurrent request with this Idempotency-Key is currently being processed.",
        ["idempotency_body_mismatch"] = "Idempotency-Key already used with a different request payload.",
        ["invalid_idempotency_key"] = "Idempotency-Key header value exceeds the 256 character limit.",
        ["rate_limited"] = "Too many requests. Please try again later.",
        ["unauthorized"] = "Authentication is required to access this resource.",
        ["forbidden"] = "You do not have permission to access this resource.",
        ["not_found"] = "The requested resource was not found.",
        ["conflict"] = "A conflict occurred with the current state of the resource.",
        ["internal_error"] = "An unexpected error occurred. Please try again later.",
        ["auth_invalid_credentials"] = "Invalid authentication credentials.",
        ["auth_user_not_found"] = "User account was not found.",
        ["PhoneNumber.Empty"] = "Phone number is required.",
        ["PhoneNumber.InvalidEgyptianNumber"] = "Phone number must be a valid Egyptian mobile number (010, 011, 012, 015).",
        ["PhoneNumber.InvalidFormat"] = "Phone number format is invalid.",
        ["Money.NegativeAmount"] = "Money amount cannot be negative.",
        ["Money.InvalidCurrency"] = "Currency is not supported; only EGP is supported.",
        ["TimeSlotRange.InvalidRange"] = "Slot start time must be before end time.",
        ["TimeSlotRange.ZeroOrNegativeDuration"] = "Slot duration must be greater than zero.",
        ["TimeSlotRange.ExceedsMaxDuration"] = "Slot duration exceeds the maximum allowed duration (6 hours)."
    };

    private static readonly Dictionary<string, (string Arabic, string English)> _titles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Validation"] = ("خطأ في صحة البيانات", "Validation Error"),
        ["Unauthorized"] = ("غير مصرح", "Unauthorized"),
        ["Forbidden"] = ("ممنوع الوصول", "Forbidden"),
        ["NotFound"] = ("غير موجود", "Not Found"),
        ["Conflict"] = ("تعارض في البيانات", "Conflict"),
        ["Failure"] = ("تعذر معالجة الطلب", "Unprocessable Entity")
    };

    public string GetLocalizedMessage(string errorCode, string? defaultDescription = null, CultureInfo? culture = null)
    {
        var targetCulture = culture ?? CultureInfo.CurrentUICulture;
        var isArabic = targetCulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase);

        var dictionary = isArabic ? _arabicMessages : _englishMessages;
        if (dictionary.TryGetValue(errorCode, out var localizedMessage))
        {
            return localizedMessage;
        }

        if (!string.IsNullOrWhiteSpace(defaultDescription))
        {
            return defaultDescription;
        }

        if (!isArabic && _arabicMessages.TryGetValue(errorCode, out var arFallback))
        {
            return arFallback;
        }

        if (isArabic && _englishMessages.TryGetValue(errorCode, out var enFallback))
        {
            return enFallback;
        }

        return errorCode;
    }

    public string GetLocalizedTitle(string errorTypeOrCode, CultureInfo? culture = null)
    {
        var targetCulture = culture ?? CultureInfo.CurrentUICulture;
        var isArabic = targetCulture.TwoLetterISOLanguageName.Equals("ar", StringComparison.OrdinalIgnoreCase);

        if (_titles.TryGetValue(errorTypeOrCode, out var titles))
        {
            return isArabic ? titles.Arabic : titles.English;
        }

        return isArabic ? "خطأ في المعالجة" : "Processing Error";
    }
}
