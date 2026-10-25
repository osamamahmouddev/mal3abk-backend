import type { Metadata, Viewport } from "next";
import { IBM_Plex_Sans_Arabic } from "next/font/google";
import "./globals.css";

const arabic = IBM_Plex_Sans_Arabic({
  subsets: ["arabic"],
  weight: ["400", "500", "600", "700"],
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
