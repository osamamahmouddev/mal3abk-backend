"use client";

import React, { useState } from "react";
import { useLandingState } from "./LandingState";

const toArabicDigits = (v: string) => v.replace(/[0-9]/g, (d) => "٠١٢٣٤٥٦٧٨٩"[Number(d)]);

export function JoinForm() {
  const { audience, setAudience, plan } = useLandingState();
  const [phone, setPhone] = useState("");
  const [phoneErr, setPhoneErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [joined, setJoined] = useState(false);

  const isVenue = audience === "venue";
  const digits = phone.replace(/\D/g, "");
  const phoneOk = /^01[0125]\d{8}$/.test(digits);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!phoneOk) {
      setPhoneErr("اكتب رقم موبايل مصري صح (١١ رقم يبدأ بـ 01).");
      return;
    }

    setLoading(true);
    setPhoneErr(null);

    try {
      const res = await fetch("/api/waitlist", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          phone: digits,
          audience,
          chosenPlan: isVenue ? plan : null,
        }),
      });

      if (!res.ok) {
        const errorData = await res.json().catch(() => ({}));
        const detail = errorData.detail || "تعذر التسجيل في قائمة الانتظار، يرجى المحاولة لاحقاً.";
        setPhoneErr(detail);
        return;
      }

      setJoined(true);
    } catch {
      setPhoneErr("حدث خطأ أثناء الاتصال بالخادم. يرجى المحاولة لاحقاً.");
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="join">
      <div style={{ display: "flex", gap: 6 }} role="group" aria-label="أنا">
        {(
          [
            ["player", "أنا لاعب"],
            ["venue", "عندي ملعب"],
          ] as const
        ).map(([id, label]) => (
          <button key={id} type="button" className="seg on-dark" aria-pressed={audience === id} onClick={() => setAudience(id)}>
            {label}
          </button>
        ))}
      </div>

      {!joined ? (
        <form onSubmit={submit} noValidate>
          <div className="field">
            <input
              aria-label="رقم الموبايل"
              inputMode="tel"
              autoComplete="tel"
              dir="ltr"
              value={phone}
              onChange={(e) => {
                setPhone(e.target.value);
                setPhoneErr(null);
              }}
              placeholder="01x xxxx xxxx"
              aria-invalid={!!phoneErr}
              aria-describedby={phoneErr ? "phone-err" : undefined}
              disabled={loading}
            />
            <button type="submit" className="btn" disabled={loading}>
              {loading ? "جارٍ التسجيل..." : isVenue ? "سجّل ملعبي" : "بلّغني"}
            </button>
          </div>
          {phoneErr && (
            <span id="phone-err" role="alert" className="join-error">
              {phoneErr}
            </span>
          )}
        </form>
      ) : (
        <div role="status" className="join-done">
          <span className="join-done-check" aria-hidden="true">
            ✓
          </span>
          <span className="join-done-copy">
            <b>{isVenue ? "تمام، فريقنا هيكلمك" : "اتسجلت!"}</b>
            <br />
            <span>
              {isVenue
                ? `هنتصل بيك خلال يومين نساعدك تجهّز ملعبك${plan ? ` على باقة ${plan}` : ""}.`
                : `هنبعتلك رسالة على ${toArabicDigits(digits.replace(/^(\d{3})(\d{4})(\d{4})$/, "$1 $2 $3"))} يوم الإطلاق.`}
            </span>
          </span>
          <button
            type="button"
            className="link-btn"
            onClick={() => {
              setJoined(false);
              setPhone("");
            }}
          >
            رقم تاني
          </button>
        </div>
      )}
      <span className="join-note">مش هنبعت إعلانات. رسالة واحدة يوم الإطلاق.</span>
    </div>
  );
}
