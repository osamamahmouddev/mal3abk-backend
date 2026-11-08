import Image from "next/image";
import { Faq } from "@/components/Faq";
import { FAQ_ITEMS } from "@/components/faqItems";
import { JoinForm } from "@/components/JoinForm";
import { LandingStateProvider } from "@/components/LandingState";
import { Plans } from "@/components/Plans";

const toArabicDigits = (v: string | number) => String(v).replace(/[0-9]/g, (d) => "٠١٢٣٤٥٦٧٨٩"[Number(d)]);

const STEPS: [string, string][] = [
  ["دوّر", "اختار المنطقة والميعاد ونوع الملعب. هتشوف الملاعب القريبة والمواعيد الفاضية فعلاً."],
  ["احجز وادفع العربون", "اختار الساعة، حوّل العربون، وارفع صورة الإيصال. الميعاد بيتمسك لك وقت المراجعة."],
  ["انزل العب", "الملعب يأكد، يوصلك إشعار، وفريقك كله يشوف الحجز. باقي المبلغ في الملعب."],
];

const BEFORE = [
  "مكالمات ورسايل واتساب لحد ما تلاقي ميعاد",
  "مش عارف السعر غير لما تسأل",
  "العربون بيتحوّل ومحدش بيأكد",
  'الشلة كلها بتسأل "اتحجز ولا لأ؟"',
];
const AFTER = [
  "المواعيد الفاضية قدامك لايف",
  "السعر والعربون والسياسة واضحين من الأول",
  "تأكيد من الملعب وإشعار ليك",
  "الحجز ظاهر للفريق كله",
];

const ICONS = {
  pay: "M3 7h18v10H3zM3 11h18M7 15h3",
  team: "M16 19v-1a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v1M9 10a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM22 19v-1a4 4 0 0 0-3-3.9M16 4.1a3 3 0 0 1 0 5.8",
  bolt: "M13 2 4 14h7l-1 8 9-12h-7z",
  bell: "M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9M10 21a2 2 0 0 0 4 0",
};
// icon, title, body, bg, fg
const FEATURES: [keyof typeof ICONS, string, string, string, string][] = [
  ["pay", "عربون بس، والباقي في الملعب", "ادفع العربون بفودافون كاش أو إنستاباي وارفع الإثبات. الملعب يأكد وانت مكانك.", "#FDECEA", "#D63B2F"],
  ["bolt", "مواعيد فاضية لايف", 'اللي بتشوفه فاضي هو فعلاً فاضي. مفيش حجز مكرر ولا "معلش اتحجز".', "#FEF3D6", "#7A4D00"],
  ["team", "فريقك في مكان واحد", "كابتن ولاعيبة وكود دعوة. الحجز بيتشاف للفريق كله.", "#E5F5EB", "#1B6E3E"],
  ["bell", "تذكير قبل الماتش", "إشعار قبل الماتش بساعتين، وقبل ما مهلة العربون تخلص.", "#EEF0F5", "#0F1B3D"],
];

// position, team, initials, record (W-L-D), points
const RANKS: [string, string, string, string, string][] = [
  ["١", "المعادي يونايتد", "مع", "18-2-3", "٥٦"],
  ["٢", "الصقور", "صق", "16-4-2", "٥٢"],
  ["٣", "ستاد الشباب FC", "شب", "15-3-5", "٤٨"],
  ["٤", "فريق النجوم", "نج", "14-3-5", "٤٥"],
];

type CalKind = "ok" | "warn" | "wait" | "";
const CAL: [string, [CalKind, string][]][] = [
  ["٧ م", [["ok", "كريم"], ["", ""], ["ok", "م. فتحي"]]],
  ["٨ م", [["warn", "أحمد سامي"], ["ok", "الصقور"], ["", ""]]],
  ["٩ م", [["wait", "يوسف"], ["", ""], ["ok", "الدلتا"]]],
  ["١٠ م", [["ok", "دوري"], ["ok", "الدلتا"], ["", ""]]],
  ["١١ م", [["", ""], ["warn", "عمر خالد"], ["", ""]]],
  ["١٢ ص", [["ok", "الصقور"], ["", ""], ["wait", "زياد"]]],
  ["١ ص", [["", ""], ["ok", "هاني"], ["", ""]]],
];

const VENUE_PERKS: [string, string][] = [
  ["مراجعة العربون بضغطة", "الإيصال بيوصلك مع المبلغ والرقم والميعاد. أكد أو ارفض بسبب."],
  ["مفيش حجز مكرر", "الحجز اليدوي والتطبيق على نفس التقويم، والتعارض بيتمنع أوتوماتيك."],
  ["أسعار ذكية", "سعر للذروة وآخر الأسبوع والأجازات، وتواريخ خاصة للماتشات الكبيرة."],
  ["أرقامك الحقيقية", "قيمة الحجوزات، العربون المستلم، والمتبقي — من غير دفتر."],
];

const faqJsonLd = {
  "@context": "https://schema.org",
  "@type": "FAQPage",
  mainEntity: FAQ_ITEMS.map(([q, a]) => ({
    "@type": "Question",
    name: q,
    acceptedAnswer: { "@type": "Answer", text: a },
  })),
};

export default function LandingPage() {
  return (
    <LandingStateProvider>
      <div className="lp">
        <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: JSON.stringify(faqJsonLd) }} />

        {/* ---------------------------------- Hero ---------------------------------- */}
        <header className="hero">
          <div className="wrap">
            <nav className="nav" aria-label="الرئيسية">
              <a href="#" className="brand" aria-label="ملعبك — الرئيسية">
                <Image
                  src="/images/logo-horizontal-darkbg.svg"
                  alt="ملعبك"
                  width={150}
                  height={54}
                  className="h-10 w-auto object-contain"
                  priority
                />
              </a>
              <div className="nav-links">
                <a href="#how">إزاي بيشتغل</a>
                <a href="#teams">الفرق والتحديات</a>
                <a href="#venues">لأصحاب الملاعب</a>
                <a href="#faq">أسئلة</a>
              </div>
              <a href="#venues" className="nav-venue hide-m">
                سجّل ملعبك
              </a>
              <a href="#join" className="nav-cta">
                نزّل التطبيق
              </a>
            </nav>

            <div className="hero-grid">
              <div className="hero-copy">
                <span className="hero-badge">بنبدأ في القاهرة والجيزة</span>
                <h1 className="h1">
                  احجز ملعبك في دقيقة.
                  <br />
                  واتحدى <em>أي فريق</em> في مصر.
                </h1>
                <p className="lead">
                  شوف المواعيد الفاضية في الملاعب اللي جنبك، ادفع العربون بفودافون كاش أو إنستاباي، وخلّي فريقك كله في مكان واحد. من غير ولا
                  مكالمة.
                </p>
                <div className="stores">
                  <a href="#join" className="store primary">
                    <svg width="26" height="26" viewBox="0 0 24 24" fill="#FFFFFF" aria-hidden="true">
                      <path d="M16.4 12.6c0-2.3 1.9-3.4 2-3.5-1.1-1.6-2.8-1.8-3.4-1.8-1.4-.2-2.8.8-3.5.8-.7 0-1.8-.8-3-.8-1.5 0-3 .9-3.8 2.3-1.6 2.8-.4 7 1.2 9.3.8 1.1 1.7 2.4 2.9 2.3 1.2 0 1.6-.7 3-.7s1.8.7 3 .7c1.3 0 2.1-1.1 2.8-2.3.9-1.3 1.3-2.6 1.3-2.6s-2.5-1-2.5-3.7zM14.2 5.8c.6-.8 1.1-1.9 1-3-.9 0-2.1.6-2.7 1.4-.6.7-1.1 1.8-1 2.9 1 .1 2.1-.5 2.7-1.3z" />
                    </svg>
                    <span>
                      <small>قريباً على</small>
                      <b>App Store</b>
                    </span>
                  </a>
                  <a href="#join" className="store">
                    <svg width="24" height="24" viewBox="0 0 24 24" aria-hidden="true">
                      <path d="M4 2.8 13.6 12 4 21.2c-.3-.2-.5-.6-.5-1V3.8c0-.4.2-.8.5-1z" fill="#2EC4B6" />
                      <path d="m16.8 8.8-3.2 3.2L4 2.8c.2-.1.5-.1.8 0l12 6z" fill="#F5B62B" />
                      <path d="m16.8 15.2-12 6c-.3.1-.6.1-.8 0l9.6-9.2 3.2 3.2z" fill="#E8574A" />
                      <path d="M20.2 11c.7.4.7 1.6 0 2l-3.4 2.2-3.2-3.2 3.2-3.2 3.4 2.2z" fill="#FFFFFF" />
                    </svg>
                    <span>
                      <small>قريباً على</small>
                      <b>Google Play</b>
                    </span>
                  </a>
                </div>
                <div className="hero-stats">
                  <span>
                    <b>٣ خطوات</b>
                    <small>من البحث للتأكيد</small>
                  </span>
                  <span>
                    <b>٠ مكالمات</b>
                    <small>كل حاجة من التطبيق</small>
                  </span>
                  <span>
                    <b>عربون بس</b>
                    <small>والباقي في الملعب</small>
                  </span>
                </div>
              </div>

              <div className="hero-visual" aria-hidden="true">
                <PhoneMockup />
                <div className="float r">
                  <span
                    style={{ width: 36, height: 36, borderRadius: 12, background: "#E5F5EB", color: "#1B6E3E", display: "flex", alignItems: "center", justifyContent: "center" }}
                  >
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3">
                      <path d="m5 12 5 5L20 7" />
                    </svg>
                  </span>
                  <span>
                    <b>العربون اتأكد</b>
                    <small>٥٠ ج.م · فودافون كاش</small>
                  </span>
                </div>
                <div className="float l">
                  <span className="avatar" style={{ width: 36, height: 36, borderRadius: 18, background: "#D63B2F", fontSize: 13 }}>
                    صق
                  </span>
                  <span>
                    <b>الصقور بيتحدّوكم</b>
                    <small>الجمعة ٩ م · ملعب ٢</small>
                  </span>
                  <span className="pill" style={{ background: "#0F1B3D", color: "#F5B62B" }}>
                    قبول
                  </span>
                </div>
              </div>
            </div>
          </div>
        </header>

        <main>
          {/* ------------------------------ How it works ------------------------------ */}
          <section className="sec" id="how">
            <div className="wrap center">
              <span className="eyebrow">إزاي بيشتغل</span>
              <h2 className="h2">من &quot;فين نلعب؟&quot; لـ &quot;متأكد&quot; في ٣ خطوات</h2>
              <p className="sub">مفيش مكالمات، ولا &quot;هكلمك وأرد عليك&quot;، ولا ميعاد يطير منك في آخر لحظة.</p>
              <div className="grid3">
                {STEPS.map(([t, d], i) => (
                  <div key={t} className="card">
                    <span className="num">{toArabicDigits(i + 1)}</span>
                    <h3>{t}</h3>
                    <p>{d}</p>
                  </div>
                ))}
              </div>
            </div>
          </section>

          {/* ------------------------------ Before/after ------------------------------ */}
          <section className="sec flush">
            <div className="wrap split">
              <div>
                <span className="eyebrow">ليه ملعبك؟</span>
                <h2 className="h2">الحجز زمان كان ماتش لوحده</h2>
                <p className="sub">
                  رنّة على صاحب الملعب، استنى يرد، &quot;الميعاد ده محجوز&quot;، دوّر على تاني، حوّل العربون واستنى حد يأكد… خلّصنا الحتة دي.
                </p>
              </div>
              <div className="compare">
                <div className="before">
                  <b>قبل</b>
                  {BEFORE.map((b) => (
                    <div key={b} className="li">
                      <span aria-hidden="true">✕</span>
                      <span>{b}</span>
                    </div>
                  ))}
                </div>
                <div className="after">
                  <b>مع ملعبك</b>
                  {AFTER.map((b) => (
                    <div key={b} className="li">
                      <span aria-hidden="true">✓</span>
                      <span>{b}</span>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </section>

          {/* -------------------------------- Features -------------------------------- */}
          <section className="sec flush">
            <div className="wrap center">
              <span className="eyebrow">كل اللي محتاجه</span>
              <h2 className="h2">معمول للي بيلعب كل أسبوع</h2>
              <div className="grid4">
                {FEATURES.map(([icon, t, d, bg, fg]) => (
                  <div key={t} className="card feature">
                    <span className="ico" style={{ background: bg, color: fg }}>
                      <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                        <path d={ICONS[icon]} />
                      </svg>
                    </span>
                    <h3>{t}</h3>
                    <p>{d}</p>
                  </div>
                ))}
              </div>
            </div>
          </section>

          {/* --------------------------------- Teams ---------------------------------- */}
          <section className="sec dark" id="teams">
            <div className="wrap split">
              <div>
                <span className="eyebrow">الفرق والتحديات</span>
                <h2 className="h2">
                  فريقك ليه بيت.
                  <br />
                  وليه ترتيب.
                </h2>
                <p className="sub">
                  اعمل فريق، ابعت كود الدعوة للشلة، واتحدى أي فريق تاني على ملعب وميعاد. النتيجة بتتأكد من الفريقين، والترتيب بيتحدّث لوحده.
                </p>
                <div className="chips">
                  <span>
                    كود دعوة <b dir="ltr">NJM-4827</b>
                  </span>
                  <span>تأكيد النتيجة من الفريقين</span>
                  <span>ترتيب لكل منطقة</span>
                </div>
              </div>
              <div className="stack">
                <div className="ranking">
                  <div className="ranking-head">
                    <b style={{ fontSize: 17 }}>ترتيب مدينة نصر</b>
                    <span className="pill" style={{ background: "#FEF3D6", color: "#7A4D00" }}>
                      الموسم ده
                    </span>
                  </div>
                  {RANKS.map(([pos, name, ini, rec, pts], i) => (
                    <div key={name} className={`rank-row${i === 3 ? " me" : ""}`}>
                      <span className="rank-pos">{pos}</span>
                      <span className="avatar">{ini}</span>
                      <b className="rank-name">{name}</b>
                      <span className="rank-rec" dir="ltr">
                        {toArabicDigits(rec)}
                      </span>
                      <b className="rank-pts">{pts}</b>
                    </div>
                  ))}
                </div>
                <div className="challenge">
                  <span className="avatar">نج</span>
                  <span className="challenge-copy">
                    <b>تحدي جديد من الصقور</b>
                    <small>الجمعة ٩ م · ملعب النجوم — ملعب ٢</small>
                  </span>
                  <span className="challenge-accept">قبول</span>
                </div>
              </div>
            </div>
          </section>

          {/* --------------------------------- Venues --------------------------------- */}
          <section className="sec" id="venues">
            <div className="wrap">
              <div className="center">
                <span className="eyebrow">لأصحاب الملاعب</span>
                <h2 className="h2">ملعبك مليان، والتليفون ساكت</h2>
                <p className="sub">
                  لوحة تحكم كاملة: تقويم لكل الملاعب، مراجعة إثبات العربون بضغطة، أسعار للذروة والويك إند، وتقارير بالإيراد الحقيقي.
                </p>
              </div>
              <div className="split" style={{ marginTop: 48, alignItems: "stretch" }}>
                <div className="dash" aria-hidden="true">
                  <div className="dash-side">
                    <b style={{ fontSize: 16, marginBottom: 8 }}>
                      ملعبك{" "}
                      <span className="pill" style={{ background: "#F5B62B", color: "#0F1B3D", fontSize: 9 }}>
                        للملاعب
                      </span>
                    </b>
                    <span>نظرة عامة</span>
                    <span className="on">تقويم الحجوزات</span>
                    <span>
                      الحجوزات{" "}
                      <span className="pill" style={{ background: "#D63B2F", color: "#FFFFFF" }}>
                        ٥
                      </span>
                    </span>
                    <span>الأسعار</span>
                    <span>التقارير</span>
                  </div>
                  <div className="dash-main">
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                      <b style={{ fontSize: 16 }}>السبت ٢٦ يوليو</b>
                      <span className="muted" style={{ fontSize: 11 }}>
                        ٣ ملاعب
                      </span>
                    </div>
                    <div className="dash-cal">
                      <span />
                      <b style={{ textAlign: "center" }}>ملعب ١</b>
                      <b style={{ textAlign: "center" }}>ملعب ٢</b>
                      <b style={{ textAlign: "center" }}>ملعب ٣</b>
                      {CAL.flatMap(([t, cells]) => [
                        <span key={t} className="t">
                          {t}
                        </span>,
                        ...cells.map(([kind, label], j) => (
                          <span key={`${t}-${j}`} className={`c ${kind}`}>
                            {label}
                          </span>
                        )),
                      ])}
                    </div>
                    <div className="dash-alert">
                      <b style={{ flex: 1 }}>إثبات عربون جديد — عمر خالد · ٧٠ ج.م</b>
                      <span style={{ background: "#0F1B3D", color: "#FFFFFF", borderRadius: 8, padding: "4px 10px", fontWeight: 700 }}>راجع</span>
                    </div>
                  </div>
                </div>
                <div className="stack" style={{ gap: 12 }}>
                  {VENUE_PERKS.map(([t, d]) => (
                    <div key={t} className="card perk">
                      <span>
                        <b>{t}</b>
                        <p>{d}</p>
                      </span>
                    </div>
                  ))}
                </div>
              </div>

              <Plans />
            </div>
          </section>

          {/* ---------------------------------- FAQ ----------------------------------- */}
          <section className="sec flush" id="faq">
            <div className="wrap center">
              <span className="eyebrow">أسئلة</span>
              <h2 className="h2">أسئلة بتتسأل كتير</h2>
              <Faq />
            </div>
          </section>

          {/* ---------------------------------- Join ---------------------------------- */}
          <section className="sec flush" id="join">
            <div className="wrap">
              <div className="cta">
                <div>
                  <h2 className="h2" style={{ marginTop: 0 }}>
                    أول ما نفتح، هتعرف قبل أي حد.
                  </h2>
                  <p className="sub">سيب رقمك وهنبعتلك رسالة يوم الإطلاق، ومعاها أول حجز من غير رسوم خدمة.</p>
                </div>
                <div>
                  <JoinForm />
                </div>
              </div>
            </div>
          </section>
        </main>

        <div className="wrap">
          <footer className="foot">
            <span>
              <Image
                src="/images/logo-horizontal-darkbg.svg"
                alt="ملعبك"
                width={130}
                height={46}
                className="h-8 w-auto object-contain mb-2"
              />
              <span>احجز ملعبك في دقيقة. معمول في مصر للي بيلعب كورة.</span>
            </span>
            <span>
              <b>للاعيبة</b>
              <a href="#how">إزاي بيشتغل</a>
              <a href="#teams">الفرق والتحديات</a>
            </span>
            <span>
              <b>للملاعب</b>
              <a href="#venues">لوحة التحكم</a>
              <a href="#venues">الباقات</a>
            </span>
            <span>
              <b>مساعدة</b>
              <a href="#faq">أسئلة</a>
              <a href="#join">كلّمنا</a>
            </span>
            <span>
              <b>قانوني</b>
              <a href="/privacy">سياسة الخصوصية</a>
              <a href="/terms">الشروط والأحكام</a>
            </span>
          </footer>
        </div>
      </div>
    </LandingStateProvider>
  );
}

/** Decorative phone showing the player app's home screen. */
function PhoneMockup() {
  const slot = { fontSize: 11, fontWeight: 600, padding: "6px 10px", borderRadius: 10, background: "#FFFFFF", border: "1px solid #DCE0E8" } as const;
  return (
    <div className="phone">
      <div className="screen">
        <div style={{ background: "#0F1B3D", color: "#FFFFFF", padding: "26px 16px 18px", display: "flex", flexDirection: "column", gap: 12 }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
            <span style={{ display: "flex", flexDirection: "column" }}>
              <span style={{ fontSize: 11, color: "#AEB7CA" }}>مساء الخير</span>
              <b style={{ fontSize: 16 }}>أحمد سامي</b>
            </span>
            <span className="avatar" style={{ background: "#D63B2F", fontSize: 13 }}>
              أس
            </span>
          </div>
          <div style={{ background: "rgba(255,255,255,.1)", borderRadius: 14, padding: "10px 12px", fontSize: 12, display: "flex", gap: 8, alignItems: "center" }}>
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#AEB7CA" strokeWidth="2.4">
              <circle cx="11" cy="11" r="7" />
              <path d="m20 20-3.5-3.5" />
            </svg>
            <span style={{ color: "#C9D1E3" }}>ملعب خماسي · النهارده · ٨ م</span>
          </div>
        </div>
        <div style={{ padding: 14, display: "flex", flexDirection: "column", gap: 10 }}>
          <b style={{ fontSize: 13 }}>حجزك الجاي</b>
          <div style={{ background: "#FFFFFF", borderRadius: 16, padding: 10, display: "flex", gap: 10, alignItems: "center", boxShadow: "0 8px 18px -12px rgba(15,27,61,.3)" }}>
            <span className="pitch" style={{ width: 52, height: 52, borderRadius: 12, flexShrink: 0 }} />
            <span style={{ flex: 1, display: "flex", flexDirection: "column", gap: 2 }}>
              <b style={{ fontSize: 12 }}>ملعب النجوم — ملعب ١</b>
              <span style={{ fontSize: 10, color: "#5B6475" }}>السبت ٢٦ يوليو · ٨ م</span>
            </span>
            <span className="pill" style={{ background: "#E5F5EB", color: "#1B6E3E" }}>
              مأكد
            </span>
          </div>
          <b style={{ fontSize: 13, marginTop: 4 }}>ملاعب قريبة منك</b>
          <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 8 }}>
            {[
              ["ملاعب الرواد", "★ ٤٫٦ · ١٫٢ كم"],
              ["ستاد الشباب", "★ ٤٫٩ · ٣ كم"],
            ].map(([n, m]) => (
              <div key={n} style={{ background: "#FFFFFF", borderRadius: 14, overflow: "hidden" }}>
                <div className="pitch" style={{ height: 64 }} />
                <div style={{ padding: "7px 8px", display: "flex", flexDirection: "column", gap: 1 }}>
                  <b style={{ fontSize: 11 }}>{n}</b>
                  <span style={{ fontSize: 10, color: "#5B6475" }}>{m}</span>
                </div>
              </div>
            ))}
          </div>
          <b style={{ fontSize: 13, marginTop: 4 }}>مواعيد فاضية النهارده</b>
          <div style={{ display: "flex", gap: 6, flexWrap: "wrap" }}>
            <span style={{ ...slot, fontWeight: 700, background: "#0F1B3D", color: "#FFFFFF", border: "none" }}>٨ م</span>
            <span style={slot}>٩ م</span>
            <span style={slot}>١١ م</span>
            <span style={slot}>١٢ ص</span>
          </div>
          <div style={{ background: "#FEF8EA", borderRadius: 14, padding: "9px 10px", display: "flex", gap: 8, alignItems: "center" }}>
            <span className="avatar" style={{ width: 28, height: 28, borderRadius: 14, background: "#D63B2F", fontSize: 10 }}>
              نج
            </span>
            <span style={{ flex: 1, display: "flex", flexDirection: "column" }}>
              <b style={{ fontSize: 11 }}>فريق النجوم · المركز ٤</b>
              <span style={{ fontSize: 10, color: "#7A4D00" }}>٨ لاعيبة · الماتش الجاي السبت</span>
            </span>
          </div>
        </div>
        <div
          style={{
            margin: "auto 10px 10px",
            background: "#FFFFFF",
            borderRadius: 20,
            padding: 8,
            display: "flex",
            justifyContent: "space-around",
            fontSize: 10,
            color: "#5B6475",
            boxShadow: "0 10px 24px -12px rgba(15,27,61,.3)",
          }}
        >
          <span style={{ background: "#0F1B3D", color: "#FFFFFF", borderRadius: 12, padding: "6px 10px", fontWeight: 700 }}>الرئيسية</span>
          <span style={{ padding: "6px 4px" }}>استكشف</span>
          <span style={{ padding: "6px 4px" }}>حجوزاتي</span>
          <span style={{ padding: "6px 4px" }}>فرقي</span>
        </div>
      </div>
    </div>
  );
}
