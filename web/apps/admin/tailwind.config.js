/** @type {import('tailwindcss').Config} */
module.exports = {
  content: ["./src/**/*.{js,ts,jsx,tsx,mdx}", "../../packages/ui/src/**/*.{js,ts,jsx,tsx,mdx}"],
  theme: {
    extend: {
      colors: {
        ink: "#0A1330", // admin navy (sidebar, primary actions)
        navy: { 900: "#0F1B3D" },
        brand: { red: "#D63B2F", "red-hover": "#b82e23", gold: "#F5B62B" },
        surface: { DEFAULT: "#F3F5F9" },
      },
      fontFamily: {
        arabic: ["var(--font-arabic)", "IBM Plex Sans Arabic", "sans-serif"],
      },
    },
  },
  plugins: [],
};
