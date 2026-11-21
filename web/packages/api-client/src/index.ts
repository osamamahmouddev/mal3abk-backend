import createClient, { type Client, type ClientOptions } from "openapi-fetch";
import type { components, paths } from "./schema";

export type { components, paths };
export type Schemas = components["schemas"];
export type ApiClient = Client<paths>;

export interface ProblemDetails {
  type?: string | null;
  title?: string | null;
  status?: number | null;
  detail?: string | null;
  errors?: Record<string, string[]>;
}

export function createApiClient(options: ClientOptions): ApiClient {
  return createClient<paths>(options);
}

const ARABIC_PROBLEM_MESSAGES: Record<string, string> = {
  "Bff.ApiUnavailable": "تعذر الاتصال بالخادم، يُرجى التأكد من تشغيل السيرفر والمحاولة مرة أخرى.",
  "Bff.InvalidCsrfToken": "انتهت صلاحية جلسة الأمان، يُرجى إعادة تحميل الصفحة والمحاولة مرة أخرى.",
  "Bff.SessionExpired": "انتهت جلستك، يُرجى تسجيل الدخول مرة أخرى.",
  "Bff.NotSignedIn": "يجب تسجيل الدخول أولاً للمتابعة.",
  "Bff.CrossOriginRequest": "طلب غير مسموح من مصدر خارجي.",
  "Identity.StepUpFailed": "كود التحقق غير صحيح أو انتهت صلاحيته، يُرجى التأكد من الكود والمحاولة مجدداً.",
  "Identity.InvalidTotpCode": "كود التحقق (TOTP) غير صحيح، تأكد من الوقت في تطبيق التحقق.",
  "Identity.LoginThrottled": "محاولات كثيرة خاطئة، يُرجى الانتظار قليلاً والمحاولة مجدداً.",
  "Identity.OtpRateLimitExceeded": "تجاوزت الحد المسموح لإرسال الأكواد، انتظر دقيقة ثم حاول ثانية.",
  "Identity.LastActiveAdmin": "لا يمكن حذف أو تعطيل آخر مسؤول نشط في النظام.",
  "Identity.AccountSuspended": "هذا الحساب معطّل حالياً.",
  "Identity.TwoFactorRequired": "يلزم تفعيل التحقق بخطوتين لهذا الحساب.",
  "Identity.InvalidCredentials": "البريد الإلكتروني أو كلمة المرور غير صحيحة.",
  "Identity.UserLockedOut": "تم قفل الحساب مؤقتاً لكثرة المحاولات الخاطئة.",
  "Impersonation.NotAllowed": "لا يمكن الدخول بصفة هذا الحساب: الدخول للمساعدة مخصص للعملاء فقط (اللاعبين وأصحاب الملاعب)، ولا يُسمح بالدخول على حسابك الشخصي أو حسابات موظفي الإدارة أو الحسابات الموقوفة.",
  "Impersonation.ReasonRequired": "يجب كتابة سبب واضح لطلب الدخول بحساب العميل للمساعدة.",
  "Impersonation.InvalidCode": "كود الدخول غير صالح أو انتهت صلاحيته (صالح لـ ٦٠ ثانية فقط).",
  "Pricing.NoBasePrice": "لم يتم تحديد سعر أساسي لهذا الملعب بعد.",
  "Pricing.CurrencyMismatch": "كل قواعد الأسعار يجب أن تكون بنفس العملة.",
  "PriceRule.Overlap": "يوجد تعارض مع قاعدة سعر أخرى نشطة في نفس الوقت والميعاد.",
  "PriceRule.NotFound": "قاعدة السعر غير موجودة.",
  "PriceRule.TooMany": "وصلت للحد الأقصى لقواعد الأسعار.",
  "Venue.NotFound": "المكان غير موجود.",
  "Pitch.NotFound": "الملعب غير موجود.",
  "Featured.VenueNotLive": "لا يمكن تمييز هذا الملعب لأنه غير منشور حالياً. يجب أن يكون الملعب معتمداً ومنشوراً ليظهر للاعبين في المنصة.",
  "Featured.InvalidSchedule": "تاريخ الانتهاء يجب أن يكون بعد تاريخ البدء وفي المستقبل، وبحد أقصى ٩٠ يوماً.",
  "Featured.SlotFull": "وصلت المساحة الإعلانية المحددة للحد الأقصى من الملاعب المميزة في هذه الفترة (١٠ ملاعب كحد أقصى).",
  "Featured.NotFound": "العرض الترويجي للملعب غير موجود.",
  "Featured.Cancelled": "تم إلغاء هذا العرض الترويجي بالفعل.",
  "Featured.Invalid": "برجاء التأكد من صحة بيانات الجدولة وترتيب العرض.",
  "AdminUsers.ReviewAccountProtected": "ده حساب مراجعة App Store و Google Play: مينفعش يتوقف أو يتحذف، وإلا مراجعة التطبيق هتترفض.",
  "Trash.NotFound": "العنصر مش موجود أو اتمسح بالفعل.",
  "Trash.UnknownKind": "نوع العنصر ده مينفعش يتحذف من هنا.",
  "Trash.StaffAccount": "حسابات فريق المنصة بتتوقف من صفحة حسابات الفريق، مش بتتحذف من هنا.",
  "Trash.OwnsVenues": "المستخدم ده صاحب ملاعب: احذف ملاعبه الأول.",
  "Trash.HasUpcomingBookings": "فيه حجوزات جاية لسه شغالة: ألغيها الأول قبل الحذف.",
  "Trash.CannotRestore": "العنصر ده مبقاش ينفع يرجع.",
  "Trash.ConfirmationMismatch": "اكتب الاسم بالظبط زي ما هو عشان تأكد.",
  "Trash.ReasonRequired": "اكتب سبب الحذف (من ٥ لـ ٥٠٠ حرف).",
  "Crm.DuplicateInTrash": "فيه عميل بنفس الرقم في المحذوف مؤخراً: رجّعه بدل ما تضيفه تاني.",
  "PaymentMethod.InvalidAccountDetails": "بيانات التحويل مش مظبوطة للطريقة دي: المحفظة محتاجة رقم موبايل مصري، والتحويل البنكي محتاج IBAN أو رقم حساب.",
  "Platform.Maintenance": "المنصة في وضع الصيانة حالياً. نعمل على تحسين الخدمة وسنعود للعمل قريباً.",
};

/** Readable message for a failed call: the API puts its error code in `title` and the reason in `detail`. */
export function problemMessage(error: unknown, fallback = "حصل خطأ، حاول تاني"): string {
  if (error && typeof error === "object") {
    const problem = error as ProblemDetails;
    if (problem.title && ARABIC_PROBLEM_MESSAGES[problem.title]) {
      return ARABIC_PROBLEM_MESSAGES[problem.title]!;
    }
    if (problem.detail && (problem.detail.includes("has no base price") || problem.detail.includes("NoBasePrice"))) {
      return ARABIC_PROBLEM_MESSAGES["Pricing.NoBasePrice"]!;
    }
    const fieldErrors = problem.errors ? Object.values(problem.errors).flat() : [];
    return fieldErrors[0] ?? problem.detail ?? problem.title ?? fallback;
  }
  return fallback;
}
