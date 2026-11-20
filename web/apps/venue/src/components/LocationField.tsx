"use client";

import React, { useState } from "react";
import { ExternalLink, Loader2, LocateFixed, MapPin } from "lucide-react";

export interface LatLng {
  lat: string;
  lng: string;
}

/** Egypt with a margin: a point outside it is almost always swapped or mistyped coordinates. */
const inEgypt = (lat: number, lng: number) => lat >= 21.5 && lat <= 32 && lng >= 24.5 && lng <= 37.5;

/**
 * Pulls coordinates out of whatever the owner pastes: plain "30.04, 31.23", or a Google Maps / Apple Maps / OSM link
 * (`@lat,lng`, `q=lat,lng`, `!3dlat!4dlng`, `ll=`, `mlat=…&mlon=…`). Short links (maps.app.goo.gl) carry no
 * coordinates, so they return null and the owner is asked for the full link or the numbers.
 */
export function parseCoordinates(text: string): { lat: number; lng: number } | null {
  const t = text.trim();
  const num = "(-?\\d{1,3}(?:\\.\\d+)?)";
  const patterns = [
    new RegExp(`!3d${num}!4d${num}`),
    new RegExp(`@${num},${num}`),
    new RegExp(`[?&](?:q|query|ll|destination|center)=${num},\\s*${num}`),
    new RegExp(`mlat=${num}&mlon=${num}`),
    new RegExp(`^${num}\\s*[,،\\s]\\s*${num}$`),
  ];
  let decoded = t;
  try {
    decoded = decodeURIComponent(t);
  } catch {
    // A stray % in pasted text: match against it as typed.
  }
  for (const p of patterns) {
    const m = decoded.match(p);
    if (m) {
      const lat = Number(m[1]);
      const lng = Number(m[2]);
      if (Math.abs(lat) <= 90 && Math.abs(lng) <= 180) return { lat, lng };
    }
  }
  return null;
}

/** Venue location for the players' map: paste a maps link, use this device's location, or type the numbers. */
export function LocationField({
  value,
  onChange,
  fieldClass,
  required,
}: {
  value: LatLng;
  onChange: (value: LatLng) => void;
  fieldClass: string;
  required?: boolean;
}) {
  const [paste, setPaste] = useState("");
  const [note, setNote] = useState<{ tone: "ok" | "bad"; text: string } | null>(null);
  const [locating, setLocating] = useState(false);

  const lat = Number(value.lat);
  const lng = Number(value.lng);
  const hasPoint = value.lat.trim() !== "" && value.lng.trim() !== "" && Number.isFinite(lat) && Number.isFinite(lng);
  const outside = hasPoint && !inEgypt(lat, lng);

  const set = (p: { lat: number; lng: number }) => onChange({ lat: p.lat.toFixed(6), lng: p.lng.toFixed(6) });

  const onPaste = (text: string) => {
    setPaste(text);
    if (!text.trim()) return setNote(null);
    const p = parseCoordinates(text);
    if (p) {
      set(p);
      setNote({ tone: "ok", text: "تمام، أخدنا الموقع من اللينك." });
    } else if (/goo\.gl|maps\.app/i.test(text)) {
      setNote({ tone: "bad", text: "اللينك المختصر مفيهوش الإحداثيات. افتحه في المتصفح وانسخ اللينك الطويل، أو اكتب الأرقام تحت." });
    } else {
      setNote({ tone: "bad", text: "مش لاقيين إحداثيات في اللي اتكتب. الصق لينك جوجل ماب أو اكتب الأرقام تحت." });
    }
  };

  const locate = () => {
    if (!("geolocation" in navigator)) return setNote({ tone: "bad", text: "المتصفح ده مش بيدعم تحديد الموقع." });
    setLocating(true);
    navigator.geolocation.getCurrentPosition(
      (pos) => {
        set({ lat: pos.coords.latitude, lng: pos.coords.longitude });
        setNote({ tone: "ok", text: "أخدنا موقعك الحالي. اتأكد إنك واقف في الملعب نفسه." });
        setLocating(false);
      },
      () => {
        setNote({ tone: "bad", text: "مقدرناش نوصل لموقعك. اسمح للمتصفح بالموقع، أو الصق لينك جوجل ماب." });
        setLocating(false);
      },
      { enableHighAccuracy: true, timeout: 15000 },
    );
  };

  const d = 0.004;
  const embed = hasPoint
    ? `https://www.openstreetmap.org/export/embed.html?bbox=${lng - d},${lat - d},${lng + d},${lat + d}&layer=mapnik&marker=${lat},${lng}`
    : null;

  return (
    <fieldset className="flex flex-col gap-2.5 rounded-2xl border border-[#E4E7EC] bg-[#FAFBFC] p-3.5">
      <legend className="px-1 text-sm font-semibold flex items-center gap-1.5">
        <MapPin className="size-4 text-brand-red" />
        موقع الملعب على الخريطة {required && <span className="text-brand-red">*</span>}
      </legend>
      <span className="text-xs text-[#5B6475] -mt-1">عشان يظهر للاعيبة في "الملاعب القريبة مني". افتح الملعب على جوجل ماب وانسخ اللينك، أو لو إنت في الملعب دوس "موقعي الحالي".</span>
      <div className="flex flex-wrap gap-2">
        <input
          dir="ltr"
          aria-label="لينك جوجل ماب أو الإحداثيات"
          placeholder="https://maps.google.com/…  أو  30.0444, 31.2357"
          value={paste}
          onChange={(e) => onPaste(e.target.value)}
          className={`${fieldClass} flex-[1_1_260px] text-right`}
        />
        <button
          type="button"
          onClick={locate}
          disabled={locating}
          className="btn-press min-h-[46px] px-4 rounded-xl border border-[#C9CFD9] bg-white hover:bg-slate-50 hover:border-slate-400 text-sm font-semibold text-navy-900 inline-flex items-center gap-2 transition-colors disabled:opacity-60"
        >
          {locating ? <Loader2 className="size-4 animate-spin" /> : <LocateFixed className="size-4" />}
          موقعي الحالي
        </button>
      </div>
      {note && (
        <span role="status" className={`text-xs font-semibold animate-fade-in ${note.tone === "ok" ? "text-[#1B6E3E]" : "text-[#A8261C]"}`}>
          {note.text}
        </span>
      )}
      <div className="grid grid-cols-2 gap-2">
        <label className="flex flex-col gap-1 text-xs font-semibold text-[#5B6475]">
          خط العرض (Latitude)
          <input
            dir="ltr"
            inputMode="decimal"
            placeholder="30.0444"
            value={value.lat}
            onChange={(e) => onChange({ ...value, lat: e.target.value })}
            aria-invalid={outside}
            className={`${fieldClass} text-right`}
          />
        </label>
        <label className="flex flex-col gap-1 text-xs font-semibold text-[#5B6475]">
          خط الطول (Longitude)
          <input
            dir="ltr"
            inputMode="decimal"
            placeholder="31.2357"
            value={value.lng}
            onChange={(e) => onChange({ ...value, lng: e.target.value })}
            aria-invalid={outside}
            className={`${fieldClass} text-right`}
          />
        </label>
      </div>
      {outside && (
        <span role="alert" className="text-xs font-semibold text-[#A8261C]">
          النقطة دي برا مصر. غالباً الرقمين متبدلين: خط العرض في مصر بين ٢٢ و٣٢، وخط الطول بين ٢٥ و٣٧.
        </span>
      )}
      {embed && !outside && (
        <div className="flex flex-col gap-1.5 animate-fade-in">
          <iframe
            title="معاينة موقع الملعب"
            src={embed}
            loading="lazy"
            referrerPolicy="no-referrer"
            className="w-full h-[220px] rounded-xl border border-[#E4E7EC] bg-white"
          />
          <a
            href={`https://www.google.com/maps?q=${lat},${lng}`}
            target="_blank"
            rel="noopener noreferrer"
            className="self-start inline-flex items-center gap-1 text-xs font-semibold text-navy-900 hover:text-brand-red"
          >
            اتأكد على جوجل ماب
            <ExternalLink className="size-3.5" />
          </a>
        </div>
      )}
    </fieldset>
  );
}

/** Form strings → the API's numbers (null when empty). */
export function toApiCoordinates(v: LatLng): { latitude: number | null; longitude: number | null } {
  const lat = Number(v.lat);
  const lng = Number(v.lng);
  const ok = v.lat.trim() !== "" && v.lng.trim() !== "" && Number.isFinite(lat) && Number.isFinite(lng);
  return ok ? { latitude: lat, longitude: lng } : { latitude: null, longitude: null };
}

export const isValidLocation = (v: LatLng) => {
  const { latitude, longitude } = toApiCoordinates(v);
  return latitude !== null && longitude !== null && inEgypt(latitude, longitude);
};
