import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  build: {
    rolldownOptions: {
      // @microsoft/signalr đặt /*#__PURE__*/ trước khai báo hàm — Rolldown bỏ qua và in cảnh báo đỏ
      // mỗi lần build dù vô hại. Chỉ tắt đúng loại này của đúng thư viện đó.
      onwarn(warning, warn) {
        if (warning.code === 'INVALID_ANNOTATION' && warning.message.includes('@microsoft/signalr')) return
        warn(warning)
      },
    },
  },
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.ico', 'favicon.svg', 'icons/apple-touch-icon.png'],
      manifest: {
        name: 'Dò Vé Số',
        short_name: 'Dò Vé Số',
        description: 'Quét và dò vé số xổ số kiến thiết tự động',
        theme_color: '#0F172A',
        background_color: '#0F172A',
        display: 'standalone',
        orientation: 'portrait',
        start_url: '/',
        icons: [
          { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png' },
          { src: '/icons/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' }
        ]
      }
    })
  ],
  server: {
    port: 5173,
    host: true,           // lắng nghe mọi card mạng (LAN / hotspot)
    allowedHosts: true,   // cho phép domain tunnel (vd *.trycloudflare.com)
    proxy: {
      // FE gọi /api/* (cùng origin) → Vite chuyển tiếp sang backend localhost:5177.
      // Nhờ vậy điện thoại chỉ cần tới được cổng 5173; KHÔNG cần CORS, KHÔNG cần lộ 5177.
      '/api': { target: 'http://localhost:5177', changeOrigin: true, ws: true }, // ws: SignalR (chuông thông báo)
    },
  },
  // `vite preview` = serve bản BUILD thật (bundle production + service worker PWA hoạt động
  // đúng, khác `vite dev`). Dùng khi đưa app ra ngoài qua Cloudflare Tunnel — xem
  // deploy/tunnel.ps1. Vẫn proxy /api để giữ nguyên tắc "một origin, không CORS".
  preview: {
    port: 4173,
    host: true,
    allowedHosts: true,
    proxy: {
      '/api': { target: 'http://localhost:5177', changeOrigin: true, ws: true }, // ws: SignalR (chuông thông báo)
    },
  }
})