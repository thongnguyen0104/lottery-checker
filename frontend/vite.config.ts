import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.ico', 'apple-touch-icon.png'],
      manifest: {
        name: 'Dò Vé Số',
        short_name: 'Dò Vé Số',
        description: 'Quét và dò vé số xổ số kiến thiết tự động',
        theme_color: '#DC2626',
        background_color: '#DC2626',
        display: 'standalone',
        orientation: 'portrait',
        start_url: '/',
        icons: [
          { src: '/icons/icon-192.png', sizes: '192x192', type: 'image/png' },
          { src: '/icons/icon-512.png', sizes: '512x512', type: 'image/png' },
          { src: '/icons/icon-maskable-192.png', sizes: '192x192', type: 'image/png', purpose: 'maskable' },
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
      '/api': { target: 'http://localhost:5177', changeOrigin: true },
    },
  }
})