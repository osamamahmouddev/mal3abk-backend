"use client";

import React, { createContext, useContext, useState } from "react";

export type Audience = "player" | "venue";

interface LandingState {
  audience: Audience;
  setAudience: (a: Audience) => void;
  plan: string | null;
  choosePlan: (plan: string) => void;
}

const Ctx = createContext<LandingState | null>(null);

/** Shared between the pricing cards and the join form: choosing a plan switches the form to "venue". */
export function LandingStateProvider({ children }: { children: React.ReactNode }) {
  const [audience, setAudience] = useState<Audience>("player");
  const [plan, setPlan] = useState<string | null>(null);

  const choosePlan = (p: string) => {
    setPlan(p);
    setAudience("venue");
  };

  return <Ctx.Provider value={{ audience, setAudience, plan, choosePlan }}>{children}</Ctx.Provider>;
}

export function useLandingState() {
  const ctx = useContext(Ctx);
  if (!ctx) throw new Error("useLandingState must be used inside LandingStateProvider");
  return ctx;
}
