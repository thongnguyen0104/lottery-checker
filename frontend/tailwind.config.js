import defaultTheme from 'tailwindcss/defaultTheme'

// Mọi màu giao diện đọc từ biến CSS (index.css) để đổi theme lúc chạy mà không build lại:
// data-accent trên <html> chọn bảng màu brand, data-mode chọn bộ màu nền/chữ sáng hoặc tối.
// Biến lưu dạng "R G B" để vẫn dùng được độ trong của Tailwind (bg-brand-500/20...).
const v = name => `rgb(var(--${name}) / <alpha-value>)`
const shades = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900]

/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,ts,jsx,tsx}'],
  darkMode: ['selector', '[data-mode="dark"]'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['"Be Vietnam Pro"', ...defaultTheme.fontFamily.sans],
      },
      colors: {
        brand: Object.fromEntries(shades.map(s => [s, v(`brand-${s}`)])),
        // Màu thứ hai của bảng màu — điểm cuối của dải gradient nút chính / nền cực quang.
        accent: v('accent'),
        canvas: v('canvas'),     // nền trang
        surface: v('surface'),   // nền thẻ, ô nhập
        muted: v('muted'),       // nền phụ: hàng xen kẽ, nút tab chưa chọn
        line: v('line'),         // viền
        ink: { DEFAULT: v('ink'), soft: v('ink-soft'), faint: v('ink-faint') },
        // Màu trạng thái tự đổi theo sáng/tối: dùng kiểu text-ok + bg-ok/10 + border-ok/30.
        ok: v('ok'),
        warn: v('warn'),
        bad: v('bad'),
        info: v('info'),
      },
      boxShadow: {
        soft: '0 1px 2px rgb(15 15 35 / .04), 0 8px 28px -8px rgb(15 15 35 / .12)',
      },
    },
  },
  plugins: [],
}
