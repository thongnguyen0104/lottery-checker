/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        brand: {
          50:  '#FEF2F2', 500: '#DC2626', 600: '#B91C1C', 700: '#991B1B'
        }
      }
    },
  },
  plugins: [],
}