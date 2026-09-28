# Cloudflare rate limit cho Lottery Checker

Áp dụng ở Cloudflare Dashboard → **Security → WAF → Rate limiting rules**.

## 1) Rule cho `/api/scan`

- Expression: `(http.request.uri.path eq "/api/scan" and http.request.method eq "POST")`
- Threshold đề xuất: `20 requests / 1 minute / IP`
- Action: `Block` trong `60s`

## 2) Rule cho `/api/check`

- Expression: `(http.request.uri.path eq "/api/check" and http.request.method eq "POST")`
- Threshold đề xuất: `60 requests / 1 minute / IP`
- Action: `Block` trong `60s`

## 3) Hành vi phía frontend

- Khi Cloudflare chặn, API trả HTTP `429`.
- Frontend đã map 429 thành thông báo thân thiện:
  - `Bạn thao tác quá nhanh. Vui lòng đợi một chút rồi thử lại.`

## 4) Ghi chú vận hành

- Nếu nhiều user dùng chung IP (NAT công ty/quán cafe), tăng ngưỡng thêm 1.5–2x.
- Theo dõi Security Events trong Cloudflare để tinh chỉnh, tránh block nhầm.
