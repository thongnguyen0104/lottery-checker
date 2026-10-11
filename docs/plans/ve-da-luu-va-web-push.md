# Kế hoạch: Vé đã lưu, tự dò sau giờ xổ, Web Push, nhắc lĩnh thưởng

> Trạng thái: **đề xuất** — 2026-10-11
> Phạm vi: tính năng 1 + 2 + 3 trong danh sách đề xuất (xem [Phụ lục](#phụ-lục--các-đề-xuất-còn-lại)).

## 1. Vấn đề và mục tiêu

Hiện tại người dùng chụp vé **trước giờ xổ** sẽ nhận trạng thái `NotDrawnYet` ([LotteryMatcher.cs](../../backend/LotteryChecker.Api/Services/LotteryMatcher.cs), thẻ "chưa đến giờ xổ" trong [ResultDisplay.tsx](../../frontend/src/components/ResultDisplay.tsx)). Họ phải tự nhớ để quay lại chụp hoặc nhập lại vé. Vé trúng cũng không có gì nhắc lĩnh trước hạn 30 ngày (`DrawSchedule.ClaimDays`).

Mục tiêu:

1. **Lưu vé**: một chạm "Lưu vé, báo tôi khi có kết quả", áp dụng cho vé chưa xổ, vé trúng và vé nhập tay.
2. **Tự dò**: khi `DailyResultFetchWorker` cào xong, hệ thống dò mọi vé đang chờ của các (ngày, đài) vừa có kết quả.
3. **Báo kết quả**: báo trong app (chuông SignalR có sẵn) và qua **Web Push**, để tin vẫn tới khi người dùng đã đóng app.
4. **Nhắc lĩnh thưởng**: vé trúng mà chưa đánh dấu "đã lĩnh" sẽ được nhắc ở mốc còn 5 ngày và còn 1 ngày trước hạn.

Không làm trong đợt này: lưu vé cho khách chưa đăng nhập, vé Miền Trung / Miền Bắc (scraper hiện chỉ cào MN), Vietlott.

## 2. Hiện trạng liên quan (đã kiểm tra trong code)

| Thành phần | Hiện có | Dùng lại thế nào |
|---|---|---|
| `LotteryMatcher.Match(ticket, date, province)` | Trả `Checked` / `NotDrawnYet` / `NoData` / `Expired` | Gọi lại khi tự dò, không viết logic dò mới |
| `DailyResultFetchWorker` | Cào lúc 16:45, thử lại mỗi 10' tới 20:00; sau mỗi lần cào gọi `SettleTicketsAsync` (vé cào) | Thêm bước chốt vé đã lưu ngay sau đó |
| `NotificationService` + `INotificationPusher` | Lưu DB và đẩy SignalR; chỉ lưu id, text dựng lúc đọc | Thêm loại thông báo mới, thêm pusher Web Push |
| `FeatureFlags` + `[RequireFeature]` | Cờ bật/tắt theo tính năng, admin xem trước | Thêm cờ `savedTickets` (mặc định tắt) |
| PWA (`vite-plugin-pwa`, `generateSW`) | Đã có manifest, cài được lên màn hình chính | Thêm script push vào service worker qua `importScripts` |
| Caddy | `/sw.js`, `/registerSW.js` đặt `no-cache` | Thêm `/push-sw.js` vào matcher |
| Auth | Cookie `dvs.auth` | Mọi API mới đều yêu cầu đăng nhập |
| DB | SQLite, `db.Database.Migrate()` lúc khởi động | Thêm 1 migration |

## 3. Thiết kế backend

### 3.1 Model mới

```csharp
public enum SavedTicketStatus { Waiting, Won, Lost, Expired }

/// Vé người dùng lưu để hệ thống tự dò / nhắc lĩnh. Mỗi (user, ngày, đài, số) chỉ 1 dòng.
public class SavedTicket
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string TicketNumber { get; set; } = "";
    public DateOnly DrawDate { get; set; }
    public string Province { get; set; } = "";
    public SavedTicketStatus Status { get; set; }
    public long Prize { get; set; }              // long, không decimal (SQLite không SUM được decimal)
    public string? PrizeTiers { get; set; }      // vd "2,7" — tên giải trúng, để hiển thị lại
    public string? Note { get; set; }            // tối đa 100 ký tự, vd "vé mua ở chợ Bến Thành"
    public DateTime CreatedAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public DateTime? ClaimedAt { get; set; }     // người dùng bấm "Đã lĩnh"
    public byte ReminderStage { get; set; }      // 0 = chưa nhắc, 1 = đã nhắc D-5, 2 = đã nhắc D-1
}

/// Một trình duyệt / thiết bị đã đồng ý nhận Web Push.
public class PushSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Endpoint { get; set; } = "";   // unique
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public int FailCount { get; set; }
}
```

Index:
- `SavedTicket`: unique `(UserId, DrawDate, Province, TicketNumber)`; thêm `(Status, DrawDate, Province)` cho truy vấn chốt vé.
- `PushSubscription`: unique `Endpoint`, index `UserId`.

`Notification` thêm cột `SavedTicketId` (FK, cascade), giống cách `ShopReviewId` / `SiteReservationId` đang làm, để xoá vé thì thông báo tự mất.

`NotificationKind` thêm `TicketWon`, `TicketLost`, `TicketExpiring`.

Migration: `AddSavedTicketsAndPush`.

### 3.2 `SavedTicketService`

| Hàm | Việc làm |
|---|---|
| `SaveAsync(userId, number, date, province, note)` | Kiểm tra: số đúng 6 chữ số, đài thuộc MN (`DrawSchedule.RegionOf == "MN"`), chưa hết hạn. Trùng thì trả dòng cũ (idempotent). Giới hạn **100 vé `Waiting`/user** (ném 429 hoặc lỗi nghiệp vụ). Nếu đã xổ và DB đã có kết quả thì chốt luôn trong lượt lưu. |
| `ListAsync(userId, filter, page)` | Lọc theo trạng thái; phân trang 20 dòng (lấy dư 1 dòng giống `CheckHistory`). |
| `DeleteAsync(userId, id)` | Chỉ xoá vé của chính mình. |
| `MarkClaimedAsync(userId, id, claimed)` | Bật/tắt `ClaimedAt`. |
| `SettleAsync(ct)` | Lấy các `(DrawDate, Province)` distinct có vé `Waiting` mà `LotteryResults` đã có dữ liệu. Với từng vé: `LotteryMatcher.Match` → `Won` / `Lost`. Lưu theo lô, rồi tạo thông báo. Trả về danh sách vé vừa chốt, gom theo user. |
| `ExpireAsync(ct)` | Vé `Waiting` quá hạn lĩnh (thường do đài không xổ hôm đó hoặc cào thất bại kéo dài) → `Expired`. |

Ghi chú:
- Vé `Waiting` mà matcher trả `NoData` thì **giữ nguyên `Waiting`**: lần cào sau (10' nữa, hoặc 16:45 hôm sau cào bù) sẽ chốt.
- `SettleAsync` phải idempotent: chỉ đụng dòng `Waiting`, nên chạy lại nhiều lần trong khung 16:45–20:00 không gây lỗi.
- Có lưu vé thì cũng ghi `CheckHistory` / `CheckStats` như luồng dò thường, để thống kê không lệch. Cần quyết định có ghi khi tự chốt hay không (xem [mục 8](#8-câu-hỏi-cần-chốt)).

### 3.3 Gắn vào worker

`DailyResultFetchWorker`, ngay sau `SettleTicketsAsync(ct)` (cả lúc khởi động lẫn trong vòng lặp):

```csharp
await SettleSavedTicketsAsync(ct);   // scope mới → SavedTicketService.SettleAsync → TicketNotifier
```

Lỗi chỉ ghi log, không được làm hỏng lượt cào.

**Worker mới `TicketReminderWorker`** (`PeriodicTimer` 1 giờ, giống `BlogImageCleanupWorker`):
- `ExpireAsync`.
- Nhắc lĩnh: chọn vé `Won && ClaimedAt == null`:
  - `ClaimDeadline - hôm nay <= 5` và `ReminderStage == 0` → nhắc, đặt `ReminderStage = 1`.
  - `<= 1` và `ReminderStage < 2` → nhắc, đặt `ReminderStage = 2`.
- Chỉ gửi trong khung **08:00–21:00 giờ VN** (`DrawSchedule.NowVn`), không gửi push lúc nửa đêm.

### 3.4 Thông báo: trong app và Web Push

Tách việc **tạo** thông báo (DB + SignalR, mỗi vé 1 dòng) khỏi việc **đẩy push** (gom theo user để tránh spam):

- **Chuông trong app**: mỗi vé 1 `Notification` (`TicketWon` / `TicketLost`), snippet dựng lúc đọc: `"{số} · {đài} · {ngày} · {tiền}"`.
- **Web Push**: mỗi lượt `SettleAsync`, mỗi user nhận **1 push gộp**:
  - Có vé trúng: "🎉 Vé 123456 Đồng Nai trúng giải Bảy — 200.000đ" (nếu trúng nhiều vé: "Bạn trúng 2/5 vé, tổng 400.000đ").
  - Không trúng vé nào: "Đã có kết quả 5 vé của bạn — chưa trúng lần này."
  - Bấm vào thông báo → mở `/tai-khoan?tab=saved`.
- Nhắc lĩnh (`TicketExpiring`): mỗi vé 1 push, vì số lượng ít.

Thành phần:

```csharp
public interface IWebPushSender
{
    /// Trả false nếu subscription đã chết (404/410) → caller xoá.
    Task<bool> SendAsync(PushSubscription sub, WebPushPayload payload, CancellationToken ct);
}

public record WebPushPayload(string Title, string Body, string Url, string Tag);

public class WebPushService(AppDbContext db, IWebPushSender sender, ILogger<WebPushService> log)
{
    Task SendToUserAsync(int userId, WebPushPayload p, CancellationToken ct);
    // gửi tới mọi subscription của user; 404/410 → xoá; lỗi khác → FailCount++ (≥5 thì xoá)
}
```

- Thư viện đề xuất: **`Lib.Net.Http.WebPush`** (tpeczek; hỗ trợ VAPID, aes128gcm). Phương án dự phòng: `WebPush` (web-push-csharp). Kiểm tra bản mới nhất khi cài.
- Payload ngắn (< 2KB), gửi tuần tự với tối đa ~4 request song song (`Parallel.ForEachAsync`), để VM 1GB không bị dồn tải.
- `Tag` = `ticket-{date}-{user}` để thông báo cùng lượt trên máy thay thế nhau chứ không chồng lên.
- **Phạm vi đợt này**: chỉ đẩy push cho các loại thông báo về vé. Bình luận blog, đánh giá điểm bán vẫn chỉ hiện ở chuông; nếu cần thì mở rộng sau kèm trang cài đặt.

### 3.5 Cấu hình

`appsettings.json` (giá trị thật đặt qua env, giống key OCR):

```json
"WebPush": {
  "Subject": "mailto:admin@<domain>",
  "PublicKey": "",
  "PrivateKey": ""
}
```

- Tạo khoá một lần: `npx web-push generate-vapid-keys`.
- Thiếu khoá thì `WebPushService` tự tắt (`Enabled = false`, chỉ log 1 lần). Các tính năng khác vẫn chạy.
- **Không đổi khoá** sau khi đã phát hành; đổi khoá thì mọi subscription cũ mất hiệu lực.

### 3.6 API

Tất cả cần đăng nhập và đặt `[RequireFeature(FeatureFlags.SavedTickets)]`.

| Method | Route | Body / Query | Ghi chú |
|---|---|---|---|
| POST | `/api/saved-tickets` | `{ ticketNumber, drawDate, province, note? }` | Trả `SavedTicketDto` |
| POST | `/api/saved-tickets/bulk` | `{ items: [...] }` (≤ 20) | Cho màn nhiều vé |
| GET | `/api/saved-tickets` | `?status=waiting\|won\|lost\|expired&page=1` | |
| GET | `/api/saved-tickets/summary` | | Số vé chờ / trúng chưa lĩnh, cho badge |
| PATCH | `/api/saved-tickets/{id}/claimed` | `{ claimed: bool }` | |
| DELETE | `/api/saved-tickets/{id}` | | |
| GET | `/api/push/public-key` | | Trả VAPID public key, hoặc 404 nếu chưa cấu hình |
| POST | `/api/push/subscribe` | `{ endpoint, keys: { p256dh, auth } }` | Upsert theo endpoint, gán user hiện tại |
| POST | `/api/push/unsubscribe` | `{ endpoint }` | |

`FeatureFlags.All` thêm:

```csharp
new(SavedTickets, "Vé đã lưu & báo kết quả", "Saved tickets & result alerts",
    "Lưu vé, tự dò khi có kết quả, thông báo đẩy và nhắc lĩnh thưởng (tab Vé đã lưu ở trang Tài khoản).",
    "Save tickets, auto-check after the draw, push alerts and claim reminders (Saved tab on the Account page)."),
```

## 4. Thiết kế frontend

### 4.1 Điểm vào "Lưu vé"

| Màn hình | Nút | Khi bấm |
|---|---|---|
| `ResultDisplay`, trạng thái `NotDrawnYet` | **"Lưu vé, báo tôi khi có kết quả"** (nút chính) | Chưa đăng nhập → mở `AuthDialog` rồi lưu tiếp. Đã lưu → toast "Đã lưu, sẽ báo lúc ~16:45" và đề nghị bật thông báo (mục 4.3). |
| `ResultDisplay`, `Checked` + trúng | "Lưu để nhắc lĩnh thưởng" (nút phụ) | Lưu, vé chốt ngay là `Won` |
| `MultiResultDisplay` | "Lưu N vé chưa xổ" | Gọi `/bulk` |
| Tab "Vé đã lưu" | "+ Thêm vé" (nhập tay: số, ngày, đài) | Dùng lại form của luồng `/api/check` |

Đài không phải MN thì ẩn nút và hiện dòng nhỏ "Hiện chỉ hỗ trợ lưu vé Miền Nam".

### 4.2 Tab "Vé đã lưu" ở trang Tài khoản

- `Profile.tsx`: `type Tab = 'tickets' | 'history' | 'saved'`, map `saved → savedTickets` trong `TAB_FEATURE`. Hỗ trợ `?tab=saved` để link từ push mở đúng tab.
- Bộ lọc dạng chip: Đang chờ · Trúng · Không trúng · Hết hạn.
- Mỗi dòng: số vé (font lớn), đài · ngày, trạng thái kèm màu (dùng lại bảng màu trạng thái có sẵn trong `Profile.tsx`). Vé đang chờ hiện "Xổ lúc 16:15". Vé trúng hiện tiền, hạn lĩnh (đếm ngược ngày) và công tắc "Đã lĩnh".
- Vuốt hoặc bấm menu ⋯ để xoá.
- Badge trên tab = số vé trúng chưa lĩnh.

### 4.3 Bật Web Push

- File mới `frontend/public/push-sw.js`:
  - `self.addEventListener('push', ...)` → `showNotification(title, { body, icon: '/icons/icon-192.png', badge, tag, data: { url } })`.
  - `notificationclick` → nếu đã có tab app thì focus và chuyển tới `url`, không thì `clients.openWindow(url)`.
- `vite.config.ts` → `VitePWA({ workbox: { importScripts: ['push-sw.js'], ... } })`. Giữ `generateSW`, không cần chuyển sang `injectManifest`.
- `src/lib/push.ts`: `isPushSupported()`, `getPushState()` (`unsupported | denied | default | subscribed`), `enablePush()` (xin quyền → `registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey })` → POST subscribe), `disablePush()`.
- **Chỉ xin quyền sau một thao tác của người dùng**: hiện thẻ "Bật thông báo để biết kết quả ngay cả khi đóng app" sau lần lưu vé đầu tiên. Không bật popup khi vừa tải trang.
- **iOS**: Web Push chỉ hoạt động khi app đã "Thêm vào Màn hình chính" (iOS 16.4+). Phát hiện Safari iOS chưa ở chế độ standalone thì hiện hướng dẫn cài (Chia sẻ → Thêm vào MH chính) thay cho nút bật.
- Trang Tài khoản: công tắc "Thông báo đẩy trên thiết bị này".
- Đăng xuất → gọi `disablePush()` để thiết bị dùng chung không nhận tin của người trước.

### 4.4 Chuông thông báo và i18n

- `NotificationBell.tsx`: thêm `KIND_ICON` cho `TicketWon` (trophy), `TicketLost` (ticket), `TicketExpiring` (clock); bấm → `/tai-khoan?tab=saved`.
- `api/client.ts`: mở rộng `AppNotification['kind']`, thêm các hàm `saveTicket`, `saveTickets`, `getSavedTickets`, `setClaimed`, `deleteSavedTicket`, `getPushKey`, `subscribePush`, `unsubscribePush`.
- Thêm chuỗi vào `vi/*.json` và `en/*.json` (`common`, `check`, `notifications`).

### 4.5 Hạ tầng

- `deploy/Caddyfile.template`: `@sw path /sw.js /registerSW.js /push-sw.js`, để trình duyệt luôn lấy bản mới.
- Prod đã có HTTPS qua Cloudflare, là điều kiện bắt buộc của Web Push. Dev chạy `localhost` vẫn dùng được.
- Thêm `WebPush__PublicKey` / `WebPush__PrivateKey` / `WebPush__Subject` vào `lottery-api.service` (env) và ghi hướng dẫn vào `deploy/README.md`.

## 5. Kiểm thử

Backend (xUnit + FluentAssertions + EF InMemory, theo mẫu `ScratchTicketTests` / `NotificationTests`):

- `SavedTicketServiceTests`
  - Lưu: số sai định dạng, đài không phải MN, vé hết hạn → từ chối; lưu trùng → trả dòng cũ; vượt 100 vé chờ → lỗi.
  - Lưu vé đã có kết quả → chốt ngay `Won` / `Lost`.
  - `SettleAsync`: có kết quả → chốt đúng tiền và tên giải; chưa có kết quả → vẫn `Waiting`; chạy 2 lần → không tạo thông báo trùng.
  - `ExpireAsync` bằng `TimeProvider` giả.
- `TicketReminderTests`: D-6 không nhắc; D-5 nhắc 1 lần; D-1 nhắc lần 2; đã lĩnh thì không nhắc; ngoài 08:00–21:00 thì không gửi.
- `WebPushServiceTests` (với `IWebPushSender` giả): sender trả "đã chết" → xoá subscription; lỗi 5 lần → xoá; push được gộp 1 tin/user/lượt chốt.
- `FeatureFlagsTests`: cờ mới tắt → API trả 404 cho user thường, admin vẫn gọi được.

Frontend / thủ công:
- Chrome Android và Chrome desktop: lưu vé trước 16:15 → đóng app → nhận push sau 16:45 → bấm push mở đúng tab.
- iOS 16.4+ đã cài PWA: nhận push. Safari iOS chưa cài: hiện hướng dẫn cài.
- Từ chối quyền → app vẫn chạy, chuông trong app vẫn báo.
- Thử bằng tay qua `api-tests.http`: thêm endpoint admin `POST /api/admin/push-test` gửi push cho chính admin.

## 6. Các bước triển khai

| # | Bước | Ước lượng | Kết quả |
|---|---|---|---|
| 1 | Model, migration, `SavedTicketService`, API, cờ `savedTickets` (tắt), test | 1–1,5 ngày | Lưu / xem / chốt vé qua API |
| 2 | Gắn `SettleAsync` vào `DailyResultFetchWorker`, thông báo chuông (`TicketWon` / `TicketLost`) | 0,5 ngày | Tự dò và báo trong app |
| 3 | Frontend: nút lưu ở các màn kết quả, tab "Vé đã lưu", chuông, i18n | 1,5 ngày | Dùng được đầu-cuối, chưa có push |
| 4 | Web Push: VAPID, `WebPushService`, `push-sw.js`, bật/tắt, hướng dẫn iOS, Caddy | 1,5 ngày | Nhận tin khi đã đóng app |
| 5 | `TicketReminderWorker` (hết hạn + nhắc D-5 / D-1) | 0,5 ngày | Nhắc lĩnh thưởng |
| 6 | Bật cờ cho admin, chạy thử 2–3 ngày xổ thật, rồi bật cho mọi người | — | Phát hành |

Mỗi bước là một PR riêng. Cờ tắt suốt bước 1–5 nên merge dần vào `main` an toàn.

## 7. Rủi ro và cách giảm

| Rủi ro | Cách giảm |
|---|---|
| VM 1GB bị dồn tải lúc 16:45 (cào + chốt + gửi push) | Chốt theo lô (distinct ngày/đài), push gộp 1 tin/user, tối đa ~4 request song song |
| Spam thông báo | Gộp push theo lượt chốt; `tag` thay thế tin cũ; nhắc lĩnh tối đa 2 lần/vé; chỉ gửi 08:00–21:00 cho nhắc lĩnh |
| Subscription chết làm phình bảng | Xoá khi gặp 404/410 hoặc lỗi ≥ 5 lần |
| Kết quả cào sai rồi sửa lại sau | Kiểm tra `ResultScraper` có ghi đè kết quả cũ không. Nếu có, cân nhắc chỉ chốt khi đài đã đủ 18 số (xem ghi chú domain: 18 số/đài) |
| Người dùng lưu vé đài MT/MB | Chặn từ API và UI cho tới khi scraper hỗ trợ |
| iOS không nhận push | Hướng dẫn cài PWA; chuông trong app vẫn là kênh chính |

## 8. Câu hỏi cần chốt

1. **Có báo vé không trúng không?** Đề xuất: có, nhưng gộp trong 1 push/lượt. Ở chuông vẫn 1 dòng/vé.
2. **Vé tự chốt có ghi vào `CheckHistory` / `CheckStats` không?** Đề xuất: có ghi `CheckStats` (mỗi vé 1 lần, đã dedupe sẵn), không ghi `CheckHistory` (để lịch sử chỉ chứa lượt người dùng tự dò).
3. **Giới hạn số vé chờ mỗi user**: 100 có hợp lý không?
4. **Khách chưa đăng nhập**: bắt buộc đăng nhập (đề xuất), hay cho lưu tạm bằng local storage + push ẩn danh?
5. Có cần màn **cài đặt thông báo** theo loại (blog / điểm bán / vé) ngay đợt này không? Đề xuất: để sau.

## Phụ lục — các đề xuất còn lại

| # | Tính năng | Ghi chú nhanh |
|---|---|---|
| 4 | Hướng dẫn lĩnh thưởng trên màn trúng | Tính tiền thực nhận sau thuế TNCN (10% phần vượt 10 triệu/giải), giấy tờ cần mang, nơi lĩnh của công ty XS từng đài; nối bản đồ điểm bán. Làm ngay sau đợt này là hợp, vì dùng chung màn vé trúng. |
| 5 | Dò vé Vietlott (Mega 6/45, Power 6/55) | Cần nguồn kết quả mới và parser OCR riêng; vé in rõ nên OCR dễ hơn vé kiến thiết. |
| 6 | Xem kết quả trực tiếp lúc xổ | Dùng lại SignalR; cần nguồn cào được theo thời gian thực (minhngoc dùng AJAX). Tốn kết nối đồng thời lúc 16:15–16:45, cần cân nhắc với VM 1GB. |
| 7 | Ảnh chia sẻ khi trúng | Vẽ ảnh phía client (canvas), che bớt số vé; nút chia sẻ qua Web Share API. |
| 8 | Nhóm góp vé chung | Dựa trên `SavedTicket` + bảng nhóm và tỉ lệ góp; tự chia tiền thưởng khi chốt. |
