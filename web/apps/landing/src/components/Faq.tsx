"use client";

import React, { useState } from "react";
import { FAQ_ITEMS } from "./faqItems";

export function Faq() {
  const [open, setOpen] = useState(0);

  return (
    <div className="faq">
      {FAQ_ITEMS.map(([q, a], i) => {
        const isOpen = open === i;
        return (
          <div key={q} className="faq-item">
            <button
              type="button"
              aria-expanded={isOpen}
              aria-controls={`faq-${i}`}
              onClick={() => setOpen(isOpen ? -1 : i)}
            >
              <span style={{ flex: 1 }}>{q}</span>
              <span className="faq-icon" aria-hidden="true">
                +
              </span>
            </button>
            <div
              id={`faq-${i}`}
              className={`faq-collapsible ${isOpen ? "is-open" : ""}`}
              role="region"
              aria-hidden={!isOpen}
            >
              <div className="faq-collapsible-inner">
                <p className="faq-answer">{a}</p>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
