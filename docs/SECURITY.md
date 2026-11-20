# SECURITY.md — دليل الأمان الكامل لمشروع ملعبك (Malabak)

> كل قسم فيه: **إيه هو؟** ← **ليه محتاجينه؟** ← **إزاي نعمله (ASP.NET)** ← **أخطاء شائعة**.
> الكود اللي هنا أمثلة توضيحية، مش نسخ-لصق جاهز للإنتاج. راجع كل حاجة مع الـ tests وقبل النشر اعمل مراجعة أمنية.
> الملف ده بيكمّل `rule.md` (القسم 7). لو فيه تعارض، الأشد صرامة هو اللي يتطبق.

---

## فهرس

0. إزاي تفكر في الأمان (طبقات + مبادئ)
1. تهديدات ملعبك (Threat Model)
2. أساسيات: Hash / Encrypt / Sign / Encode
3. **التوكن (Tokens) — أهم قسم**
4. الباسورد والـ OTP
5. **تغيير رقم التليفون خطوة بخطوة**
6. الويب: Cookies و CSRF و CORS
7. **Rate Limiting**
8. **WebSocket / SignalR وقت الحجز**
9. **التشفير (Encryption)** — في الطريق، على الديسك، على مستوى الحقل، المفاتيح
10. الصلاحيات (Authorization) و IDOR
11. إساءة استخدام الحجز (Slot hoarding / fraud)
12. رفع الملفات (إيصالات الدفع)
13. **Cloudflare**
14. أمان تطبيق الموبايل
15. Headers، Input، Secrets، Logging، Audit
16. البنية التحتية و DevSecOps
17. الحوادث والامتثال القانوني
18. ترتيب التنفيذ + Checklist
19. قاموس مصطلحات

---

## 0. إزاي تفكر في الأمان

### 0.1 مفيش حاجة اسمها "آمن 100%"
الهدف إنك تخلي الهجوم **غالي ومعقد وبيتكشف بسرعة**. بنعمل ده بطبقات (Defense in Depth):

```
المستخدم ──► Cloudflare (WAF, DDoS, Bot) ──► API (Rate limit, Auth, Validation)
        ──► Domain rules ──► قاعدة البيانات (constraints, تشفير) ──► Backups + Logs + Alerts
```

لو طبقة وقعت، اللي بعدها تمسك.

### 0.2 المبادئ اللي كل قرار بيتبنى عليها
| المبدأ | معناه بالبلدي | مثال في ملعبك |
|---|---|---|
| **Never trust the client** | أي حاجة جاية من الأب أو المتصفح ممكن تكون مزورة | السعر والتوفر والـ role والـ id يتحسبوا في السيرفر |
| **Deny by default** | أي endpoint مقفول لحد ما نفتحه | `FallbackPolicy` يطلب تسجيل دخول |
| **Least privilege** | كل واحد ياخد أقل صلاحية تكفيه | موظف ملعب يشوف حجوزات ملعبه بس |
| **Defense in depth** | أكتر من طبقة حماية | الـ UI يخفي، والـ API يمنع، والـ DB constraint يرفض |
| **Fail secure** | لو حصل خطأ، اقفل ومتفتحش | لو Redis وقع، رفض الطلب الحساس بدل ما تسمح |
| **Secure by default** | الإعداد الافتراضي يكون الآمن | Cookies بـ HttpOnly وSecure تلقائياً |
| **Minimize data** | متجمعش بيانات مش محتاجها | مفيش رقم قومي ولا موقع دقيق |
| **Auditability** | كل فعل حساس يتسجل | قبول/رفض إيصال دفع |
| **Zero trust** | حتى الشبكة الداخلية مش موثوقة | TLS بين السيرفر والداتابيز |

---

## 1. تهديدات ملعبك (Threat Model)

قبل ما نحمي، لازم نعرف بنحمي من مين. (STRIDE مبسّط)

| # | التهديد | مين بيعمله | الأثر | الحماية (القسم) |
|---|---|---|---|---|
| T1 | سرقة حساب عن طريق OTP (تخمين/اعتراض/SIM swap) | مخترق | وصول لحجوزات وبيانات | 4, 5, 7 |
| T2 | **SMS Pumping / Toll fraud**: بوتات تبعت آلاف OTP لأرقام مدفوعة وتكلّفك فلوس | محتال | فاتورة SMS ضخمة | 4, 7, 13 |
| T3 | سرقة الـ token من الموبايل أو المتصفح | Malware / XSS | انتحال شخصية | 3, 14 |
| T4 | **IDOR/BOLA**: تغيّر `bookingId` في الطلب وتشوف حجز حد تاني | أي مستخدم | تسريب بيانات | 10 |
| T5 | حجز نفس الملعب مرتين (race) | مستخدمين/بوتات | مشاكل تشغيل | rule.md §5 + 11 |
| T6 | **Slot hoarding**: بوت يحجز كل المواعيد ويسيبها تنتهي | منافس/مخرّب | ملعب يبان مليان وهو فاضي | 11, 7 |
| T7 | إيصالات تحويل مزورة | محتال | خسارة فلوس للملعب | 11, 12 |
| T8 | CSRF على داشبورد الملاك | موقع خبيث | تنفيذ أفعال باسم الملاك | 6 |
| T9 | XSS (سكريبت يتزرع في اسم فريق/ملاحظة) | مستخدم خبيث | سرقة جلسات | 6, 15 |
| T10 | اختراق حساب أدمن | مهاجم مستهدف | كارثة كاملة | 3.9, 13 |
| T11 | Scraping لقاعدة الملاعب والأسعار | منافس | خسارة تنافسية | 7, 13 |
| T12 | DDoS | أي حد | وقف الخدمة | 13 |
| T13 | تسريب أسرار (مفاتيح/كلمات سر) في Git | مطور بالغلط | اختراق كامل | 16 |
| T14 | هيجان على الـ WebSocket (اتصالات كتير/رسائل ضخمة/خطف) | مهاجم | وقف الخدمة/تسريب | 8 |
| T15 | تسريب بيانات من الـ backups أو اللوجز | داخلي/خارجي | تسريب PII | 9, 15 |
| T16 | تغيير رقم التليفون لسرقة الحساب | مهاجم بجلسة مسروقة | سرقة دائمة | 5 |
| T17 | موظف ملعب بيستغل صلاحياته | داخلي | تلاعب | 10, 15 |

---

## 2. أساسيات: Hash / Encrypt / Sign / Encode

دي أكتر حاجة بتتلخبط. فهمها مرة واحدة:

| العملية | بترجّع؟ | بتحتاج مفتاح؟ | بتستخدم فين؟ | مثال |
|---|---|---|---|---|
| **Encoding** (Base64, URL-encode) | بترجع للأصل بسهولة | لأ | تغيير الشكل بس. **مش حماية** | Base64 للـ JWT |
| **Hashing** | **لأ، اتجاه واحد** | لأ (أو سر مع HMAC) | باسورد، refresh token، OTP، بصمة ملف | Argon2id, SHA-256 |
| **Encryption** (symmetric) | أيوه بالمفتاح | مفتاح واحد | بيانات لازم ترجّعها (رقم حساب بنكي) | AES-256-GCM |
| **Encryption** (asymmetric) | أيوه بالمفتاح الخاص | زوج مفاتيح | تبادل مفاتيح، TLS | RSA/ECDH |
| **Signing** (توقيع) | بيثبت إن الداتا مش اتغيرت ومن صاحبها | مفتاح خاص للتوقيع، عام للتحقق | JWT، webhook | ES256, HMAC |
| **MAC / HMAC** | وسم تحقق | سر مشترك | تحقق webhook، OTP hash | HMAC-SHA256 |

قواعد ذهبية:
- **الباسورد يتعمله Hash مش Encryption** (مفيش سبب ترجعه).
- Hash عادي (SHA-256) **ما ينفعش للباسورد** لأنه سريع جداً؛ لازم Argon2id/bcrypt/PBKDF2 (بطيئين عمداً + salt).
- **Base64 مش تشفير**. أي حد يفكّه.
- **ممنوع:** MD5، SHA-1 للأمان، DES/3DES، AES-ECB، تشفير من تأليفك، مفتاح مكتوب في الكود.
- **المفتاح هو السر** مش الخوارزمية. الخوارزمية معروفة للكل.

---

## 3. التوكن (Tokens) — أهم قسم

### 3.1 ليه توكن أصلاً؟
السيرفر الـ REST بدون حالة (stateless). كل طلب لازم يثبت "أنا مين". بدل ما نبعت الباسورد في كل طلب، بعد تسجيل الدخول السيرفر يدّيك **تذكرة** (token) تبعتها مع كل طلب.

### 3.2 التصميم المعتمد: Access Token + Refresh Token

| | Access Token | Refresh Token |
|---|---|---|
| الشكل | **JWT** موقّع | **نص عشوائي طويل (opaque)** مش JWT |
| العمر | **10–15 دقيقة** | **30 يوم** (sliding) بحد أقصى مطلق 90 يوم |
| بيتبعت فين | كل طلب: `Authorization: Bearer ...` | **endpoint واحد بس**: `/auth/refresh` |
| بيتخزن في السيرفر؟ | لأ (stateless) | **أيوه كـ Hash في الداتابيز** |
| لو اتسرق | ضرره محدود بوقته القصير | خطير، علشان كده بنعمل Rotation وكشف إعادة الاستخدام |
| بيتلغي؟ | بينتهي لوحده (+ فحص الجلسة للحالات الحرجة) | بيتلغي فوراً من الداتابيز |

**ليه الـ Access قصير؟** لأن JWT مينفعش "نلغيه" بسهولة بعد ما يتصدر. لو اتسرق، الضرر ينتهي بعد دقائق.
**ليه الـ Refresh مش JWT؟** لأنه لازم يتلغى فوراً، فلازم نخزّنه ونتحكم فيه.

### 3.3 محتوى الـ Access Token (JWT)
JWT = ثلاث أجزاء `header.payload.signature`. الـ payload **مقروء لأي حد** (Base64) — **متحطش فيه أسرار أو بيانات شخصية**.

```json
{
  "iss": "https://api.malabak.app",
  "aud": "malabak-api",
  "sub": "0192f3a1-...-user-guid",
  "sid": "0192f3a1-...-session-guid",
  "jti": "unique-token-id",
  "iat": 1761350000, "nbf": 1761350000, "exp": 1761350900,
  "role": ["Player"],
  "amr": ["otp"]
}
```
- `sub` = user id، `sid` = رقم الجلسة (علشان نلغي جلسة بعينها)، `jti` = رقم التوكن.
- **الصلاحيات التفصيلية (مين موظف في أي ملعب) متتحطش في التوكن**؛ تتفحص من الداتابيز/الكاش في كل طلب (علشان لو الصلاحية اتغيرت تتطبق فوراً).
- `amr` = طريقة المصادقة (otp/pwd/mfa) مفيد للـ step-up.

### 3.4 التوقيع: ES256 بدل HS256
- **HS256** = سر واحد مشترك (HMAC). بسيط، بس أي خدمة تتحقق لازم تعرف السر ← ممكن تزوّر.
- **ES256** (ECDSA P-256) = مفتاح خاص للتوقيع (في السيرفر بس)، ومفتاح عام للتحقق ← ينفع تشاركه بأمان.
- **القرار:** `ES256` + `kid` في الـ header علشان نعمل **تدوير مفاتيح (key rotation)** بدون وقف الخدمة (نخلي المفتاح القديم للتحقق فقط لحد ما التوكنات القديمة تنتهي).
- **ممنوع:** `alg: none`، وقبول خوارزميات متعددة. اقفل القائمة على خوارزمية واحدة.
- المفتاح الخاص في Secret Manager (Key Vault/AWS Secrets)، مش في الريبو.

### 3.5 التحقق في ASP.NET (إعداد صارم)

```csharp
builder.Services
  .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
  .AddJwtBearer(o =>
  {
      o.MapInboundClaims = false;                 // خلي أسماء الـ claims زي ما هي (sub, sid...)
      o.RequireHttpsMetadata = true;
      o.TokenValidationParameters = new TokenValidationParameters
      {
          ValidateIssuer = true,  ValidIssuer = cfg.Issuer,
          ValidateAudience = true, ValidAudience = cfg.Audience,
          ValidateLifetime = true,
          RequireExpirationTime = true,
          RequireSignedTokens = true,
          ValidateIssuerSigningKey = true,
          ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256 }, // خوارزمية واحدة بس
          ClockSkew = TimeSpan.FromSeconds(30),                       // الافتراضي 5 دقايق = كتير
          IssuerSigningKeyResolver = (token, secToken, kid, p) => keyStore.GetPublicKeys(kid) // دعم الـ rotation
      };
      o.Events = new JwtBearerEvents
      {
          OnTokenValidated = async ctx =>
          {
              // فحص إن الجلسة لسه شغالة والمستخدم مش محظور (من Redis/كاش قصير)
              var sid = ctx.Principal!.FindFirst("sid")?.Value;
              var sessions = ctx.HttpContext.RequestServices.GetRequiredService<ISessionStore>();
              if (!await sessions.IsActiveAsync(sid, ctx.HttpContext.RequestAborted))
                  ctx.Fail("session_revoked");
          }
      };
  });
```

### 3.6 دورة حياة الـ Refresh Token (Rotation + Reuse Detection)

**Rotation:** كل مرة تستخدم refresh token، السيرفر يدّيك **جديد** ويلغي القديم.
**Reuse detection:** لو حد استخدم refresh token **قديم اتلغى** ← معناه إن حد تاني سرقه ← **نلغي العيلة كلها (كل الجلسة)** وننبّه اليوزر.

جدول الداتابيز `refresh_tokens`:

| العمود | وصفه |
|---|---|
| `id` | GUID |
| `token_hash` | SHA-256 للتوكن (مش التوكن نفسه) — unique index |
| `family_id` | بيربط كل التوكنات الجاية من نفس تسجيل الدخول |
| `session_id` | الجلسة (جهاز) |
| `user_id` | المستخدم |
| `device_id`, `device_name`, `ip`, `user_agent` | للعرض في "الأجهزة المسجلة" |
| `created_at`, `expires_at`, `absolute_expires_at` | |
| `used_at` | اتستخدم امتى |
| `replaced_by_id` | التوكن اللي حل محله |
| `revoked_at`, `revoked_reason` | `logout`, `reuse_detected`, `password_changed`, `admin` |

خطوات `/auth/refresh`:

```csharp
public async Task<Result<TokenPair>> RefreshAsync(string rawToken, DeviceInfo device, CancellationToken ct)
{
    var hash = Sha256(rawToken);
    var rt = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);

    if (rt is null) return Error.Unauthorized("invalid_refresh");             // مجهول

    // 1) إعادة استخدام توكن اتلغى = احتمال سرقة
    if (rt.UsedAt is not null || rt.RevokedAt is not null)
    {
        // سماح بسيط (10 ثواني) لحالة إن الأب بعت الطلب مرتين بسبب النت — بنرجّع نفس النتيجة بدل ما نقتل الجلسة
        if (rt.UsedAt > clock.UtcNow.AddSeconds(-10) && rt.RevokedReason is null)
            return await graceReplay.GetOrFailAsync(rt, ct);

        await RevokeFamilyAsync(rt.FamilyId, "reuse_detected", ct);
        await audit.WriteAsync("auth.refresh_reuse_detected", rt.UserId, ct);
        await notifier.SecurityAlertAsync(rt.UserId, "تم اكتشاف استخدام مشبوه لجلستك وتم تسجيل خروجك", ct);
        return Error.Unauthorized("session_revoked");
    }

    if (rt.ExpiresAt < clock.UtcNow || rt.AbsoluteExpiresAt < clock.UtcNow) return Error.Unauthorized("expired");

    // 2) فحص إن المستخدم لسه مسموحله
    var user = await db.Users.FindAsync(rt.UserId, ct);
    if (user is null || user.IsSuspended) return Error.Forbidden("account_disabled");

    // 3) Rotation في transaction
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    rt.UsedAt = clock.UtcNow;
    var next = NewRefreshToken(rt, device);          // نفس family_id و session_id
    rt.ReplacedById = next.Entity.Id;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    return new TokenPair(IssueAccessToken(user, rt.SessionId), next.RawValue, ...);
}
```

مهم:
- **التوكن الخام بيتولّد بـ `RandomNumberGenerator.GetBytes(32)`** (256 بت) ويتبعت Base64Url. **مش GUID ومش Random().**
- **التخزين:** Hash بس. لو الداتابيز اتسربت، التوكنات مش هتنفع.
- **Concurrency:** الأب ممكن يبعت refresh من أكتر من مكان في نفس الوقت (مثلاً طلبين فشلوا 401 مع بعض). **في الأب اعمل single-flight**: طلب refresh واحد، وباقي الطلبات تستنى نتيجته. وفي السيرفر الـ grace window فوق + unique constraint يمنعوا الانهيار.
- **Logout:** يلغي الـ refresh token والـ session. **Logout-all:** يلغي كل الجلسات (`family_id`s) للمستخدم.
- **تغيير الباسورد/رقم التليفون:** يلغي كل الجلسات الأخرى (غير الحالية).
- شاشة **"الأجهزة المسجلة"** في البروفايل: يشوف اليوزر جلساته ويقفلها.

### 3.7 تخزين التوكن عند العميل

| العميل | المكان الصح | المكان الغلط |
|---|---|---|
| **أندرويد** | `EncryptedSharedPreferences` / Keystore | `SharedPreferences` عادي، ملف، لوج |
| **iOS** | **Keychain** (`kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`) | `UserDefaults` |
| **Flutter** | `flutter_secure_storage` | `shared_preferences` |
| **ويب (Next.js)** | **Cookie** بـ `HttpOnly; Secure; SameSite=Lax` يمسكها سيرفر Next.js (BFF) | `localStorage`/`sessionStorage` (XSS بيسرقها) |

- الـ Access Token ممكن يفضل في الذاكرة فقط في الأب.
- `android:allowBackup="false"` علشان التوكن ما يتحملش في Backup. (التفاصيل في القسم 14)
- ما تكتبش التوكن في أي Log أو Crash report أو Analytics.

### 3.8 أنواع توكنات تانية في المشروع

| التوكن | الغرض | العمر | ملاحظات |
|---|---|---|---|
| **Access** | API العادي | 10-15 د | JWT, aud=`malabak-api` |
| **Refresh** | تجديد | 30 يوم | opaque + hashed |
| **Step-up token** | عمليات حساسة (تغيير رقم، حذف حساب) | 5 د | بعد OTP/باسورد، scope محدد (`scope=change_phone`) |
| **WS ticket** | فتح WebSocket | 60 ث، **استخدام مرة واحدة** | القسم 8 |
| **Upload URL** | رابط تحميل إيصال | 5 د | Signed URL |
| **Invitation token** | دعوة موظف/لاعب لفريق | 48 س، مرة واحدة | مخزن Hash، مربوط بإيميل/رقم |
| **Public share/payment token** | رابط عام (لو احتجنا) | قصير | عشوائي 256 بت، مش ID متسلسل |
| **Impersonation code** | الدعم يدخل بحساب عميل | 60 ث، مرة واحدة | في Redis، ويتسجل في audit، وفيه `act` claim |

### 3.9 الأدمن: أقوى من اللاعبين
- **Issuer/Audience مختلفين** (`aud=malabak-admin`) و endpoints منفصلة `/admin/...` ← توكن لاعب عمره ما يشتغل على أدمن.
- Access **5-10 دقايق**، Refresh أقصر (مثلاً 8 ساعات) وبدون "تذكرني".
- **2FA اختياري** (TOTP/Passkey)، مش SMS. قرار صاحب المشروع: الأدمن يفعّله أو يلغيه من الإعدادات، والافتراضي مقفول. لو اتفعّل، ميتلغيش غير من جلسة مفتوحة وبكود حالي (مش بالباسورد لوحده)، والعمليات الخطيرة بتطلب الكود. لو مقفول، العمليات الخطيرة بتطلب الباسورد. **التوصية: فعّله.**
- حماية بـ **Cloudflare Access (Zero Trust)** + IP allowlist للداشبورد.
- كل فعل أدمن في الـ **audit log** (قبل/بعد).
- العمليات الخطيرة (حذف ملعب، reset) تطلب **Step-up** وكتابة اسم العنصر للتأكيد.

### 3.10 أخطاء شائعة في التوكن
- ❌ تحط `exp` طويل (أيام) في الـ Access.
- ❌ تخزن JWT في `localStorage`.
- ❌ تثق في `role` جاي من التوكن لعمليات حساسة بدون فحص الداتابيز.
- ❌ ما تعملش Rotation للـ refresh.
- ❌ توكن في الـ URL (بيتسجل في اللوجز والـ referrer). استثناء مؤقت: WS ticket قصير جداً (القسم 8).
- ❌ نفس المفتاح للـ dev والـ prod.
- ❌ مفيش طريقة تلغي جلسة.

---

## 4. الباسورد والـ OTP

### 4.1 مين بيستخدم إيه؟
- **اللاعب:** رقم موبايل + **OTP** (بدون باسورد). أسهل وأنسب للسوق.
- **صاحب الملعب/الموظف:** إيميل + باسورد (+ OTP/2FA اختياري ثم إجباري للمالك).
- **الأدمن:** إيميل + باسورد + **2FA اختياري** (مقفول افتراضياً، شوف §3.9).

### 4.2 الباسورد
- **Hashing:** الأفضل **Argon2id** (OWASP: ذاكرة ≥ 19 MiB، t=2، p=1). في ASP.NET Identity الافتراضي PBKDF2 مقبول بشرط ترفع الـ iterations:
  ```csharp
  builder.Services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000); // قيمة دنيا، ارفعها حسب الأداء
  ```
  (لو عايز Argon2id تعمل `IPasswordHasher<User>` مخصص بمكتبة موثوقة.)
- **السياسة:** طول ≥ 10 (الطول أهم من التعقيد)، ما تجبرش رموز غريبة، **افحص الباسورد ضد قايمة المسربات** (HIBP k-anonymity API)، امنع الباسوردات الشائعة.
- **Pepper اختياري:** سر إضافي (في Key Vault) يتضاف قبل الـ hash.
- **Reset:** توكن عشوائي مرة واحدة، صلاحية 30 د، مخزن Hash، والرد دايماً "لو الإيميل مسجل هتوصلك رسالة" (**non-enumerable**).
- **Brute force:** rate limit بالـ IP + الحساب (القسم 7)، تأخير تدريجي، CAPTCHA بعد 5 فشل. **ما تعملش lockout دائم** (بيخلي حد يقفل حساب ضحية = DoS). استخدم تأخير مؤقت.

### 4.3 الـ OTP (الأخطر ماليًا وأمنيًا)

**خصائص الـ OTP الآمن:**
| الخاصية | القيمة | السبب |
|---|---|---|
| الطول | 6 أرقام | توازن بين الأمان وسهولة الإدخال |
| التوليد | `RandomNumberGenerator.GetInt32(0, 1_000_000)` | CSPRNG مش `Random` |
| التخزين | **HMAC-SHA256(secret, code + identifier + purpose)** | لو الداتابيز اتسربت ما حدش يعرف الأكواد |
| الصلاحية | 5 دقايق | يقلل نافذة الهجوم |
| المحاولات | 5 محاولات فقط لكل كود | منع التخمين (1,000,000 احتمال) |
| الاستخدام | مرة واحدة | ويتحذف بعد النجاح |
| المقارنة | `CryptographicOperations.FixedTimeEquals` | منع timing attack |
| الربط | بـ `purpose` (login / change_phone / delete_account) وبالـ identifier | كود login ما ينفعش لتغيير الرقم |
| Resend | cooldown 60 ث، وحد أقصى 3 في الساعة للرقم | منع الإزعاج والتكلفة |
| الرد | **دايماً نفس الرد** سواء الرقم موجود ولا لأ | منع تخمين الأرقام المسجلة |

**SMS Pumping (T2) — دي بتوجع فعلاً:**
بوتات بتطلب OTP لأرقام دولية مدفوعة، وإنت اللي بتدفع. الحماية:
1. **Allowlist للدول**: مصر (+20) فقط في البداية.
2. **Cloudflare Turnstile** (CAPTCHA غير مزعج) قبل `/otp/send`.
3. **Rate limits متعددة**: لكل رقم + لكل IP + لكل device-id + لكل ASN + **سقف يومي عام** مع تنبيه.
4. **Device attestation** (Play Integrity / App Attest) للأب قبل إرسال OTP.
5. مراقبة: معدل (OTP مرسل ÷ OTP متحقق منه). لو نسبة النجاح انخفضت فجأة = هجوم.
6. مزود SMS فيه حماية fraud، وسقف مالي شهري.
7. فكر في **WhatsApp OTP** كقناة بديلة (أرخص وأصعب في الـ pumping).

**SIM-swap / تدوير الأرقام:** الأرقام ممكن تتنقل لشخص تاني. لذلك:
- ربط الجلسة بالجهاز، وإشعار عند تسجيل دخول من جهاز جديد.
- حسابات الملاك: **إيميل + باسورد + 2FA** وما تعتمدش على الرقم لوحده.

### 4.4 تسجيل دخول اللاعب (Flow)
```
1) POST /auth/otp/send    {phone, purpose:"login", deviceId, turnstileToken}  → 200 دايماً
2) POST /auth/otp/verify  {phone, code, deviceId}                                → access + refresh
3) أول مرة؟ → onboarding (اسم، منطقة)
```

---

## 5. تغيير رقم التليفون — خطوة بخطوة

### 5.1 ليه ده أخطر flow في التطبيق؟
الرقم هو **هويتك**. لو مهاجم معاه جلسة مسروقة وغيّر الرقم لرقمه، **سرق الحساب للأبد** (حتى لو صاحب الحساب غيّر الباسورد). علشان كده التغيير لازم يثبت **ملكية الرقمين** وبشروط.

### 5.2 المبادئ
1. **المستخدم لازم يكون مسجل دخول + مصادقة حديثة (Recent auth/Step-up).**
2. إثبات ملكية **الرقم القديم** (OTP عليه) — يثبت إن اللي بيطلب هو صاحب الحساب مش بس صاحب الجلسة.
3. إثبات ملكية **الرقم الجديد** (OTP عليه).
4. **إشعار فوري** على القديم (والإيميل لو موجود) إن الرقم اتغير.
5. **إلغاء كل الجلسات الأخرى** بعد التغيير.
6. **Cooldown** بعد التغيير (مثلاً 7 أيام لا تغيير تاني) + حد أقصى للمحاولات.
7. كل ده في **audit log**.
8. **الرقم الجديد مش بيتطبق إلا بعد التحقق الكامل** (pending change، مش تعديل مباشر).

### 5.3 الحالات (State machine)

```
(none) ──start──► PendingOldVerify ──oldOtp ok──► PendingNewVerify ──newOtp ok──► Completed
   │                    │                              │
   └──────────── Expired / Cancelled / TooManyAttempts ─┴──► Failed
```

جدول `phone_change_requests`:
| العمود | |
|---|---|
| `id`, `user_id` | |
| `old_phone_hash`, `new_phone` (مشفّر), `new_phone_hash` | الـ hash للبحث |
| `status` | `PendingOldVerify` / `PendingNewVerify` / `Completed` / `Expired` / `Cancelled` / `Failed` |
| `old_otp_hash`, `new_otp_hash`, `attempts_old`, `attempts_new` | |
| `created_at`, `expires_at` (15 دقيقة كلها) | |
| `ip`, `device_id`, `session_id` | |

### 5.4 الـ Endpoints

```
POST /api/v1/me/phone-change/start      {newPhone}                 (يتطلب Step-up صالح)
POST /api/v1/me/phone-change/verify-old {requestId, code}
POST /api/v1/me/phone-change/verify-new {requestId, code}
POST /api/v1/me/phone-change/cancel     {requestId}
```

### 5.5 الخطوات بالتفصيل

**الخطوة 0 — Step-up (إعادة تأكيد الهوية):**
- لو اليوزر مش عمل مصادقة في آخر 5 دقايق: اطلب OTP على رقمه الحالي (أو الباسورد لحسابات الملاك) → السيرفر يطلّع **step-up token** (`scope=change_phone`, عمره 5 د).
- ليه؟ لو الجلسة اتسرقت، السارق ما يقدرش يبدأ من غير ما يكون معاه الموبايل نفسه.

**الخطوة 1 — `start`:**
1. تحقق من Step-up token (scope صحيح، عمره صالح، نفس المستخدم/الجلسة).
2. تحقق من صيغة الرقم (E.164، مصري `+20 1x xxxx xxxx`) باستخدام مكتبة `libphonenumber`.
3. **Rate limit:** 3 طلبات تغيير/يوم للمستخدم، و5/ساعة لكل IP.
4. تحقق إن آخر تغيير كان من أكتر من 7 أيام.
5. لو الرقم الجديد = الحالي → خطأ.
6. لو الرقم الجديد مسجل على حساب تاني → **ما تفضحش**. ارجع رسالة عامة "تعذر استخدام هذا الرقم" (بدون تحديد السبب) علشان ما يبقاش وسيلة لتخمين الأرقام.
7. اعمل `PendingOldVerify`، ابعت **OTP على الرقم القديم** (purpose=`change_phone_old`).
8. ابعت إشعار "تم طلب تغيير رقمك" (إيميل / push).

**الخطوة 2 — `verify-old`:**
- تحقق من الكود (قواعد 4.3). بعد النجاح: الحالة `PendingNewVerify` وابعت **OTP على الرقم الجديد** (purpose=`change_phone_new`).

**الخطوة 3 — `verify-new`:** (كله في **transaction واحدة**)
```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);

// 1) تأكد إن الرقم لسه مش مستخدم (الـ unique index هو الحارس الأخير ضد الـ race)
user.ChangePhone(newPhone, clock.UtcNow);                  // domain method
await userManager.UpdateSecurityStampAsync(user);          // يبطّل أي cookies/توكنات قديمة تعتمد عليه
await sessions.RevokeAllExceptAsync(user.Id, currentSessionId, "phone_changed", ct);
req.Complete();
await audit.WriteAsync("user.phone_changed", user.Id, new { oldHash, newHash }, ct);
await outbox.EnqueueAsync(new PhoneChangedNotification(user.Id, oldPhone), ct); // إشعار للرقم القديم

await db.SaveChangesAsync(ct);          // لو الرقم اتاخد في نفس اللحظة → DbUpdateException (unique) → رجّع خطأ عام
await tx.CommitAsync(ct);
```
- أرسل **SMS للرقم القديم**: "تم تغيير رقم حسابك في ملعبك. لو مش إنت كلّم الدعم" (فيه رابط تراجع لمدة 24 ساعة اختياري — **recovery link**).

### 5.6 الحالات الاستثنائية
| الحالة | التعامل |
|---|---|
| **فقدت الرقم القديم** (مش قادر أعمل verify-old) | مسار دعم فني: تحقق بشري (بيانات الحجوزات، آخر حجز، صور)، وبفترة انتظار 48–72 ساعة + إشعارات، **ومش فوري أبداً**. |
| محاولات كتير غلط | `Failed` + تأخير + تنبيه للمستخدم |
| الطلب انتهى | `Expired`، يبدأ من أول |
| تغيير أثناء حجز Pending | الحجز يفضل زي ما هو (مربوط بـ user id مش بالرقم) |
| حسابات الملاك | يتطلب كمان **باسورد/2FA** + إشعار إيميل |
| الأدمن يغيّر رقم لحد | فقط عن طريق إجراء مسجّل وبتصريح عالي + Step-up + audit |

### 5.7 أخطاء شائعة
- ❌ تعديل الرقم مباشرة قبل التحقق.
- ❌ التحقق من الرقم الجديد فقط (السارق يحوّل الحساب لرقمه).
- ❌ ما تلغيش الجلسات الأخرى.
- ❌ رسالة خطأ بتقول "الرقم مسجل لحد تاني".
- ❌ مفيش cooldown ولا rate limit (يتحول لأداة SMS pumping).

---

## 6. الويب: Cookies و CSRF و CORS

### 6.1 CSRF — إيه هو؟
المتصفح **بيبعت الـ cookies تلقائياً** مع أي طلب لنفس الدومين. لو موقع خبيث خلّاك تفتح صفحة فيها فورم بتبعت لـ `malabak.app/api/bookings/cancel` وإنت مسجل دخول، المتصفح هيبعت الـ cookie ← الطلب ينفّذ باسمك.

**مهم:** CSRF بيخص **الـ Cookies فقط**. تطبيق الموبايل بيبعت `Authorization: Bearer` يدوياً ← **مش معرّض لـ CSRF**. الداشبورد (Next.js) هو اللي محتاج حماية.

### 6.2 الحماية (كلها مع بعض)
1. **Cookie flags:** `HttpOnly; Secure; SameSite=Lax` (الأدمن `Strict`) وبادئة `__Host-` (تمنع الـ subdomain attacks): `__Host-session`.
2. **تحقق من الـ `Origin` / `Sec-Fetch-Site`** في كل طلب يغيّر حالة (POST/PUT/PATCH/DELETE): لازم يكون من دومينّا.
3. **CSRF Token** (Synchronizer/Double-submit موقّع): توكن عشوائي مربوط بالجلسة يتبعت في هيدر `X-CSRF-Token` ويتقارن في السيرفر.
4. **ما تغيّرش حالة بـ GET** أبداً.
5. **CORS** ضيق (6.4).

### 6.3 معمارية الويب (BFF)
```
المتصفح ──(cookie HttpOnly)──► Next.js server (BFF) ──(Bearer access token)──► ASP.NET API
```
- التوكنات **مش بتوصل للمتصفح أبداً**. Next.js بيمسك الـ refresh/access في الـ session (مشفّرة) ويكلم الـ API.
- CSRF يتطبق على الحد بين المتصفح و Next.js.
- في Next.js: Server Actions عندها حماية Origin مدمجة، لكن **Route Handlers لازم تتحمى يدوياً**.
- الـ session cookie: مشفّرة ومؤقتة (iron-session / Auth.js)، ومفتاحها في env/secret.

لو في أي مكان في ASP.NET بتستخدم cookie auth (مثلاً Hangfire dashboard أو Swagger الداخلي):
```csharp
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-Token"; o.Cookie.Name = "__Host-csrf";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Strict; });
```

### 6.4 CORS
- **Allowlist صريحة** للدومينات: `https://venue.malabak.app`, `https://admin.malabak.app`. **ممنوع `*`** مع credentials.
- موبايل أب مش بيحتاج CORS (CORS خاصية متصفح).
- حدد الـ methods والـ headers المسموحة.
```csharp
policy.WithOrigins(allowedOrigins).WithMethods("GET","POST","PUT","PATCH","DELETE")
      .WithHeaders("Authorization","Content-Type","X-CSRF-Token","Idempotency-Key","Accept-Language")
      .AllowCredentials().SetPreflightMaxAge(TimeSpan.FromHours(1));
```

### 6.5 XSS (لأنه بيكسر كل ما فات)
- React بيعمل escape تلقائي. **ممنوع `dangerouslySetInnerHTML`** على محتوى مستخدم (أسماء فرق، وصف ملعب، ملاحظات).
- **CSP (Content-Security-Policy)** صارمة بـ nonce (القسم 15).
- Sanitize على السيرفر لأي HTML (لو اضطرينا).
- الـ cookies HttpOnly علشان XSS ما يسرقش التوكن.

---

## 7. Rate Limiting

### 7.1 إيه هو وليه؟
تحديد عدد الطلبات المسموحة في فترة. بيحمي من: تخمين OTP/باسورد، SMS pumping، scraping، slot hoarding، DDoS بسيط، وكمان بيحمي فلوسك (تكلفة SMS/Cloud).

### 7.2 الطبقات (كلها)
| الطبقة | بتحمي من | الأداة |
|---|---|---|
| **1. Cloudflare** (الحافة) | DDoS، bots، IP سيئة، حجم ضخم | WAF Rate Limiting Rules + Bot Fight + Turnstile |
| **2. API Gateway/Middleware** | إساءة استخدام لكل endpoint | ASP.NET `RateLimiter` + Redis |
| **3. Business rules** | سلوك خبيث مشروع | حدود دومين (max pending bookings...) |
| **4. DB** | سباق ومحاولات تزامن | constraints |

### 7.3 الخوارزميات (إيه الفرق؟)
| الخوارزمية | الفكرة | تنفع لـ |
|---|---|---|
| **Fixed window** | X طلب كل دقيقة، العداد بيتصفّر | بسيط، بس ممكن ضربتين عند الحدود |
| **Sliding window** | نافذة متحركة | OTP، Login (دقيق) |
| **Token bucket** | سطل بيتملي بمعدل ثابت، فيسمح بـ burst | API عام (Explore) |
| **Concurrency** | عدد الطلبات المتزامنة | رفع الملفات، reports ثقيلة |

### 7.4 المفتاح (على إيه نحدد؟)
- **IP**: لأي endpoint عام. (تحذير: مستخدمين موبايل ورا CGNAT بيشاركوا IP؛ ما تخليش الحد ضيق جداً.)
- **User ID**: للمسارات المسجلة.
- **Phone/Email المستهدف**: للـ OTP والـ login (يحمي الضحية).
- **Device ID**: للأب.
- **تركيبة:** IP + هوية مستهدفة.

### 7.5 الحدود المقترحة (نقطة بداية، تتظبط بالـ metrics)

| Endpoint | الحد | المفتاح | عند التجاوز |
|---|---|---|---|
| `POST /auth/otp/send` | 1/60 ث، 3/10 د، 5/ساعة، 10/يوم للرقم | phone + ip + device | 429 + Turnstile |
| `POST /auth/otp/verify` | 5 محاولات/كود، 10/15 د | phone + ip | 429 + تأخير |
| `POST /auth/login` (ملاك) | 5/15 د للحساب، 20/15 د للـ IP | email + ip | 429 + CAPTCHA |
| `POST /auth/refresh` | 30/د | user/session | 429 |
| `POST /auth/forgot-password` | 3/ساعة | email + ip | 200 دايماً (بدون كشف) |
| `POST /me/phone-change/*` | 3/يوم | user | 429 + تنبيه |
| `GET /venues` (Explore) | 60/د (token bucket) | ip / user | 429 |
| `GET /venues/{id}/availability` | 120/د | user | 429 |
| `POST /bookings` | 5/د، 20/ساعة | user | 429 |
| **حجوزات Pending مفتوحة** | **2 كحد أقصى لكل لاعب** | business rule | 409 |
| `POST /bookings/{id}/proof` | 5/ساعة، 2 concurrent | user | 429 |
| `POST /teams/*/challenges` | 20/يوم | user | 429 |
| WebSocket connect | 10/د | ip/user | رفض |
| Admin endpoints | 60/د | admin id | 429 + تنبيه |
| أي API عام | 300/د | ip (Cloudflare) | challenge |

### 7.6 التطبيق في ASP.NET
```csharp
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (ctx, ct) =>
    {
        if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
            ctx.HttpContext.Response.Headers.RetryAfter = ((int)retry.TotalSeconds).ToString();
        ctx.HttpContext.Response.ContentType = "application/problem+json";
        return new ValueTask(ctx.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails { Status = 429, Title = "too_many_requests", Extensions = { ["code"] = "rate_limited" } }, ct));
    };

    o.AddPolicy("otp-send", http =>
    {
        var phone = http.GetNormalizedPhoneFromBody();       // قراءة بحذر (buffering) أو من middleware مسبق
        var key = $"{http.GetClientIp()}|{phone}";
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        { PermitLimit = 3, Window = TimeSpan.FromMinutes(10), SegmentsPerWindow = 5, QueueLimit = 0 });
    });

    o.AddPolicy("api-user", http => RateLimitPartition.GetTokenBucketLimiter(
        http.User.FindFirst("sub")?.Value ?? http.GetClientIp(),
        _ => new TokenBucketRateLimiterOptions { TokenLimit = 60, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(1), QueueLimit = 0 }));
});
app.UseRateLimiter();
app.MapPost("/auth/otp/send", Handler).RequireRateLimiting("otp-send");
```
**مهم جداً:**
1. الـ `RateLimiter` المدمج **بيخزّن العدّاد في ذاكرة كل instance**. لو عندك أكتر من سيرفر، المهاجم هيتوزع عليهم. الحل: **عدّاد مركزي في Redis** (Lua script أو مكتبة موثوقة) على الـ endpoints الحساسة (OTP/login).
2. **الـ IP الحقيقي:** ورا Cloudflare، الـ IP اللي بيوصل لك هو IP Cloudflare. لازم تقرأ `CF-Connecting-IP` **بس لو الطلب جاي فعلاً من نطاقات Cloudflare** (وإلا حد يزوّر الهيدر ويتجاوز الحدود):
```csharp
app.Use(async (ctx, next) =>
{
    if (cloudflareRanges.Contains(ctx.Connection.RemoteIpAddress!) &&
        ctx.Request.Headers.TryGetValue("CF-Connecting-IP", out var ip) && IPAddress.TryParse(ip, out var real))
        ctx.Connection.RemoteIpAddress = real;
    await next();
});
```
(أو استخدم `ForwardedHeaders` مع `KnownProxies/KnownNetworks` لنطاقات Cloudflare. نطاقات Cloudflare بتتحدث، اجلبها دورياً.)
3. **ما تكشفش تفاصيل**: الرد `429` + `Retry-After` بس.
4. **الفشل المغلق:** لو Redis وقع، الـ endpoints الحساسة (OTP) ترفض، مش تسمح بلا حد.
5. سجّل الـ 429 كـ metric واعمل **alert** على الزيادة المفاجئة.

### 7.7 الأخطاء الشائعة
- ❌ Rate limit على الـ IP بس (مستخدمو الموبايل بيشاركوا IP).
- ❌ ما فيش حد على الـ OTP (= فاتورة مفتوحة).
- ❌ Lockout دائم للحساب (DoS على الضحية).
- ❌ الـ IP الحقيقي مش متظبط ورا Cloudflare (كل الناس شكلها IP واحد وتتحجب كلها).

---

## 8. WebSocket / SignalR وقت الحجز

### 8.1 هنستخدمه في إيه؟
- تحديث **توفر المواعيد** لحظياً لما حد يحجز (شاشة اختيار الميعاد).
- تحديث **حالة الحجز** للاعب (تم رفع إيصال → قيد المراجعة → مؤكد).
- إشعار **طلبات جديدة وإيصالات** لداشبورد الملعب.

**قاعدة ذهبية:** الـ WebSocket لـ **السيرفر ← العميل فقط (push)**. أي عملية بتغيّر حالة (احجز، ألغِ، وافق) **بتتم عبر REST** (فيها validation وIdempotency وrate limit وaudit). ده بيقلل مساحة الهجوم لأقصى حد.

### 8.2 التهديدات الخاصة بـ WS
| التهديد | الشرح |
|---|---|
| **Token في الـ URL** | المتصفحات مش بتقدر تبعت `Authorization` في WebSocket، فـ SignalR بيبعت `?access_token=` في الـ URL ← بيتسجل في اللوجز |
| **CSWSH** (Cross-Site WebSocket Hijacking) | WebSocket **مش محمي بـ CORS**؛ لو معتمد على cookie، موقع خبيث يفتح WS باسمك |
| **انتهاء التوكن أثناء الاتصال** | التحقق بيحصل مرة واحدة وقت الاتصال، والاتصال يفضل مفتوح بعد ما التوكن ينتهي أو اليوزر يتحظر |
| **الدخول لغرف مش بتاعتك** | عميل يطلب `Join("venue-staff:77")` |
| **إغراق** | آلاف الاتصالات أو رسائل ضخمة |
| **تسريب بيانات** | بث "اللاعب فلان حجز" لكل الناس |
| **Replay/duplicate** | رسائل متكررة بعد reconnect |

### 8.3 الحل خطوة بخطوة

**أ) "تذكرة" اتصال قصيرة (WS Ticket) بدل الـ access token في الـ URL**
```
1) العميل (مسجل دخول) → POST /api/v1/realtime/ticket   (Bearer عادي)
2) السيرفر يطلّع ticket: JWT صغير aud=malabak-ws، عمره 60 ثانية، فيه jti،
   ويخزن jti في Redis بـ TTL 60 ث.
3) العميل يفتح: wss://api.malabak.app/hubs/booking?access_token=<ticket>
4) السيرفر يتحقق من التوقيع + aud + exp، ويعمل "استهلاك": يمسح الـ jti من Redis.
   لو المسح رجّع false = التذكرة اتستخدمت قبل كده → ارفض.
```
فايدتها: لو الـ URL اتسجل في أي مكان، الـ ticket **بيكون ميت** (60 ث + مرة واحدة). وكمان الـ access token الحقيقي عمره ما بيظهر في URL.

```csharp
// إعداد scheme منفصل للـ hubs
builder.Services.AddAuthentication()
  .AddJwtBearer("WsTicket", o =>
  {
      o.TokenValidationParameters = /* نفس الصارمة، لكن Audience = "malabak-ws" وعمر قصير */;
      o.Events = new JwtBearerEvents
      {
          OnMessageReceived = ctx =>
          {
              var path = ctx.HttpContext.Request.Path;
              if (path.StartsWithSegments("/hubs"))                       // بس مسارات الـ hubs
                  ctx.Token = ctx.Request.Query["access_token"];
              return Task.CompletedTask;
          },
          OnTokenValidated = async ctx =>
          {
              var jti = ctx.Principal!.FindFirst("jti")!.Value;
              var redis = ctx.HttpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
              if (!await redis.KeyDeleteAsync($"wsjti:{jti}"))            // مرة واحدة فقط
                  ctx.Fail("ticket_already_used");
          }
      };
  });

[Authorize(AuthenticationSchemes = "WsTicket")]
public sealed class BookingHub : Hub { ... }
```
**وبرضه:** اخفي الـ query string من اللوجز (ASP.NET request logging، Cloudflare Logpush، reverse proxy).

**ب) WebSockets بس، بدون fallback**
```csharp
app.MapHub<BookingHub>("/hubs/booking", o => o.Transports = HttpTransportType.WebSockets);
```
(الـ long-polling والـ SSE بيبعتوا التوكن مع كل طلب ويوسعوا الهجوم.)

**ج) فحص الـ Origin (ضد CSWSH)**
```csharp
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/hubs") && ctx.Request.Headers.TryGetValue("Origin", out var origin)
        && !allowedOrigins.Contains(origin!)) { ctx.Response.StatusCode = 403; return; }
    await next();
});
```
(تطبيق الموبايل غالباً مفيش Origin؛ تعامل معاه حسب الـ ticket + attestation.) وبما إننا بنستخدم ticket مش cookie، الخطر CSWSH أقل بكتير، بس الـ Origin check طبقة إضافية.

**د) أقصى عمر للاتصال + فصل عند الإلغاء**
- الاتصال يعيش **أقصى 15 دقيقة** وبعدها العميل يطلب ticket جديد ويعيد الاتصال (مع jitter).
- لما الجلسة تتلغى (logout/ban/reuse detected) السيرفر ينشر حدث `session.revoked` على Redis، وخدمة بتفصل كل اتصالات الـ `sid` ده (`Context.Abort()`).
- Hub filter يفحص `IsActiveAsync(sid)` على عمليات معينة.

**هـ) الغرف (Groups) — السيرفر هو اللي يقرر**
```csharp
public override async Task OnConnectedAsync()
{
    var userId = Context.User!.GetUserId();
    await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");  // خاص باليوزر، من الـ token مش من العميل
    await base.OnConnectedAsync();
}

public async Task WatchVenue(Guid venueId, DateOnly date)                  // متابعة توفر ملعب (معلومة عامة)
{
    if (!await limits.CanWatchAsync(Context.UserIdentifier!))  throw new HubException("limit");
    await Groups.AddToGroupAsync(Context.ConnectionId, $"avail:{venueId}:{date:yyyyMMdd}");
}

public async Task JoinStaffRoom(Guid venueId)                              // لموظفي الملعب فقط
{
    if (!await access.IsStaffOfVenueAsync(Context.User!, venueId))         // فحص من الداتابيز
        throw new HubException("forbidden");
    await Groups.AddToGroupAsync(Context.ConnectionId, $"staff:{venueId}");
}
```
- **ممنوع** إن العميل يبعت اسم Group حر.
- أرسل لـ `user:{id}` للأحداث الخاصة (حالة حجزي).
- بث **التوفر** يكون **بدون هوية**: `{ pitchId, slot, status: "held|booked|free" }` (مفيش اسم اللاعب).

**و) حدود الاتصال والرسائل**
```csharp
builder.Services.AddSignalR(o =>
{
    o.MaximumReceiveMessageSize = 16 * 1024;           // الافتراضي 32KB، قللها
    o.KeepAliveInterval = TimeSpan.FromSeconds(15);
    o.ClientTimeoutInterval = TimeSpan.FromSeconds(45);
    o.HandshakeTimeout = TimeSpan.FromSeconds(10);
    o.MaximumParallelInvocationsPerClient = 1;
    o.EnableDetailedErrors = false;                    // مفيش تفاصيل أخطاء للعميل في الإنتاج
});
```
- حد أقصى **3–5 اتصالات لكل مستخدم** و**20 لكل IP**، واتصال جديد يطرد الأقدم.
- Rate limit على استدعاءات الـ hub methods (Hub filter).
- Backpressure: لو العميل بطيء، اقفله.

**ز) التوسع (أكتر من سيرفر)**
- **Redis backplane** (`AddStackExchangeRedis`) مع TLS + كلمة سر.
- لأننا WebSockets فقط، مفيش حاجة اسمها sticky sessions.

**ح) Cloudflare**
- WebSockets شغالة على Cloudflare. ابعت **ping/keepalive كل 15–30 ث** علشان الاتصال ما يتقفلش لو ساكت.
- متكاشيش الـ `/hubs`.
- Rate limiting rule على `/api/v1/realtime/ticket` و`/hubs`.

**ط) العميل (الموبايل)**
- Reconnect بـ **exponential backoff + jitter** (1s, 2s, 4s... max 30s) علشان ما نعملش "thundering herd" لو السيرفر وقع.
- بعد كل reconnect: **اعمل re-sync بـ REST** (جيب الحالة الحالية) — لأن ممكن تكون فاتتك رسائل.
- فصل الـ WS لما الأب يروح background (يوفر بطارية وتهديدات)، وإعادة فتحه عند الـ foreground.
- الأحداث تكون **idempotent** (لو جت مرتين ما تبوظش الواجهة): ابعت `eventId` و `version`.

### 8.4 وقت الحجز تحديداً (Flow آمن)
```
1) اللاعب يفتح شاشة المواعيد → REST: GET availability (المصدر الحقيقي)
2) يفتح WS → يستقبل تحديثات "slot held/booked/free" لتغطية الواجهة
3) يضغط احجز → REST: POST /bookings (Idempotency-Key)
        السيرفر: slot hold + DB exclusion constraint (الحارس الحقيقي ضد الحجز المزدوج)
4) السيرفر يبث (بعد commit) حدث "slot held" لغرفة التوفر + حدث خاص "booking pending" لغرفة user
5) لو اتأخر اللاعب → job الـ expiry يحرر الميعاد → بث "slot free"
```
- **الـ WebSocket مش هو اللي بيمنع الحجز المزدوج.** اللي بيمنعه الداتابيز. الـ WS بيحدّث الشاشة بس.
- **ما تثقش في حالة الشاشة**: حتى لو الميعاد بان فاضي، السيرفر هو الحكم عند `POST /bookings` وبيرجّع 409 لو اتحجز.

### 8.5 أخطاء شائعة
- ❌ استقبال أوامر حجز من الـ WS.
- ❌ `access_token` طويل العمر في الـ URL.
- ❌ ثقة في `groupName` من العميل.
- ❌ اتصال مفتوح لأيام بعد انتهاء التوكن.
- ❌ بث بيانات شخصية للجميع.
- ❌ مفيش حد للاتصالات.

---

## 9. التشفير (Encryption)

### 9.1 الخريطة
```
في الطريق (in transit)  ← TLS 1.2/1.3 في كل حتة
على الديسك (at rest)    ← تشفير الداتابيز/الديسك/الـ backups/الـ blob
على مستوى الحقل (field) ← AES-256-GCM لأخطر البيانات
المفاتيح (key mgmt)     ← KMS/Key Vault + rotation + فصل الصلاحيات
```

### 9.2 In Transit (TLS)
- **HTTPS فقط**، إعادة توجيه HTTP→HTTPS، **HSTS** (`max-age=31536000; includeSubDomains; preload`).
- حد أدنى **TLS 1.2** (يفضل 1.3 فقط لو العملاء بتدعمه).
- **Cloudflare SSL mode = Full (strict)** (مش Flexible أبداً ← Flexible معناه إن الجزء بين Cloudflare والسيرفر بدون تشفير!). استخدم **Cloudflare Origin CA certificate** على السيرفر.
- **Authenticated Origin Pulls (mTLS)**: السيرفر يقبل بس اتصالات معاها شهادة Cloudflare.
- **TLS داخلي**: التطبيق ↔ PostgreSQL (`SSL Mode=VerifyFull`)، التطبيق ↔ Redis (`ssl=true` + كلمة سر)، التطبيق ↔ Storage.
- شهادات تتجدد تلقائي (Let's Encrypt/Cloudflare)، وراقب انتهاءها.

### 9.3 At Rest
- **PostgreSQL:** تشفير الديسك/الـ volume من مزود الاستضافة (مفعّل). الـ backups مشفّرة، ومخزنة في مكان تاني، ومتجربة استرجاعها.
- **Object storage (إيصالات/صور):** SSE (يفضل SSE-KMS)، bucket **خاص** (Block public access).
- **اللوجز:** من غير بيانات شخصية، ومشفّرة وبمدة احتفاظ محددة.
- **أجهزة الموظفين:** تشفير الديسك.

### 9.4 Field-Level Encryption (تشفير على مستوى الحقل)
**ليه؟** لأن تشفير الديسك بيحمي لو سرقوا الهارد بس. لو اتسربت الداتابيز عن طريق SQL injection أو حساب DBA مخترق، الحقول المشفّرة تفضل بلا قيمة.

**إيه اللي يتشفّر؟** (القائمة تتناقش قانونياً)
| البيانات | الإجراء |
|---|---|
| تفاصيل الدفع للملعب (رقم حساب بنكي، محفظة، IBAN) | **تشفير AES-GCM** |
| ملاحظات الدعم الحساسة | تشفير |
| أرقام التليفون | **تشفير + Blind index** (للبحث) |
| الإيميل | تشفير + blind index (أو اكتفِ بالـ DB encryption حسب المخاطر) |
| الباسورد | **Hash** (مش تشفير) |
| الـ refresh token / OTP | **Hash/HMAC** |
| صور الإيصالات | تشفير على مستوى الـ storage |

**الخوارزمية:** `AES-256-GCM` (تشفير + تحقق سلامة). قواعد:
1. **Nonce (IV) 12 بايت عشوائي ومختلف في كل مرة** (تكرار الـ nonce مع نفس المفتاح = كارثة).
2. **AAD (Associated Data):** اربط الـ ciphertext بمكانه (`table|column|rowId`) علشان ما حدش ينقله لصف تاني.
3. خزّن `version | keyId | nonce | tag | ciphertext`.

```csharp
public sealed class FieldCipher(IKeyProvider keys)
{
    public byte[] Encrypt(string plaintext, string aad)
    {
        var (keyId, key) = keys.CurrentDataKey();                 // DEK
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length]; var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(aad));
        return [1, ..BitConverter.GetBytes(keyId), ..nonce, ..tag, ..cipher];   // version|keyId|nonce|tag|ct
    }
    public string Decrypt(byte[] blob, string aad) { /* عكسها، لو الـ tag غلط ← CryptographicException */ }
}
```
**Blind Index (علشان تبحث برقم التليفون وهو مشفّر):**
```csharp
var phoneHash = HMACSHA256.HashData(blindIndexKey, Encoding.UTF8.GetBytes(NormalizeE164(phone)));
// عمود phone_hash (unique index) للبحث/الـ uniqueness، وعمود phone_enc للعرض
```
- مفتاح الـ blind index **غير** مفتاح التشفير.
- ده بيسمح بـ "هل الرقم ده موجود؟" لكن مش "رقم يبدأ بـ 0100" (مفيش بحث جزئي).

### 9.5 إدارة المفاتيح (أهم من الخوارزمية)
**Envelope Encryption:**
```
KEK (Key Encryption Key)  ← جوه KMS/Key Vault، عمره ما بيطلع منه
   └─ يشفّر ─► DEK (Data Encryption Key) ← بيتخزن مشفّر جنب الداتا ويتفك في الذاكرة
                  └─ يشفّر ─► بيانات الصفوف
```
- المفاتيح **مش في الكود ولا في الريبو ولا في الـ appsettings**.
- **Rotation:** KEK سنوياً، DEK حسب السياسة؛ الـ `keyId` في الـ blob بيسمح بفك القديم وتشفير الجديد، وjob لإعادة تشفير تدريجي.
- **فصل الصلاحيات:** اللي يقدر يقرأ الداتابيز ≠ اللي يقدر يستخدم المفتاح.
- **Backup للمفاتيح** (لو ضاع المفتاح، الداتا ضاعت).
- **ASP.NET Data Protection API:** للـ cookies والـ antiforgery والتوكنات المؤقتة. **لازم تخزّن الـ keyring خارج السيرفر** (DB/Redis/Blob) ومشفّر بـ KMS، وإلا لو السيرفر اتعمله redeploy الـ cookies كلها تبوظ.
```csharp
builder.Services.AddDataProtection().SetApplicationName("malabak")
    .PersistKeysToDbContext<DataProtectionDbContext>()
    .ProtectKeysWithAzureKeyVault(keyUri, credential);   // أو AWS KMS
```

### 9.6 جدول "أستخدم إيه؟"
| الحاجة | الأداة |
|---|---|
| باسورد | Argon2id (أو PBKDF2 مرفوع) |
| refresh token / reset token / invitation | عشوائي 256 بت + SHA-256 في الداتابيز |
| OTP | HMAC-SHA256 + مقارنة ثابتة الوقت |
| توقيع JWT | ES256 |
| webhook | HMAC-SHA256 + مقارنة ثابتة الوقت + timestamp لمنع replay |
| بيانات حساسة | AES-256-GCM + envelope + AAD |
| بحث على مشفّر | HMAC blind index |
| ملفات | SSE-KMS + signed URLs |
| تبادل مفاتيح/اتصال | TLS 1.3 |
| أرقام عشوائية للأمان | `RandomNumberGenerator` (مش `Random`) |

---

## 10. الصلاحيات (Authorization) و IDOR

### 10.1 الفرق
- **Authentication:** إنت مين؟ ← التوكن.
- **Authorization:** مسموحلك تعمل إيه؟ ← الصلاحيات.

### 10.2 أخطر ثغرة في APIs: BOLA/IDOR
`GET /bookings/123` ← اللاعب يغيّر الرقم ويشوف حجز حد تاني. الحل **في كل handler**:
```csharp
var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct);
if (booking is null) return Error.NotFound();                           // ما تفرقش بين "مش موجود" و"مش بتاعك" لو ده بيكشف وجوده
if (!access.CanView(user, booking)) return Error.NotFound();            // أو Forbidden حسب الحالة
```
- أو الأحسن: **فلتر الـ query بالمالك أصلاً** (`WHERE booking.PlayerId = @me OR venue in myVenues`) فمفيش طريقة تجيب غلط.
- استخدم **GUIDs/Guid v7 مش أرقام متسلسلة**، لكن ده **مش بديل** عن فحص الصلاحية.
- **Mass assignment:** استخدم DTOs. ما تـ bind الـ request مباشرة على entity (ممكن حد يبعت `isAdmin: true` أو `price`).

### 10.3 نموذج الصلاحيات
- **Roles** (Player, VenueOwner, VenueStaff, Admin, Support, Finance) + **Permissions** دقيقة (`booking.approve`, `venue.edit`, `report.view`).
- **Resource-based policies:** "هل المستخدم موظف في ملعب صاحب الحجز؟"
- **Tenant isolation:** كل بيانات الملعب مفلترة بـ `venue_id`. استخدم **EF Global Query Filter** + اختبارات تتأكد إن مفيش تسريب بين الملاعب.
```csharp
modelBuilder.Entity<Booking>().HasQueryFilter(b => b.VenueId == _tenant.VenueId || _tenant.IsPlatformAdmin);
```
- الصلاحيات الحساسة تتفحص من الداتابيز (مش من التوكن) ومعاها كاش قصير.
- **Fallback policy**: كل endpoint يطلب Authenticated إلا اللي معلّم `[AllowAnonymous]` صراحة.
```csharp
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
```
- **اختبارات تلقائية**: لكل endpoint: Anonymous=401، دور غلط=403، بيانات ملعب تاني=404/403، صح=200.

### 10.4 الدعم الفني والأدمن
- **Impersonation** (دخول بصفة عميل للمساعدة): كود لمرة واحدة 60 ث في Redis، توكن قصير فيه claim `act` (مين اللي بيتقمص)، **بدون صلاحيات حساسة** (مفيش تغيير باسورد/رقم/دفع)، **بانر ظاهر** في الواجهة، وكل شيء في الـ audit + إشعار للعميل.

---

## 11. إساءة استخدام الحجز (Business-logic abuse)

دي مش "اختراق" تقني، لكنها أخطر على الـ business.

| الإساءة | الحماية |
|---|---|
| **Slot hoarding** (حجز وترك) | حد أقصى **2 حجز pending لكل لاعب**، مدة hold قصيرة (مثلاً 15–30 د لرفع الإيصال)، **نقاط سمعة**: تكرار الـ expiry بدون دفع ← تقييد مؤقت أو طلب دفع مسبق |
| **حجوزات وهمية متعددة بحسابات متعددة** | تحقق رقم الموبايل + device attestation + rate limit بالـ device + مراقبة أنماط |
| **إيصالات مزورة / متكررة** | Hash للصورة (SHA-256 + perceptual hash) لكشف نفس الإيصال على أكتر من حجز؛ المراجعة البشرية من الملعب؛ سجل لكل مراجعة؛ **ما تقولش "verified" بدون موافقة الملعب** |
| **اختلاف السعر** | السعر يتحسب في السيرفر وبيتخزن snapshot |
| **Replay/Double submit** | `Idempotency-Key` فريد لكل محاولة، مخزن 24 ساعة مع نتيجته |
| **تجاوز مدة الإلغاء** | السيرفر يحسب من وقت السيرفر (مش وقت الجهاز) |
| **Scraping الأسعار والتوفر** | Rate limit + Bot management + عدم كشف بيانات زيادة + pagination محدود |
| **تضخيم إحصائيات الفرق** | النتيجة تتأكد من الفريق التاني، والنزاعات لا تؤثر على الترتيب |
| **موظف ملعب يتلاعب** | Audit كامل، صلاحيات دقيقة، تنبيه لقرارات غير عادية |

**Idempotency-Key (إزاي):**
```
POST /bookings  Idempotency-Key: 7c1a...   (الأب يولّده UUID مرة لكل "ضغطة احجز" ويعيد استخدامه في الـ retry)
السيرفر: لو المفتاح موجود → رجّع نفس النتيجة القديمة (مش بيعمل حجز جديد)
          لو جديد → نفّذ، خزّن (key + hash للـ request + response) لمدة 24 س
          لو نفس المفتاح بـ body مختلف → 422
```

---

## 12. رفع الملفات (إيصالات الدفع وصور الملاعب)

- **الحد الأقصى للحجم** (مثلاً 5MB) في Cloudflare + Kestrel + الـ endpoint.
- **الأنواع المسموحة:** JPEG/PNG/WebP/PDF. **تحقق من الـ magic bytes** (مش الامتداد ولا الـ Content-Type لوحده).
- **أعد ترميز الصور** (re-encode) علشان تشيل أي payload مخفي، و**شيل EXIF** (فيه GPS/موديل الجهاز = خصوصية).
- **مسح فيروسات** (ClamAV أو خدمة) قبل ما الملف يبقى متاح.
- **اسم عشوائي** (GUID) — ما تستخدمش اسم المستخدم (Path traversal).
- التخزين **خاص** (مش `wwwroot` ومش public bucket).
- التحميل عن طريق endpoint بيفحص الصلاحية، أو **Signed URL** عمره 5 دقايق.
- Headers عند التحميل: `Content-Disposition: attachment`، `X-Content-Type-Options: nosniff`، ودومين منفصل للملفات لو في محتوى مستخدم.
- **Retention:** حذف الإيصالات بعد فترة محددة (مثلاً 12–24 شهر) وفق سياسة مكتوبة.
- Rate limit + concurrency limit على الرفع.

---

## 13. Cloudflare

### 13.1 ليه؟
Cloudflare بيقف **قدام** السيرفر بتاعك: بيمتص DDoS، بيفلتر الطلبات السيئة، بيشفّر، بيخبّي IP السيرفر الحقيقي. بدونه، أي حد يقدر يضرب السيرفر مباشرة.

### 13.2 الإعدادات المطلوبة (Checklist)

**DNS/الأساسيات**
- [ ] كل الدومينات/السبدومينات (`api`, `venue`, `admin`, `app`) **Proxied (السحابة البرتقالية)**.
- [ ] **DNSSEC** مفعّل. مسجّل الدومين فيه 2FA وقفل Registrar Lock.
- [ ] **ما تكشفش IP السيرفر**: ما تعملش DNS لـ IP مباشر، وانتبه لسجلات MX/mail وأي subdomain غير مخفي. لو IP اتكشف، غيّره.

**SSL/TLS**
- [ ] Mode = **Full (strict)**، **Always Use HTTPS**، **Min TLS 1.2**، **TLS 1.3 مفعّل**، **HSTS** مفعّل.
- [ ] **Origin CA cert** على السيرفر + **Authenticated Origin Pulls**.

**حماية السيرفر الأصلي (Origin lock)**
- [ ] Firewall على السيرفر يسمح فقط بنطاقات Cloudflare IP على 443 (والباقي مقفول).
- [ ] أو الأفضل: **Cloudflare Tunnel (`cloudflared`)** ← السيرفر ما بيفتحش أي port للإنترنت أصلاً.

**WAF**
- [ ] **Managed Rules** (Cloudflare Managed + OWASP Core) مفعّلة.
- [ ] **Custom rules** مثلاً:
  - حجب أي طلب لـ `/admin` من خارج IPs/Access.
  - حجب دول مش مستهدفة على `/auth/otp/send` (أو Challenge).
  - حجب User-Agents المعروفة كـ scrapers.
  - Challenge للـ ASN المشبوهة (data centers) على مسارات الحجز.

**Rate Limiting Rules** (أول طبقة)
- [ ] `/api/v1/auth/*` ← حد صارم لكل IP.
- [ ] `/api/v1/auth/otp/send` ← حد أصرم + Challenge.
- [ ] `/hubs/*` و `/api/v1/realtime/ticket`.
- [ ] API عام ← حد معقول.
(راجع حدود خطتك: بعض الخطط المجانية لها عدد قواعد محدود.)

**Bots**
- [ ] **Bot Fight Mode / Super Bot Fight Mode** (حسب الخطة).
- [ ] **Turnstile** على: إرسال OTP، تسجيل الدخول، التسجيل، نموذج الدعم.

**DDoS**
- [ ] الحماية التلقائية مفعّلة (L3/4/7). جهّز "I'm Under Attack Mode" للطوارئ.
- [ ] تنبيهات DDoS عبر Notifications.

**Cloudflare Access (Zero Trust) للأدمن والبيئات الداخلية**
- [ ] `admin.malabak.app` و `staging.*` و Hangfire dashboard و Swagger الداخلي خلف **Access** (SSO/OTP + سياسات).
- [ ] طبقة إضافية فوق تسجيل دخول الأدمن (حتى لو التطبيق اتخرق، مش هيوصل له).

**الكاش**
- [ ] **ما تكاشيش** أي رد فيه `Authorization` أو بيانات مستخدم: ضع `Cache-Control: no-store` على الـ API الخاص.
- [ ] كاش للصور والملفات الثابتة العامة فقط.
- [ ] تأكد من Cache Rules إن `/api/*` = Bypass cache.

**Headers/Transforms**
- [ ] إضافة security headers عند الحافة كطبقة إضافية.
- [ ] إزالة `Server`/`X-Powered-By`.

**WebSockets**
- [ ] مفعّل (افتراضي). Keepalive 15–30 ث.

**المراقبة**
- [ ] **Logpush** للـ firewall/HTTP logs → مكان آمن (مع إخفاء query string).
- [ ] تنبيهات: ارتفاع 429/403/5xx، هجوم DDoS، تغيير في إعدادات الحساب.
- [ ] الحساب نفسه: **2FA إجباري** (مفتاح أمان)، أقل صلاحيات للأعضاء، API tokens بأقل scope، Audit logs.

### 13.3 خدمات إضافية ممكن تفيد
- **R2** (تخزين الملفات) أو S3 مع Cloudflare قدامه.
- **Cloudflare Images/Polish** لتحسين صور الملاعب.
- **Workers** لو احتجنا منطق حافة (مثلاً التحقق المبدئي).
- **Email Routing/Security** للدومين.

### 13.4 تحذيرات
- Cloudflare **مش بديل** عن أمان التطبيق. هو طبقة.
- لو الـ origin متقفلش على Cloudflare، المهاجم يتخطاه ويضرب السيرفر مباشرة.
- إعدادات Flexible SSL = خطر.
- راجع الـ plan: ميزات (Bot Management، Rate Limiting rules، WAF custom rules) بتختلف حسب الخطة.

---

## 14. أمان تطبيق الموبايل

- **ما تحطش أسرار في التطبيق** (API keys، مفاتيح تشفير). أي حاجة في الـ APK/IPA ممكن تتفك. الأسرار الحقيقية في السيرفر.
- **تخزين:** Keystore/Keychain (القسم 3.7). `android:allowBackup="false"` وقواعد `dataExtractionRules`.
- **HTTPS فقط:** `usesCleartextTraffic=false`، و ATS في iOS.
- **Certificate pinning:** يمنع MITM لو الجهاز فيه شهادة مزروعة. **بحذر:** بتبوظ التطبيق لو الشهادة اتغيّرت. مع Cloudflare الشهادات بتتدور، فاعمل **pin على public key (SPKI) مع مفتاح backup**، أو استخدم pinning للـ intermediate CA، ومعاه آلية تحديث. لو مش جاهز تدير ده، الأفضل ما تعملوش بدل ما تقفل التطبيق على المستخدمين.
- **Obfuscation/Minify:** R8/ProGuard في أندرويد (بيصعّب مش بيمنع).
- **Root/Jailbreak detection:** إشارة مش حماية؛ متبنيش أمانك عليها.
- **Play Integrity API / App Attest:** يثبتوا إن الطلب جاي من تطبيقك الأصلي على جهاز سليم → استخدمهم قبل OTP والحجز.
- **Screens حساسة:** `FLAG_SECURE` (يمنع screenshots) على شاشة الإيصال/الدفع لو حبيت.
- **Biometric** اختياري لفتح التطبيق.
- **Deep links:** تحقق من المدخلات (مثلاً `malabak://booking/{id}`)، ومفيش تنفيذ فعل حساس بدون تأكيد. استخدم **App Links/Universal Links** (مش custom schemes فقط).
- **Logs:** مفيش توكن/OTP/بيانات شخصية في Logcat أو Crashlytics.
- **Clipboard:** ما تنسخش بيانات حساسة تلقائياً.
- **تحديث إجباري:** endpoint يرجّع `minSupportedVersion` علشان تقفل النسخ القديمة المصابة.
- **Dependencies:** راجع مكتبات الأب (SCA).

---

## 15. Headers، Input، Secrets، Logging، Audit

### 15.1 Security Headers (API + الويب)
| Header | القيمة | الغرض |
|---|---|---|
| `Strict-Transport-Security` | `max-age=31536000; includeSubDomains; preload` | HTTPS دايماً |
| `Content-Security-Policy` | صارمة بـ nonce، `default-src 'self'`، `frame-ancestors 'none'`، `object-src 'none'`، `base-uri 'self'` | منع XSS/Clickjacking |
| `X-Content-Type-Options` | `nosniff` | منع تخمين الـ MIME |
| `Referrer-Policy` | `strict-origin-when-cross-origin` | تقليل تسريب الـ URL |
| `Permissions-Policy` | `geolocation=(self), camera=(), microphone=()` | تقييد الميزات |
| `Cross-Origin-Opener-Policy` | `same-origin` | عزل |
| `Cache-Control` | `no-store` على الـ API الخاص | منع الكاش |
- استخدم `NWebsec`/middleware، وراجع بـ securityheaders.com.

### 15.2 Input
- Validate **كل** مدخل (طول، نوع، نطاق، regex بحذر). FluentValidation.
- **Allowlist** مش blocklist.
- EF Core parameterized دايماً. **ممنوع** string concat في SQL (`FromSqlRaw` بمتغيرات = خطر؛ استخدم `FromSqlInterpolated`).
- حدود حجم الـ body، وعدد عناصر الـ arrays، وعمق الـ JSON.
- تعامل مع Unicode/RTL: اسم فريق فيه رموز تحكم اتجاه ممكن يخدع الواجهة (spoofing) ← normalize وامنع control chars.
- **SSRF:** لو السيرفر بيجيب URL من المستخدم (مثلاً صورة)، allowlist للدومينات وامنع الـ IPs الداخلية.
- **Open redirect:** ما تعملش redirect لـ URL جاي من المستخدم بدون allowlist.

### 15.3 الأخطاء
- **ProblemDetails** بكود ثابت، **بدون stack trace/اسم جدول/إصدار** للعميل في الإنتاج.
- رسائل الـ auth عامة ("بيانات غير صحيحة").
- ما تفرقش في الرد/الزمن بين "مستخدم موجود" و"مش موجود" (تماثل الزمن).

### 15.4 Secrets
- **ممنوع** في Git: `.env`, `appsettings.Production.json`, مفاتيح، شهادات.
- Dev: **User Secrets**. Prod: **Key Vault / AWS Secrets Manager / Doppler** + Managed Identity.
- أداة **gitleaks/trufflehog** في الـ pre-commit والـ CI.
- لو سر اتسرّب: **غيّره فوراً** (rotation)، حتى لو مسحته من الـ commit (التاريخ فيه).
- أسرار مختلفة لكل بيئة. أقل صلاحية لكل سر.

### 15.5 Logging (بدون ما تسرّب)
- **Structured logs** (Serilog) + `CorrelationId` في كل طلب.
- **ممنوع تسجيل:** توكنات، OTP، باسوردات، أرقام حسابات، محتوى الإيصالات، الـ query string الحساسة.
- **Mask** للأرقام (`+2010****1234`) والإيميلات.
- اللوجز في مكان آمن بصلاحيات محدودة، ومدة احتفاظ.
- **Log injection:** نظّف القيم (سطور جديدة).

### 15.6 Audit Log (غير اللوجز العادية)
- **ليه؟** مين عمل إيه ومتى — للمسؤولية والتحقيق والنزاعات.
- **يتسجل:** تسجيل دخول/فشل/جهاز جديد، تغيير رقم/إيميل/باسورد، refresh reuse، قبول/رفض إيصال، تغيير حالة حجز، تغيير سعر/سياسة، عمليات الأدمن، impersonation، تغيير صلاحيات، تصدير بيانات، حذف.
- **الحقول:** `actor_id`, `actor_type`, `action`, `entity_type`, `entity_id`, `before`, `after`, `ip`, `device`, `correlation_id`, `at_utc`.
- **Append-only:** مفيش UPDATE/DELETE (صلاحيات DB تمنع)، وفي نسخة مقروءة بس. يفضل hash chain أو تخزين في مكان WORM.
- **اللي بيشوفه:** الأدمن/المالك حسب النطاق. العميل يشوف اللي يخصه (إشعار "تم تغيير رقمك").

---

## 16. البنية التحتية و DevSecOps

### 16.1 الشبكة
- الداتابيز والـ Redis **مش متاحين للإنترنت**: شبكة خاصة (VPC/private network) وفايروول.
- **أقل صلاحيات** لحساب التطبيق على الداتابيز (مش superuser، ولا يقدر DROP). حسابات منفصلة: تطبيق، migrations، read-only للتقارير.
- SSH بالمفاتيح فقط + 2FA/bastion، أو بدون SSH (Tunnel/SSM).
- Docker: **non-root user**، صورة صغيرة (distroless/alpine)، مفيش أسرار في الـ image، `read_only` filesystem لو أمكن.

### 16.2 CI/CD أمان
| الفحص | الأداة |
|---|---|
| Secrets | gitleaks |
| ثغرات المكتبات | `dotnet list package --vulnerable`، Dependabot/Renovate، `pnpm audit` |
| SAST | CodeQL / SonarQube / Semgrep |
| Container | Trivy / Grype |
| IaC | Checkov/tfsec |
| DAST | OWASP ZAP على staging |
| SBOM | CycloneDX |
- **Branch protection**: مراجعة إجبارية، 2FA لكل المطورين، commits موقّعة (اختياري).
- نشر الـ production بموافقة يدوية، ومن pipeline فقط (مفيش deploy من لابتوب).
- ثبّت إصدارات الـ GitHub Actions (SHA) علشان supply-chain.

### 16.3 Supply chain
- راجع كل NuGet/npm جديد: الـ licence، آخر تحديث، عدد المساهمين. الـ lockfiles (`packages.lock.json`, `pnpm-lock.yaml`) في الـ Git.
- تجنّب مكتبات مهجورة.

### 16.4 Backups & DR
- **3-2-1**: 3 نسخ، على وسيطين، واحدة خارج الموقع. مشفّرة.
- **جرّب الاسترجاع** كل فترة (نسخة غير مجربة = مفيش نسخة).
- PITR للداتابيز. تحديد RPO/RTO.
- نسخة من المفاتيح (في مكان منفصل).

### 16.5 المراقبة والتنبيهات (Detection)
تنبيه فوري على:
- زيادة مفاجئة في 401/403/429/5xx.
- OTP كتير من رقم/IP/دولة.
- `refresh_reuse_detected`.
- تسجيل دخول أدمن من مكان جديد.
- محاولات IDOR (404/403 كتير من نفس المستخدم).
- تغيير رقم/باسورد بكثرة.
- ارتفاع تكلفة SMS.
- اتصالات WS غير عادية.
- فشل jobs أو الـ outbox.
- تغيير إعدادات Cloudflare.
أدوات: Seq/Grafana/Loki + Prometheus + Sentry + Uptime monitor.

### 16.6 Patching
- تحديث .NET و النظام و الـ images شهرياً على الأقل، والثغرات الحرجة فوراً.

---

## 17. الحوادث والامتثال القانوني

### 17.1 خطة الاستجابة للحوادث (Incident Response)
1. **اكتشف** (تنبيه/بلاغ).
2. **احتوِ**: عطّل الحساب/المفتاح، اقفل endpoint، فعّل Under Attack، **غيّر الأسرار**.
3. **افهم**: من اللوجز والـ audit: إيه اللي اتسرب، من امتى، كام مستخدم؟
4. **أصلح** الثغرة وتأكد.
5. **بلّغ**: المستخدمين المتأثرين + الجهات المختصة حسب القانون.
6. **تعلّم**: Post-mortem بدون لوم، وحدّث الملف ده.
- جهّز **قائمة جهات اتصال** و **runbooks** قبل ما تحتاجها، وجرّبها.
- **زر طوارئ**: إلغاء كل الجلسات (revoke all) + تدوير مفاتيح الـ JWT.

### 17.2 الخصوصية والقانون (مصر)
- مصر عندها **قانون حماية البيانات الشخصية (القانون رقم 151 لسنة 2020)** ولائحته. **اسأل محامي مختص** قبل الإطلاق عن: التسجيل/التراخيص، مدة الاحتفاظ، نقل البيانات خارج مصر (لو استضافتك برة)، حقوق المستخدم (الاطلاع، التصحيح، الحذف)، الإخطار بالاختراقات.
- **قلّل البيانات** (لا رقم قومي، لا موقع دقيق محفوظ).
- **الموقع:** استخدمه لحظياً للبحث، وما تخزّنهوش بدقة؛ وما تعرضش موقع لاعب لغيره.
- **سياسة خصوصية وشروط استخدام** واضحة بالعربي.
- **حذف الحساب:** Soft delete فوري + حذف/إخفاء البيانات الشخصية بعد فترة، مع الاحتفاظ بالحد الأدنى المطلوب قانونياً/محاسبياً (الحجوزات والمعاملات بدون بيانات شخصية).
- **الموافقة (Consent)** على الإشعارات والموقع.
- **تصدير بيانات المستخدم** عند الطلب.
- بيانات الدفع: إحنا مش بنخزن بيانات كروت (تحويل يدوي)، لكن لو دخلنا بوابة دفع لاحقاً → **PCI DSS** (استخدم بوابة تدير الكروت ولا تلمس بيانات الكارت أبداً).

---

## 18. ترتيب التنفيذ + Checklist

### المرحلة S0 — قبل أي كود (أسبوع)
- [ ] قرارات: ES256، Argon2id/PBKDF2، Redis، مزود SMS، Cloudflare plan، Key Vault.
- [ ] Threat model (القسم 1) متراجع.
- [ ] ADRs للأمان.

### المرحلة S1 — الأساس (مع Phase 0 و1 في rule.md)
- [ ] HTTPS/HSTS/TLS في كل مكان + Cloudflare Full strict + Origin lock.
- [ ] Secrets management + gitleaks.
- [ ] Fallback authorization policy + ProblemDetails + security headers.
- [ ] JWT ES256 + Access/Refresh + Rotation + Reuse detection + Sessions + Logout-all.
- [ ] OTP (كل خصائص 4.3) + Turnstile + rate limits (Redis).
- [ ] Rate limiting layers (Cloudflare + ASP.NET).
- [ ] Audit log + Correlation ID + Serilog masking.

### المرحلة S2 — الحجز والدفع (Phase 3)
- [ ] Idempotency-Key.
- [ ] DB exclusion constraint + حدود pending.
- [ ] رفع الإيصالات الآمن (القسم 12).
- [ ] IDOR tests لكل endpoint + tenant isolation.
- [ ] Field encryption لبيانات الدفع + blind index.
- [ ] WS ticket + SignalR الآمن (القسم 8).

### المرحلة S3 — الحسابات الحساسة
- [ ] Phone/email change flow (القسم 5) + Step-up.
- [ ] 2FA للملاك، واختياري للأدمن (قرار صاحب المشروع، شوف §3.9).
- [ ] Impersonation آمن.
- [ ] "الأجهزة المسجلة" وإشعارات الدخول.
- [ ] Cloudflare Access للأدمن.

### المرحلة S4 — قبل الإطلاق
- [ ] Pen test (داخلي/خارجي)، ZAP، مراجعة OWASP ASVS L2.
- [ ] Load test + اختبار Rate limit.
- [ ] Backup/Restore drill + IR drill.
- [ ] مراجعة قانونية (قانون 151/2020) + سياسة خصوصية.
- [ ] Bug bounty/قناة إبلاغ ثغرات (`security.txt`).

### Checklist مختصر قبل أي Merge
- [ ] Authorization موجود ومتختبر؟
- [ ] Validation على كل مدخل؟
- [ ] أي بيانات حساسة اتسجلت في لوج؟
- [ ] Rate limit مناسب؟
- [ ] Audit للأفعال الحساسة؟
- [ ] مفيش secrets؟ مفيش مكتبة جديدة غير مراجعة؟
- [ ] Error handling مش بيسرّب تفاصيل؟

---

## 19. قاموس مصطلحات

| المصطلح | معناه |
|---|---|
| **JWT** | توكن موقّع (header.payload.signature)، المحتوى مقروء، التوقيع هو الأمان |
| **Opaque token** | نص عشوائي مالوش معنى، السيرفر بس يعرف يفسره |
| **Rotation** | كل استخدام لـ refresh ينتج واحد جديد ويلغي القديم |
| **Reuse detection** | كشف استخدام توكن اتلغى = احتمال سرقة |
| **Session / sid** | جلسة جهاز واحد |
| **Step-up** | مصادقة إضافية لعملية حساسة |
| **OTP** | كود لمرة واحدة |
| **MFA/2FA** | عاملين تحقق (حاجة تعرفها + حاجة تملكها) |
| **BFF** | سيرفر وسيط للويب بيخبي التوكنات عن المتصفح |
| **CSRF** | خداع المتصفح علشان يبعت cookie لطلب مش بتاعك |
| **XSS** | حقن سكريبت في الصفحة |
| **CORS** | قواعد المتصفح للطلبات بين الدومينات (مش أمان للسيرفر) |
| **CSWSH** | خطف WebSocket من موقع تاني |
| **IDOR/BOLA** | الوصول لبيانات حد تاني بتغيير id |
| **Mass assignment** | العميل يبعت حقول ما كانش مفروض يتحكم فيها |
| **SSRF** | خداع السيرفر يطلب عناوين داخلية |
| **SQLi** | حقن SQL |
| **Rate limiting** | تحديد عدد الطلبات |
| **Throttling** | إبطاء بدل الرفض |
| **Idempotency** | نفس الطلب مرتين = نفس النتيجة |
| **Hash** | بصمة باتجاه واحد |
| **Salt / Pepper** | إضافة عشوائية (مع الـ hash) / سر إضافي (خارج الداتابيز) |
| **HMAC** | بصمة بمفتاح سري |
| **AES-GCM** | تشفير متماثل + تحقق سلامة |
| **Nonce/IV** | قيمة فريدة لكل عملية تشفير |
| **AAD** | بيانات مرتبطة بالتشفير تتحقق منها |
| **KMS / KEK / DEK** | خدمة مفاتيح / مفتاح تشفير المفاتيح / مفتاح تشفير البيانات |
| **Envelope encryption** | تشفير المفاتيح بمفتاح أكبر |
| **Blind index** | HMAC للبحث على بيانات مشفّرة |
| **TLS / HSTS** | تشفير الاتصال / إجبار HTTPS |
| **mTLS** | الطرفين يثبتوا هويتهم بشهادات |
| **WAF** | جدار حماية للتطبيقات |
| **DDoS** | إغراق السيرفر بطلبات |
| **Bot management** | كشف وحجب الروبوتات |
| **Zero Trust / Cloudflare Access** | ما فيش ثقة تلقائية؛ كل دخول يتحقق |
| **Origin** | السيرفر الأصلي اللي ورا Cloudflare |
| **SMS pumping** | هجوم يستنزف فلوس الـ SMS |
| **Audit log** | سجل قرارات غير قابل للتعديل |
| **RPO / RTO** | أقصى فقد بيانات مسموح / أقصى زمن استرجاع |
| **SAST/DAST/SCA** | فحص الكود / فحص التطبيق شغّال / فحص المكتبات |
| **OWASP ASVS** | قائمة معايير أمان التطبيقات |
| **Pen test** | اختبار اختراق |
| **PII** | بيانات شخصية |

---

### مراجع للاستزادة
- OWASP Top 10, **OWASP API Security Top 10**, OWASP ASVS, OWASP Cheat Sheet Series (Authentication, Session, JWT, CSRF, Password Storage, Cryptographic Storage, Rate limiting, WebSocket).
- Microsoft Docs: ASP.NET Core Security, Rate limiting middleware, SignalR security, Data Protection.
- Cloudflare Docs: SSL/TLS Full (strict), WAF, Rate limiting rules, Turnstile, Access, Tunnel, Authenticated Origin Pulls.

> **تذكير:** الأرقام (مدد، حدود) نقطة بداية معقولة، مش قدر. راقب الاستخدام الحقيقي وظبطها.
