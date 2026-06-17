// Danh sách đài đầy đủ — hard-code (frontend cache, không gọi API mỗi lần).
// `code` PHẢI khớp đúng code mà backend ProvinceMatcher sinh ra.
export const ALL_PROVINCES = [
  // Miền Nam
  { code: 'TPHCM', name: 'TP.HCM' },
  { code: 'DongThap', name: 'Đồng Tháp' },
  { code: 'CaMau', name: 'Cà Mau' },
  { code: 'BenTre', name: 'Bến Tre' },
  { code: 'VungTau', name: 'Vũng Tàu' },
  { code: 'BacLieu', name: 'Bạc Liêu' },
  { code: 'DongNai', name: 'Đồng Nai' },
  { code: 'CanTho', name: 'Cần Thơ' },
  { code: 'SocTrang', name: 'Sóc Trăng' },
  { code: 'TayNinh', name: 'Tây Ninh' },
  { code: 'AnGiang', name: 'An Giang' },
  { code: 'BinhThuan', name: 'Bình Thuận' },
  { code: 'VinhLong', name: 'Vĩnh Long' },
  { code: 'BinhDuong', name: 'Bình Dương' },
  { code: 'TraVinh', name: 'Trà Vinh' },
  { code: 'LongAn', name: 'Long An' },
  { code: 'HauGiang', name: 'Hậu Giang' },
  { code: 'KienGiang', name: 'Kiên Giang' },
  { code: 'TienGiang', name: 'Tiền Giang' },
  { code: 'DaLat', name: 'Đà Lạt' },
  { code: 'LamDong', name: 'Lâm Đồng' },
  // Miền Trung
  { code: 'PhuYen', name: 'Phú Yên' },
  { code: 'Hue', name: 'Huế' },
  { code: 'DakLak', name: 'Đắk Lắk' },
  { code: 'QuangNam', name: 'Quảng Nam' },
  { code: 'KhanhHoa', name: 'Khánh Hòa' },
  { code: 'DaNang', name: 'Đà Nẵng' },
  { code: 'BinhDinh', name: 'Bình Định' },
  { code: 'QuangTri', name: 'Quảng Trị' },
  { code: 'QuangBinh', name: 'Quảng Bình' },
  { code: 'GiaLai', name: 'Gia Lai' },
  { code: 'NinhThuan', name: 'Ninh Thuận' },
  { code: 'KonTum', name: 'Kon Tum' },
  { code: 'QuangNgai', name: 'Quảng Ngãi' },
  // Miền Bắc (cơ cấu giải khác — hiện chưa hỗ trợ dò)
  { code: 'MB', name: 'Miền Bắc' },
]

// Đổi code đài → tên hiển thị tiếng Việt (fallback: trả lại code nếu không tìm thấy)
export const provinceName = (code: string): string =>
  ALL_PROVINCES.find(p => p.code === code)?.name ?? code
