import type { Metadata } from "next";
import { IBM_Plex_Sans_Arabic, Inter } from "next/font/google";
import "./globals.css";
import { currentUser } from "@malabak/bff/server";
import { Providers } from "@/components/Providers";
import { bffConfig } from "@/lib/bff-config";
import { AdminShell } from "@/components/AdminShell";

const arabic = IBM_Plex_Sans_Arabic({
  subsets: ["arabic"],
  weight: ["400", "500", "600", "700"],
  display: "swap",
  variable: "--font-arabic",
});

const inter = Inter({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  display: "swap",
  variable: "--font-inter",
});

export const metadata: Metadata = {
  title: "ملعبك — إدارة المنصة",
  description: "لوحة إدارة منصة ملعبك",
  robots: { index: false, follow: false },
};

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const user = await currentUser(bffConfig);
  return (
    <html lang="ar" dir="rtl" className={`${arabic.variable} ${inter.variable}`}>
      <body>
        <Providers>
          <AdminShell user={user}>{children}</AdminShell>
        </Providers>
      </body>
    </html>
  );
}
