import type { Metadata } from "next";
import { IBM_Plex_Sans_Arabic, Tajawal, Inter } from "next/font/google";
import "./globals.css";
import { AppShell } from "@/components/AppShell";

const arabic = IBM_Plex_Sans_Arabic({
  subsets: ["arabic"],
  weight: ["400", "500", "600", "700"],
  display: "swap",
  variable: "--font-arabic",
});

const tajawal = Tajawal({
  subsets: ["arabic"],
  weight: ["400", "500", "700"],
  display: "swap",
  variable: "--font-tajawal",
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

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="ar" dir="rtl" className={`${arabic.variable} ${tajawal.variable} ${inter.variable}`}>
      <body className="bg-[#F3F5F9] text-[#0F172A] antialiased">
        <AppShell>{children}</AppShell>
      </body>
    </html>
  );
}
