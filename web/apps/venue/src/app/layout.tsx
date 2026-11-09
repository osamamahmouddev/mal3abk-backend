import type { Metadata } from "next";
import { IBM_Plex_Sans_Arabic, Inter } from "next/font/google";
import "./globals.css";
import { AppShell } from "@/components/AppShell";
import { Providers } from "@/components/Providers";
import { currentUser } from "@malabak/bff/server";
import { bffConfig } from "@/lib/bff-config";

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
  title: "ملعبك — لوحة إدارة الملعب",
  description: "نظام إدارة الملاعب والحجوزات الرياضية في مصر",
};

export default async function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const user = await currentUser(bffConfig);
  return (
    <html lang="ar" dir="rtl" className={`${arabic.variable} ${inter.variable}`}>
      <body className="bg-[#F3F5F9] text-[#0F172A] antialiased">
        <Providers signedIn={user !== null}>
          <AppShell user={user}>{children}</AppShell>
        </Providers>
      </body>
    </html>
  );
}
