import type { Metadata, Viewport } from "next";
import localFont from "next/font/local";
import "./globals.css";

const arabic = localFont({
  src: [
    { path: "./fonts/ibm-plex-sans-arabic-arabic-400-normal.woff2", weight: "400", style: "normal" },
    { path: "./fonts/ibm-plex-sans-arabic-arabic-500-normal.woff2", weight: "500", style: "normal" },
    { path: "./fonts/ibm-plex-sans-arabic-arabic-600-normal.woff2", weight: "600", style: "normal" },
    { path: "./fonts/ibm-plex-sans-arabic-arabic-700-normal.woff2", weight: "700", style: "normal" },
  ],
  display: "swap",
  variable: "--font-arabic",
});

const description =
  "شوف المواعيد الفاضية في الملاعب اللي جنبك، ادفع العربون بفودافون كاش أو إنستاباي، وخلّي فريقك كله في مكان واحد. من غير ولا مكالمة.";

export const metadata: Metadata = {
  title: "ملعبك — احجز ملعبك في دقيقة",
  description,
  applicationName: "ملعبك",
  openGraph: {
    title: "ملعبك — احجز ملعبك في دقيقة",
    description,
    locale: "ar_EG",
    type: "website",
    siteName: "ملعبك",
  },
  twitter: {
    card: "summary",
    title: "ملعبك — احجز ملعبك في دقيقة",
    description,
  },
};

export const viewport: Viewport = {
  themeColor: "#0F1B3D",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="ar" dir="rtl" className={arabic.variable}>
      <body>{children}</body>
    </html>
  );
}
