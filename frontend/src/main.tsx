import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// Tự host font (không gọi Google Fonts): mỗi độ đậm tách theo bảng chữ, trình duyệt chỉ tải
// phần chữ Việt/Latin thực sự dùng tới.
import '@fontsource/be-vietnam-pro/400.css'
import '@fontsource/be-vietnam-pro/500.css'
import '@fontsource/be-vietnam-pro/600.css'
import '@fontsource/be-vietnam-pro/700.css'
import '@fontsource/be-vietnam-pro/800.css'
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
