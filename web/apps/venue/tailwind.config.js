/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/pages/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/components/**/*.{js,ts,jsx,tsx,mdx}",
    "./src/app/**/*.{js,ts,jsx,tsx,mdx}",
    "../../packages/ui/src/**/*.{js,ts,jsx,tsx,mdx}",
  ],
  theme: {
    extend: {
      colors: {
        navy: {
          900: "#0F1B3D",
          800: "#182750",
          700: "#223565",
        },
        brand: {
          red: "#D63B2F",
          "red-hover": "#b82e23",
          gold: "#F5B62B",
        },
        surface: {
          DEFAULT: "#F3F5F9",
          card: "#FFFFFF",
          muted: "#E9ECF2",
        },
      },
      fontFamily: {
        arabic: ["var(--font-arabic)", "IBM Plex Sans Arabic", "sans-serif"],
        sans: ["var(--font-inter)", "Inter", "sans-serif"],
      },
      keyframes: {
        fadeIn: {
          "0%": { opacity: "0" },
          "100%": { opacity: "1" },
        },
        fadeInUp: {
          "0%": { opacity: "0", transform: "translateY(12px)" },
          "100%": { opacity: "1", transform: "translateY(0)" },
        },
        fadeInDown: {
          "0%": { opacity: "0", transform: "translateY(-12px)" },
          "100%": { opacity: "1", transform: "translateY(0)" },
        },
        scaleIn: {
          "0%": { opacity: "0", transform: "scale(0.96)" },
          "100%": { opacity: "1", transform: "scale(1)" },
        },
        modalIn: {
          "0%": { opacity: "0", transform: "scale(0.96) translateY(10px)" },
          "100%": { opacity: "1", transform: "scale(1) translateY(0)" },
        },
        slideInRight: {
          "0%": { transform: "translateX(100%)" },
          "100%": { transform: "translateX(0)" },
        },
        slideInLeft: {
          "0%": { transform: "translateX(-100%)" },
          "100%": { transform: "translateX(0)" },
        },
        slideUpToast: {
          "0%": { opacity: "0", transform: "translateY(20px) scale(0.97)" },
          "100%": { opacity: "1", transform: "translateY(0) scale(1)" },
        },
        shimmer: {
          "100%": { transform: "translateX(100%)" },
        },
        pulseSubtle: {
          "0%, 100%": { opacity: "1" },
          "50%": { opacity: "0.6" },
        },
        livePing: {
          "0%": { transform: "scale(1)", opacity: "1" },
          "75%, 100%": { transform: "scale(2.2)", opacity: "0" },
        },
      },
      animation: {
        "fade-in": "fadeIn 200ms cubic-bezier(0.16, 1, 0.3, 1) both",
        "fade-in-up": "fadeInUp 260ms cubic-bezier(0.16, 1, 0.3, 1) both",
        "fade-in-down": "fadeInDown 200ms cubic-bezier(0.16, 1, 0.3, 1) both",
        "scale-in": "scaleIn 220ms cubic-bezier(0.16, 1, 0.3, 1) both",
        "modal-in": "modalIn 260ms cubic-bezier(0.16, 1, 0.3, 1) both",
        "slide-in-right": "slideInRight 260ms cubic-bezier(0.16, 1, 0.3, 1) both",
        "slide-in-left": "slideInLeft 260ms cubic-bezier(0.16, 1, 0.3, 1) both",
        "slide-up": "slideUpToast 300ms cubic-bezier(0.16, 1, 0.3, 1) both",
        "pulse-subtle": "pulseSubtle 2s cubic-bezier(0.4, 0, 0.6, 1) infinite",
        "live-ping": "livePing 2s cubic-bezier(0, 0, 0.2, 1) infinite",
      },
      transitionTimingFunction: {
        spring: "cubic-bezier(0.16, 1, 0.3, 1)",
        smooth: "cubic-bezier(0.4, 0, 0.2, 1)",
      },
    },
  },
  plugins: [],
};
