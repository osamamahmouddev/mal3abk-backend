"use client";

import React, { useEffect, useState } from "react";
import { usePathname } from "next/navigation";

export function TopProgressBar() {
  const pathname = usePathname();
  const [progress, setProgress] = useState(0);
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    if (visible) {
      setProgress(100);
      const timer = setTimeout(() => {
        setVisible(false);
        setProgress(0);
      }, 250);
      return () => clearTimeout(timer);
    }
  }, [pathname, visible]);

  useEffect(() => {
    const handleStart = () => {
      setVisible(true);
      setProgress(25);
      const t1 = setTimeout(() => setProgress(65), 120);
      const t2 = setTimeout(() => setProgress(85), 350);
      return () => {
        clearTimeout(t1);
        clearTimeout(t2);
      };
    };

    window.addEventListener("malabak-nav-start", handleStart);
    return () => window.removeEventListener("malabak-nav-start", handleStart);
  }, []);

  if (!visible && progress === 0) return null;

  return (
    <div
      aria-hidden="true"
      className="fixed top-0 left-0 right-0 z-[100] h-[3px] pointer-events-none transition-opacity duration-200"
      style={{ opacity: visible ? 1 : 0 }}
    >
      <div
        className="h-full bg-gradient-to-r from-brand-red via-brand-gold to-emerald-500 shadow-[0_0_10px_rgba(214,59,47,0.8)] transition-all duration-200 ease-out"
        style={{ width: `${progress}%` }}
      />
    </div>
  );
}
