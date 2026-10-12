# Kế hoạch: Minigame "Đua xe né chướng ngại" để kiếm tiền ảo cho Vé cào 2 số

> Trạng thái: **đã triển khai bước 1–4** (cờ `miniGame` mặc định tắt) — 2026-10-12. Mục 8 chốt: đổi "₫" → "xu"; chơi có thưởng lúc nào cũng được (không cần số dư < 10.000); 5 lượt × tối đa 5.000 xu/ngày; chưa làm checkpoint. Bước 5 (admin soi lượt chơi) chưa làm — inputs đã lưu sẵn trong `GameSessions`.
> Phạm vi: 1 minigame chơi một mình, phần thưởng là số dư ảo `User.Balance`, chỉ dùng để mua vé cào 2 số trong app.

## 1. Mục tiêu

- Người dùng hết số dư vẫn chơi tiếp vé cào được, không cần admin cộng tiền (`AdminWalletController.TopUp`).
- Game ngắn (30–90 giây), chơi được trên điện thoại: xe chạy tự động, người chơi đổi làn để né chướng ngại và ăn xu.
- **Không gian lận được bằng F12**: sửa JS, sửa biến, gọi thẳng API, gửi lại request cũ hay chỉnh tốc độ đồng hồ đều không làm tăng phần thưởng.

Không làm trong đợt này: bảng xếp hạng, chơi nhiều người, nạp tiền thật.

## 2. Điều kiện pháp lý (phải giữ đúng thì mới là "tiền ảo")

Lập luận "tiền ảo nên không vướng pháp lý" chỉ đứng được khi **cả hệ thống** giữ các điều kiện dưới đây. Chỉ cần vi phạm một điều, số dư sẽ có giá trị quy ra tiền thật, khi đó vé cào 2 số trở thành đánh bạc trực tuyến, dù tiền có từ minigame hay từ nơi khác:

| # | Điều kiện | Hiện trạng |
|---|---|---|
| 1 | Không nạp số dư bằng tiền thật (chuyển khoản, thẻ cào, ví điện tử, kể cả qua admin "nhận tiền rồi cộng hộ") | `TopUp` của admin tồn tại. **Cần quy định rõ** là chỉ dùng để tặng/sửa lỗi, không bán |
| 2 | Không rút, không đổi số dư ra tiền, thẻ cào, quà hay hiện vật | Chưa có. Giữ nguyên |
| 3 | Không chuyển số dư giữa các tài khoản (nếu chuyển được sẽ thành chợ mua bán ngoài) | Chưa có. Giữ nguyên |
| 4 | Ghi rõ trên UI: "Xu ảo, không có giá trị quy đổi" | **Cần thêm** ở trang vé cào và trang game |
| 5 | Nên đổi đơn vị hiển thị từ "₫" sang "xu" | Hiện hiện "₫". **Đề xuất đổi** |

Ngoài ra, game có kết nối máy chủ và có phần thưởng có thể thuộc diện quy định trò chơi điện tử trực tuyến (Nghị định 147/2024/NĐ-CP). Trước khi bật công khai **nên hỏi ý kiến luật sư**. Tài liệu này không phải tư vấn pháp lý.

## 3. Luật chơi

- Đường 3 làn, xe tự chạy lên phía trước, tốc độ tăng dần theo thời gian.
- Chướng ngại (rào, ổ gà, xe khác) xuất hiện theo hàng. Mỗi hàng luôn chừa ít nhất 1 làn trống để luôn có đường né.
- Thao tác: vuốt hoặc bấm trái/phải để đổi làn (mỗi lần 1 làn). Không có nhảy, không có phanh, để phần mô phỏng đơn giản và kiểm tra lại được.
- Ăn xu trên đường: +1 điểm mỗi xu. Đi qua mỗi hàng chướng ngại an toàn: +1 điểm.
- Đâm vào chướng ngại là hết lượt. Chạy tối đa 90 giây thì tự về đích.
- **Phần thưởng** = `min(điểm × 1.000 xu, 5.000 xu)` mỗi lượt (đổi từ 50 xu/điểm theo yêu cầu cho dễ kiếm — qua 5 hàng là chạm trần).

Giới hạn để không phá giá vé cào (giá 10.000/vé):

| Giới hạn | Giá trị đề xuất | Lý do |
|---|---|---|
| Lượt có thưởng / ngày | 5 | Tối đa ~25.000 xu/ngày, tức 2–3 vé |
| Thưởng tối đa / lượt | 5.000 | |
| Chỉ cho chơi có thưởng khi số dư < 10.000 | **Không** (đã chốt) | Giới hạn 5 lượt/ngày là đủ |
| Chơi thêm sau khi hết lượt | Được, nhưng không thưởng | |

Tất cả là hằng số trong `MiniGameService`, giống cách `ScratchTicketService` làm với `Price` và `MaxQuantity`.

## 4. Chống gian lận: server quyết định kết quả

### 4.1 Nguyên tắc

Client **không bao giờ** gửi điểm. Client chỉ gửi **chuỗi thao tác** (đổi làn ở tick nào). Server tự mô phỏng lại toàn bộ ván bằng cùng thuật toán và cùng seed, rồi **tự tính điểm**. Sửa JS để xe đi xuyên chướng ngại hay để điểm thành 9999 đều vô ích, vì server không đọc những giá trị đó.

```
Client                                  Server
  │  POST /api/minigame/start             │
  │ ─────────────────────────────────────▶│ tạo seed ngẫu nhiên (CSPRNG), lưu GameSession
  │◀───────────────────────────────────── │ { sessionId, seed, startedAt }
  │                                       │
  │  chơi: sinh đường từ seed, ghi input  │
  │  [{t:42,d:+1},{t:97,d:-1},...]        │
  │                                       │
  │  POST /api/minigame/finish            │
  │ ─────────────────────────────────────▶│ 1. session thuộc user, đang Started, chưa hết hạn
  │   { sessionId, inputs }               │ 2. replay mô phỏng với seed + inputs, tính điểm
  │                                       │ 3. kiểm tra thời gian thực tế ≥ thời gian ván
  │                                       │ 4. cộng tiền (Wallet.Apply) + đánh dấu Finished trong 1 lần lưu
  │◀───────────────────────────────────── │ { score, reward, balance }
```

### 4.2 Mô phỏng tất định, dùng chung FE/BE

Để server replay ra **đúng** kết quả như client thấy:

- **Tick cố định**: 30 tick/giây, tối đa 2.700 tick (90 giây). Client chạy vòng lặp theo tick, không theo delta-time, và chỉ nội suy khi vẽ.
- **Chỉ dùng số nguyên**: vị trí và tốc độ tính bằng đơn vị nguyên, ví dụ tốc độ = `200 + tick / 10`. Không dùng `float`, nên JS và C# không lệch nhau.
- **PRNG riêng** (vd. `mulberry32` hoặc `xorshift32`) viết giống hệt ở cả TS và C#, khởi tạo từ seed. Không dùng `Math.random()`.
- Code: `frontend/src/minigame/sim.ts` và `backend/.../Services/MiniGameSim.cs`.
- **Golden test**: `backend/LotteryChecker.Tests/TestData/minigame-golden.json` chứa các bộ (seed, inputs, điểm mong đợi), sinh bằng `frontend/scripts/minigame-golden.ts --write`. Test xUnit `MiniGameTests` và lệnh `npm run check:minigame` cùng đọc file này, nên khi hai bên lệch nhau là biết ngay.

### 4.3 Các kiểu tấn công và cách chặn

| Tấn công | Chặn bằng |
|---|---|
| Sửa biến điểm hoặc vô hiệu va chạm trong JS | Server không nhận điểm, tự replay. Đâm ở tick nào thì ván dừng ở tick đó |
| Gọi thẳng `finish` với điểm bịa | Không có trường điểm. Inputs không hợp lệ (tick giảm dần, tick > tick va chạm, `d` khác ±1, quá 2.700 tick, quá 1.000 input) thì bị loại |
| Gửi lại (replay) request `finish` cũ | Session chỉ chốt 1 lần: trạng thái `Started → Finished` dùng `IsConcurrencyToken` như `ScratchTicket.Status` |
| Gọi `start` nhiều lần để chọn seed dễ | Đếm lượt có thưởng **lúc start**, không phải lúc finish. Start rồi bỏ cũng mất lượt. Mỗi user chỉ có 1 session `Started` tại một thời điểm |
| Xem trước seed để tính đường đi tối ưu offline (bot) | Không chặn tuyệt đối được, vì seed buộc phải xuống client để vẽ đường. Giảm tác hại bằng: thưởng tối đa thấp, giới hạn lượt/ngày, kiểm tra thời gian (bên dưới). Một bot hoàn hảo cũng chỉ lấy được 25.000 xu/ngày, đúng bằng người chơi giỏi |
| Tua nhanh (gửi `finish` 2 giây sau `start` với ván 90 giây) | `finish` phải đến sau `startedAt + tick/30 giây − 3 giây dung sai` theo **giờ server**. Sửa đồng hồ máy khách không có tác dụng |
| Treo session để giữ lượt | Session hết hạn sau 3 phút → `Expired`, không thưởng |
| Nhiều tài khoản | Ngoài phạm vi game, vì đã có rate limit đăng ký ở `AuthController`. Thêm rate limit theo IP cho `/minigame/start` |

**Tuỳ chọn mạnh hơn (giai đoạn sau)**: chia đường làm từng đoạn 10 giây, server chỉ trả seed của đoạn kế tiếp khi client gửi input của đoạn trước (`/minigame/checkpoint`). Bot không thể tính trước cả ván, nhưng phải gọi API nhiều hơn và trễ mạng có thể làm game giật. Chưa làm ngay.

## 5. Thiết kế backend

### 5.1 Model

```csharp
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameSessionStatus { Started, Finished, Expired }

/// Một lượt chơi minigame. Seed do server sinh; điểm do server replay từ Inputs — không tin client.
public class GameSession
{
    public int Id { get; set; }
    public Guid PublicId { get; set; }          // client giữ cái này, không lộ Id tăng dần
    public int UserId { get; set; }
    public uint Seed { get; set; }
    public bool Rewarded { get; set; }          // lượt có thưởng (đã trừ quota lúc start)
    public GameSessionStatus Status { get; set; } // IsConcurrencyToken
    public int Score { get; set; }
    public int Ticks { get; set; }
    public long Reward { get; set; }
    public string? Inputs { get; set; }         // JSON nén, giữ để admin soi khi nghi gian lận
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
```

Thêm `WalletTransactionKind.GameReward`. Mọi khoản thưởng đều đi qua `Wallet.Apply`, nên vẫn đối soát được trong sổ giao dịch.

Index: `(UserId, StartedAt)` để đếm lượt trong ngày, `PublicId` unique.

### 5.2 Service và API

`MiniGameService` (theo khuôn `ScratchTicketService`: trả `record` lỗi enum, retry khi gặp `DbUpdateConcurrencyException`):

| Endpoint | Mô tả |
|---|---|
| `GET /api/minigame` | Lượt có thưởng còn lại hôm nay (theo giờ VN, `DrawSchedule.NowVn`), giới hạn, số dư |
| `POST /api/minigame/start` | Expire session cũ đang dở → tạo session mới, `Rewarded = còn lượt`. Trả `{ sessionId, seed, rewarded }` |
| `POST /api/minigame/finish` | `{ sessionId, inputs: [[tick, dir], ...] }` → validate, replay, chốt, cộng tiền |

Tất cả đều `[Authorize]` + `[RequireFeature("miniGame")]` + rate limit (vd. 20 start/phút/IP).

### 5.3 Test (`MiniGameTests.cs`)

- Golden: replay ra đúng điểm của từng bộ trong file JSON dùng chung.
- Mỗi hàng chướng ngại luôn còn ít nhất 1 làn trống (chạy 10.000 seed).
- `finish` hai lần thì lần 2 bị từ chối, tiền chỉ cộng 1 lần.
- `finish` quá sớm so với số tick bị từ chối. Quá 3 phút → Expired.
- Inputs sai định dạng, tick đi lùi, input sau tick va chạm đều bị loại hoặc bỏ qua.
- Hết lượt có thưởng thì `Reward = 0`. Thưởng không vượt mức tối đa.

## 6. Thiết kế frontend

- `frontend/src/components/MiniGame.tsx`: vẽ bằng `<canvas>`, không thêm thư viện game. Vòng lặp `requestAnimationFrame` gom thời gian thành các tick cố định và gọi `sim.step()`.
- `frontend/src/minigame/sim.ts`: logic thuần, không đụng DOM, test được bằng Vitest.
- Màn hình: Bắt đầu (còn X lượt có thưởng) → Chơi → Kết quả ("+3.200 xu", nút "Mua vé cào").
- Ở trang vé cào, khi số dư không đủ, hiện nút "Chơi minigame kiếm xu" thay cho câu "liên hệ admin".
- i18n: `locales/{vi,en}/minigame.json`.
- Nếu điểm server trả khác điểm client tự tính, hiện điểm của server. Đây gần như luôn là dấu hiệu client bị sửa.

## 7. Các bước triển khai

1. `MiniGameSim` C# + `sim.ts` + golden test (phần khó nhất, làm trước).
2. Model, migration `AddGameSessions`, `MiniGameService`, controller, cờ `miniGame`, test.
3. UI canvas, i18n, nút liên kết từ trang vé cào.
4. Đổi "₫" → "xu" và thêm dòng "không có giá trị quy đổi" (mục 2).
5. Admin: xem lượt chơi của user (điểm, inputs) để soi trường hợp nghi gian lận.

## 8. Câu hỏi cần chốt

1. Có đổi đơn vị "₫" thành "xu" trên toàn bộ vé cào không? (Đề xuất: có.)
2. Chỉ cho chơi có thưởng khi số dư < 10.000, hay lúc nào cũng được?
3. Giới hạn 5 lượt × 5.000 xu/ngày có hợp lý không?
4. Có cần làm checkpoint theo đoạn (mục 4.3) ngay từ đầu không? (Đề xuất: chưa.)
