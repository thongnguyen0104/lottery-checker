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
        // Màu thứ hai của bảng màu — quầng nền cực quang, pháo giấy.
        accent: v('accent'),
        // Nền nút chính / tấm vé / huy hiệu (from-primary to-primary-end) + màu chữ trên nền đó.
        // Tách khỏi brand vì Hoàng kim cần vàng sáng + chữ navy, các bảng màu khác thì chữ trắng.
        primary: { DEFAULT: v('primary'), end: v('primary-end') },
        'on-primary': v('on-primary'),
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
        // Đổi theo sáng/tối (index.css): bóng nhạt kiểu nền sáng không thấy được trên navy.
        soft: 'var(--shadow-soft)',
      },
    },
  },
  plugins: [],
}
