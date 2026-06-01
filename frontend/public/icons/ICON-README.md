# App Icon Set — Dò Vé Số

## File trong bộ này

| File | Kích thước | Mục đích |
|---|---|---|
| `icon.svg` | vector | Master, dùng để re-export nếu muốn chỉnh design sau này |
| `icon-1024.png` | 1024×1024 | Master PNG, dùng cho App Store/Play Store nếu sau này build native |
| `icon-512.png` | 512×512 | PWA manifest, splash screen |
| `icon-192.png` | 192×192 | PWA manifest, home screen Android |
| `icon-96.png` | 96×96 | Notification, shortcut Android |
| `apple-touch-icon.png` | 180×180 | iOS "Add to Home Screen" |
| `favicon-32.png` / `favicon-16.png` | 32, 16 | Tab browser desktop |
| `favicon.ico` | multi-res (16/32/48) | Compat với browser cũ |
| `icon-maskable.svg` | vector | Master maskable |
| `icon-maskable-512.png` / `icon-maskable-192.png` | 512, 192 | Android adaptive icon (launcher tự crop tròn/vuông) |

## Cách cài vào project React

**1. Copy tất cả PNG vào `public/icons/`** (trừ `.svg` và `.ico`):
```
frontend/
└── public/
    ├── icons/
    │   ├── icon-192.png
    │   ├── icon-512.png
    │   ├── icon-maskable-192.png
    │   ├── icon-maskable-512.png
    │   ├── apple-touch-icon.png
    │   └── ...
    ├── favicon.ico
    └── manifest.webmanifest
```

**2. Đặt `favicon.ico` ngay tại `public/favicon.ico`** (mặc định Vite serve từ root).

**3. Thêm vào `index.html`**:
```html
<head>
  <link rel="icon" href="/favicon.ico" sizes="any">
  <link rel="icon" type="image/png" sizes="32x32" href="/icons/favicon-32.png">
  <link rel="icon" type="image/png" sizes="16x16" href="/icons/favicon-16.png">
  <link rel="apple-touch-icon" href="/icons/apple-touch-icon.png">
  <link rel="manifest" href="/manifest.webmanifest">
  <meta name="theme-color" content="#DC2626">
</head>
```

**4. Copy `manifest.webmanifest` vào `public/`** — đã có sẵn icon path đúng.

## Kiểm tra trên thiết bị thật

- **Android**: mở Chrome → vào site → menu → "Add to Home screen" → icon phải hiện đúng. Nếu launcher (Samsung One UI, Pixel Launcher...) hiển thị icon trong khung tròn — đó là maskable version đang hoạt động.
- **iOS**: Safari → Share → "Add to Home Screen" → kiểm tra icon trên home screen có sắc nét không.
- **Desktop**: F12 → Application tab → Manifest → check warnings.

## Re-export nếu chỉnh sửa

```bash
pip install cairosvg pillow

python3 -c "
import cairosvg
for size in [16, 32, 96, 180, 192, 512, 1024]:
    cairosvg.svg2png(url='icon.svg', write_to=f'icon-{size}.png',
                     output_width=size, output_height=size)
"
```
