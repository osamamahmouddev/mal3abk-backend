import type { Metadata } from "next";
import Link from "next/link";
import { ArrowRight } from "lucide-react";

export const metadata: Metadata = {
  title: "الشروط والأحكام — ملعبك",
  description: "شروط وأحكام استخدام منصة ملعبك لحجز وإدارة الملاعب الرياضية في جمهورية مصر العربية",
};

export default function TermsPage() {
  return (
    <main className="wrap" style={{ maxWidth: 860, margin: "40px auto", padding: "0 20px", lineHeight: 1.8 }}>
      <div style={{ marginBottom: 24 }}>
        <Link href="/" className="link-btn" style={{ fontSize: 14, display: "inline-flex", alignItems: "center", gap: 6 }}>
          <ArrowRight size={16} aria-hidden="true" />
          العودة للرئيسية
        </Link>
      </div>

      <h1 style={{ fontSize: 32, marginBottom: 8, color: "var(--navy)" }}>الشروط والأحكام</h1>
      <p className="muted" style={{ fontSize: 14, marginBottom: 32 }}>
        آخر تحديث: نوفمبر ٢٠٢٦ — تسري هذه الشروط على جميع مستخدمي منصة ملعبك
      </p>

      <section style={{ marginBottom: 32 }}>
        <h2 style={{ fontSize: 20, marginBottom: 12 }}>١. نبذة عن المنصة وطبيعة الخدمة</h2>
        <p>
          منصة <b>ملعبك</b> هي وسيط تقني يربط بين لاعبي كرة القدم والرياضيين في مصر وبين أصحاب الملاعب وإداراتها،
          لتسهيل استعراض المواعيد المتاحة، حجز الفترات، وسداد مبالغ التأمين (العربون) بطرق دفع إلكترونية معتمدة.
        </p>
      </section>

      <section style={{ marginBottom: 32 }}>
        <h2 style={{ fontSize: 20, marginBottom: 12 }}>٢. قواعد الحجز وتأكيد العربون</h2>
        <ul>
          <li>يتم حجز الميعاد مبدئياً لفترة زمنية محددة (نافذة رفع العربون) تتيح للاعب إتمام التحويل ورفع إيصال الدفع.</li>
          <li>في حال عدم رفع إيصال الدفع خلال المهلة المحددة، يُلغى الحجز المؤقت تلقائياً ويُتاح الميعاد للاعبين الآخرين.</li>
          <li>يخضع الإيصال لمراجعة وتأكيد إدارة الملعب وفقاً لسياسات الحجز المحددة لكل ملعب.</li>
        </ul>
      </section>

      <section style={{ marginBottom: 32 }}>
        <h2 style={{ fontSize: 20, marginBottom: 12 }}>٣. سياسة الإلغاء واسترداد العربون</h2>
        <p>
          تحدد كل إدارة ملعب مهلة الإلغاء المسموح بها قبل موعد المباراة (مثلاً ٢٤ ساعة أو ١٢ ساعة قبل الميعاد).
          في حال الإلغاء قبل انتهاء المهلة المسموحة، يُعاد العربون وفقاً لسياسة الملعب الموضحة في شاشة الحجز.
        </p>
      </section>

      <section style={{ marginBottom: 32 }}>
        <h2 style={{ fontSize: 20, marginBottom: 12 }}>٤. التزامات أصحاب الملاعب</h2>
        <ul>
          <li>الالتزام بتوفير الملعب المحجوز في الميعاد المحدد بالحالة والمرافق المعلنة في المنصة.</li>
          <li>تحديث المواعيد وجداول الصيانة الدورية لمنع أي تعارض في الحجوزات.</li>
          <li>التعامل بنزاهة وسرعة في مراجعة إيصالات العربون والرد على حجوزات اللاعبين.</li>
        </ul>
      </section>

      <section style={{ marginBottom: 32 }}>
        <h2 style={{ fontSize: 20, marginBottom: 12 }}>٥. الاستخدام المقبول والحظر</h2>
        <p>
          يُحظر تماماً محاولة حجز فترات وهمية، أو رفع إيصالات دفع مزورة أو معدلة، أو استخدام برامج آلية (Bots) لاستخراج البيانات،
          ويؤدي أي انتهاك إلى الإيقاف الفوري للحساب واتخاذ الإجراءات القانونية اللازمة.
        </p>
      </section>

      <section style={{ marginBottom: 40 }}>
        <h2 style={{ fontSize: 20, marginBottom: 12 }}>٦. القانون الواجب التطبيق</h2>
        <p>
          تخضع هذه الشروط والأحكام وتُفسر وفقاً للقوانين السارية في <b>جمهورية مصر العربية</b>،
          وتختص محاكم القاهرة بنظر أي نزاع قد ينشأ عن استخدام المنصة.
        </p>
      </section>
    </main>
  );
}
