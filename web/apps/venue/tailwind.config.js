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
        arabic: ["var(--font-tajawal)", "Tajawal", "var(--font-arabic)", "IBM Plex Sans Arabic", "sans-serif"],
        sans: ["var(--font-inter)", "Inter", "sans-serif"],
      },
    },
  },
  plugins: [],
};
