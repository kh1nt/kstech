/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Views/**/*.cshtml",
    "./Pages/**/*.cshtml",
    "./wwwroot/**/*.js"
  ],
  darkMode: 'class',
  corePlugins: {
    preflight: false,
  },
  theme: {
    extend: {
      colors: {
        // ERP theme colors
        "brand-primary": "#236460",
        "brand-secondary": "#a9f090",
        "background-light": "#f3f5f6",
        "background-dark": "#111827",
        "card-light": "#ffffff",
        "card-dark": "#1f2937",
        "text-light": "#1f2937",
        "text-dark": "#f3f4f6",
        "muted-light": "#6b7280",
        "muted-dark": "#9ca3af",
        // Store theme colors
        "store-ink": "#132336",
        "store-muted": "#5f6e7f",
        "store-accent": "#0f7d75",
        "store-accent-soft": "#dbf6f2",
        "store-surface": "#f6f8fb",
        "store-highlight": "#ffb454",
        // Landing Page colors
        "primary": "#14b8a6",
        "primary-container": "#042f2e",
        "on-primary": "#on-primary",
        "on-primary-container": "#2dd4bf",
        "surface": "#1e293b",
        "surface-variant": "#334155",
        "surface-container": "#0f172a",
        "surface-container-low": "#0f172a",
        "surface-container-highest": "#475569",
        "surface-container-lowest": "#0b0f19",
        "on-surface": "#f8fafc",
        "on-surface-variant": "#cbd5e1",
        "outline": "#334155",
        "outline-variant": "#334155"
      },
      borderRadius: {
        DEFAULT: "0.5rem",
        'lg': '0.5rem',
        'xl': '1rem',
        '2xl': '1.5rem',
      },
      fontFamily: {
        sans: ['Inter', 'sans-serif'],
        display: ['Inter', 'sans-serif'],
        storeSans: ['"Public Sans"', 'sans-serif'],
        storeDisplay: ['Sora', 'sans-serif'],
        headline: ['"Space Grotesk"', 'sans-serif'],
        body: ['Inter', 'sans-serif'],
        label: ['Inter', 'sans-serif']
      }
    },
  },
  plugins: [
    require('@tailwindcss/forms'),
    require('@tailwindcss/typography')
  ],
}
