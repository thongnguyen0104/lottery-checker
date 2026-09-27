# Tạo VM miễn phí trên Oracle Cloud (Always Free) — từng bước

Mục tiêu: 1 VM **Ubuntu 24.04, ARM (VM.Standard.A1.Flex) 2 OCPU / 12 GB RAM**, IP tĩnh, mở port
80/443 — đủ để chạy `deploy/setup-server.sh` + `publish.ps1`. Tổng chi phí **$0**.

> Giao diện Oracle Console thỉnh thoảng đổi tên nút/vị trí menu. Nếu không thấy đúng chữ như
> dưới đây, tìm nút có ý nghĩa tương tự (hoặc gõ tên vào ô tìm kiếm trên cùng Console).

---

## Bước 0 — Chuẩn bị (5 phút)

| Cần | Ghi chú |
|---|---|
| Email chưa từng đăng ký Oracle Cloud | Mỗi người/thẻ chỉ được 1 tài khoản free |
| Số điện thoại nhận SMS | Để xác minh |
| **Thẻ Visa/Mastercard** (credit hoặc debit quốc tế) | Chỉ để xác minh danh tính. Oracle giữ tạm ~1 USD rồi hoàn lại. **Không trừ tiền** nếu chỉ dùng tài nguyên Always Free. Một số thẻ debit nội địa bị từ chối — thử thẻ khác |
| Máy Windows có `ssh` | Windows 10/11 có sẵn OpenSSH |

### Tạo SSH key trên máy Windows

Script deploy mặc định dùng `D:\Projects\lottery.key`.

**Trường hợp A — ĐÃ có `D:\Projects\lottery.key`** (máy bạn hiện đang có file này): **đừng**
chạy `ssh-keygen -t ...` nữa (sẽ ghi đè mất key cũ). Chỉ cần sinh file public key từ nó:

```powershell
# Khoá quyền trước (xem giải thích bên dưới), không thì ssh-keygen từ chối đọc
icacls D:\Projects\lottery.key /inheritance:r
icacls D:\Projects\lottery.key /grant:r "$($env:USERNAME):(R)"
icacls D:\Projects\lottery.key /remove:g '*S-1-5-11' '*S-1-5-32-545'   # bỏ Authenticated Users + Users

ssh-keygen -y -f D:\Projects\lottery.key | Out-File -Encoding ascii D:\Projects\lottery.key.pub
# KHÔNG dùng dấu > : PowerShell 5.1 ghi file UTF-16, Oracle sẽ báo key không hợp lệ
```

**Trường hợp B — chưa có key nào:**

```powershell
ssh-keygen -t rsa -b 4096 -f D:\Projects\lottery.key -N '""'
# sinh ra 2 file:
#   D:\Projects\lottery.key      <- private key, GIỮ KÍN, không commit
#   D:\Projects\lottery.key.pub  <- public key, sẽ dán vào Oracle
icacls D:\Projects\lottery.key /inheritance:r
icacls D:\Projects\lottery.key /grant:r "$($env:USERNAME):(R)"
icacls D:\Projects\lottery.key /remove:g '*S-1-5-11' '*S-1-5-32-545'   # bỏ Authenticated Users + Users
```

**Trường hợp C — lúc tạo VM đã chọn "Generate a key pair for me"** và tải về
`ssh-key-YYYY-MM-DD.key`: key đó mới vào được VM. Chép ra chỗ cố định, khoá quyền như trên, rồi
mọi lệnh `ssh`/`scp`/`publish.ps1` dùng `-i` / `-Key` trỏ tới file này:

```powershell
Copy-Item "$env:USERPROFILE\Downloads\ssh-key-2026-09-26.key" D:\Projects\oracle-vm.key
icacls D:\Projects\oracle-vm.key /inheritance:r
icacls D:\Projects\oracle-vm.key /grant:r "$($env:USERNAME):(R)"
icacls D:\Projects\oracle-vm.key /remove:g '*S-1-5-11' '*S-1-5-32-545'
```

Vì sao phải `icacls`: file trên ổ D: thừa hưởng quyền đọc của nhóm Users, `ssh` trên Windows
thấy vậy sẽ báo *UNPROTECTED PRIVATE KEY FILE* và từ chối dùng key.

---

## Bước 1 — Đăng ký tài khoản (15–30 phút)

1. Vào https://signup.cloud.oracle.com (hoặc https://cloud.oracle.com → **Sign Up**).
2. Điền quốc gia **Vietnam**, họ tên, email → xác minh email.
3. **Cloud Account Name**: đặt tên ngắn không dấu (vd `thongdove`). Tên này dùng để đăng nhập sau này — ghi lại.
4. **Home Region: `Singapore (ap-singapore-1)`**.
   - ⚠️ **Chọn xong KHÔNG đổi được**, và tài nguyên Always Free **chỉ tạo được ở home region**.
   - Không chọn *Singapore West (ap-singapore-2)*: cùng độ trễ về VN nhưng hay hết slot ARM hơn.
5. Nhập địa chỉ, số điện thoại (nhận SMS), thông tin thẻ → **Start my free trial**.
6. Chờ email *"Your Oracle Cloud account is fully provisioned"* (vài phút tới vài giờ). Chưa nhận
   email này thì chưa tạo VM được.

> Tài khoản mới có thêm **$300 credit dùng trong 30 ngày**. Hết 30 ngày, tài nguyên **không**
> thuộc Always Free sẽ bị dừng; VM làm theo hướng dẫn này nằm trong Always Free nên vẫn chạy.

### (Nên làm) Đặt cảnh báo chi phí

Console → menu ☰ → **Billing & Cost Management → Budgets → Create Budget**: số tiền `1` USD,
alert khi vượt 100% → gửi về email. Lỡ tạo nhầm tài nguyên tính phí là biết ngay.

---

## Bước 2 — Tạo mạng (VCN) + mở port 80/443 (5 phút)

Tạo mạng trước để khi tạo VM chỉ việc chọn.

1. Đăng nhập https://cloud.oracle.com (Cloud Account Name ở Bước 1.3) → góc trên phải kiểm tra
   region đang là **Singapore**.
2. Menu ☰ → **Networking → Virtual cloud networks** → **Actions / Start VCN Wizard**
   → chọn **Create VCN with Internet Connectivity** → **Start VCN Wizard**.
3. **VCN name: `vcn-lottery`** (đặt đúng tên này — script `oci-retry-arm.ps1` tìm subnet tên
   `public subnet-vcn-lottery`). Các ô CIDR để mặc định → **Next** → **Create**.
4. Mở port web: vào `vcn-lottery` → tab **Subnets** → `public subnet-vcn-lottery`
   → **Security Lists** → `Default Security List for vcn-lottery` → **Add Ingress Rules**, thêm 2 rule:

   | Source CIDR | IP Protocol | Destination Port Range | Mô tả |
   |---|---|---|---|
   | `0.0.0.0/0` | TCP | `80` | HTTP (Caddy xin chứng chỉ Let's Encrypt) |
   | `0.0.0.0/0` | TCP | `443` | HTTPS |

   Port 22 (SSH) đã có sẵn rule, không cần thêm.

> Ubuntu image của Oracle còn chặn thêm một lớp **iptables bên trong VM**. Không cần làm tay:
> `setup-server.sh` bước [1/6] tự mở 80/443.

---

## Bước 3 — Tạo VM (5 phút nếu còn slot)

Menu ☰ → **Compute → Instances** → **Create instance**.

| Mục | Chọn |
|---|---|
| **Name** | `lottery` |
| **Placement** | Availability domain nào cũng được (Singapore có thể chỉ có AD-1). Hết slot thì quay lại đổi AD |
| **Image** | **Change image** → **Canonical Ubuntu** → **24.04** (bản thường, không cần *Minimal*). `setup-server.sh` viết cho 24.04 |
| **Shape** | **Change shape** → **Virtual machine** → **Ampere** → **VM.Standard.A1.Flex** → **Number of OCPUs = 2**, **Amount of memory = 12 GB**. Phải thấy nhãn **Always Free-eligible** |
| **Primary VNIC** | Select existing VCN: `vcn-lottery`, subnet: `public subnet-vcn-lottery`, **Automatically assign public IPv4 address: Bật** |
| **Add SSH keys** | **Upload public key files (.pub)** → chọn `D:\Projects\lottery.key.pub` (hoặc *Paste public keys* và dán nội dung file .pub) |
| **Boot volume** | Để mặc định (~47–50 GB). Always Free cho tổng 200 GB |

Bấm **Create**. Trạng thái chuyển **PROVISIONING → RUNNING** (1–2 phút). Ghi lại **Public IP address**.

> ⚠️ **Hạn mức ARM free hiện là 2 OCPU / 12 GB cho CẢ tài khoản** (giảm từ 4/24 từ 15/06/2026,
> Oracle tự terminate instance vượt hạn mức từ 18/08/2026). Đừng tạo nhiều hơn, đừng tạo 2 VM ARM
> cộng lại vượt mức này.

### Nếu báo *"Out of capacity for shape VM.Standard.A1.Flex"* (rất thường gặp)

Không phải lỗi của bạn — Oracle hết máy ARM ở region đó. Thử theo thứ tự:

1. Đổi **Availability domain** khác (nếu có) → Create lại.
2. Giảm xuống **1 OCPU / 6 GB** (dễ chen vào slot lẻ hơn; app vẫn chạy được, OCR chậm hơn chút).
3. Thử lại vào sáng sớm (2–6h).
4. **Để máy tự thử liên tục** bằng script có sẵn:
   ```powershell
   winget install --id Oracle.OCI-CLI     # máy bạn đã cài sẵn — bỏ qua dòng này
   oci setup config                       # chưa chạy — cần làm
   #   - hỏi user OCID / tenancy OCID: lấy ở Console → ảnh đại diện góc phải → My profile
   #     (user OCID) và → Tenancy (tenancy OCID)
   #   - region: ap-singapore-1
   #   - đồng ý tạo API key mới
   # Rồi Console → My profile → API keys → Add API key → Paste public key
   #   → dán nội dung file ~/.oci/oci_api_key_public.pem
   oci iam region list          # chạy được = cấu hình đúng

   .\deploy\oci-retry-arm.ps1 -Ocpus 2 -MemoryGb 12
   # hoặc để mặc định 1 OCPU / 6 GB cho dễ có slot:
   .\deploy\oci-retry-arm.ps1
   ```
   Script thử mỗi 90 giây, tự nghỉ 5 phút khi bị Oracle chặn vì gọi dày, tạo được thì in Public IP.
   Cứ để cửa sổ chạy (có khi vài giờ tới vài ngày).
5. Đường cùng: shape **VM.Standard.E2.1.Micro** (AMD, luôn có slot, cũng Always Free) —
   nhưng chỉ 1/8 OCPU + 1 GB RAM, OCR sẽ chậm hơn rõ rệt (vài giây mỗi vé).

---

## Bước 4 — Đổi Public IP sang Reserved (IP tĩnh)

IP mặc định là **Ephemeral**: stop/start VM có thể đổi IP → domain trỏ sai → web chết. Reserved IP
cũng miễn phí.

1. **Compute → Instances → `lottery`** → mục **Attached VNICs** (hoặc tab *Networking*) → bấm vào VNIC.
2. **IPv4 Addresses** → dòng primary IP → **⋮ → Edit**.
3. Oracle không cho đổi thẳng Ephemeral → Reserved:
   - Chọn **No public IP** → **Update** (IP cũ bị gỡ).
   - **Edit** lại → chọn **Reserved public IP** → **Create new reserved IP** (đặt tên `lottery-ip`) → **Update**.
4. Ghi lại **IP mới** — từ giờ IP này không đổi nữa.

---

## Bước 5 — SSH thử vào VM

```powershell
ssh -i D:\Projects\lottery.key ubuntu@<IP>
# lần đầu hỏi fingerprint → gõ yes
```

Vào được dấu nhắc `ubuntu@lottery:~$` là xong. Kiểm tra nhanh:

```bash
uname -m          # aarch64 (ARM)
nproc; free -h    # 2 CPU, ~12 GB RAM
exit
```

Không vào được:
- `Permission denied (publickey)` → sai file key, hoặc user không phải `ubuntu`.
- `UNPROTECTED PRIVATE KEY FILE` → chạy lại 2 lệnh `icacls` ở Bước 0.
- Treo rồi timeout → Security List thiếu rule port 22 (wizard mặc định có), hoặc IP sai.

---

## Bước 6 — Domain miễn phí (DuckDNS) trỏ về VM

HTTPS là bắt buộc (camera trên điện thoại chỉ chạy qua HTTPS), mà Let's Encrypt không cấp chứng
chỉ cho IP trần → cần domain.

1. https://www.duckdns.org → đăng nhập bằng GitHub.
2. Ô **sub domain**: gõ tên (vd `dove-so`) → **add domain**.
3. Ô **current ip** của domain đó: dán **Reserved IP** ở Bước 4 → **update ip**.
4. Kiểm tra từ Windows:
   ```powershell
   nslookup dove-so.duckdns.org     # phải ra đúng IP của VM
   ```

---

## Bước 6b — CHỈ khi dùng máy Micro (VM.Standard.E2.1.Micro, 1 GB RAM)

Máy Micro chỉ có ~950 MB RAM, lúc mới bật Ubuntu đã dùng ~375 MB. Làm 2 việc sau **trước** Bước 7,
nếu không `apt` hoặc app dễ bị hệ điều hành kill vì hết RAM:

```bash
# 1. Swap 2 GB
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile
sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
free -h                                  # dòng Swap: 2.0Gi
```

2. Sau khi chạy `setup-server.sh` (Bước 7), thêm vào `/etc/lottery-api.env` dòng:
   ```ini
   DOTNET_gcServer=0
   ```
   (.NET mặc định dùng Server GC — giữ nhiều bộ nhớ để chạy nhanh trên máy nhiều lõi; Workstation GC
   tốn RAM ít hơn hẳn, hợp với máy 1 GB.)

OCR trên máy này sẽ chậm hơn máy ARM (chỉ được 1/8 OCPU). Vẫn nên để `oci-retry-arm.ps1` săn ARM song song.

---

## Bước 7 — Cài app lên VM (dùng script có sẵn)

```powershell
# 1. Đẩy thư mục deploy lên VM
scp -i D:\Projects\lottery.key -r .\deploy ubuntu@<IP>:~/

# 2. Cài server: iptables, .NET 10 runtime, Tesseract, Caddy (HTTPS), systemd (~5 phút)
ssh -i D:\Projects\lottery.key ubuntu@<IP>
sudo bash ~/deploy/setup-server.sh dove-so.duckdns.org

# 3. Điền key OCR.space (lấy trên máy dev: dotnet user-secrets list)
sudo nano /etc/lottery-api.env
#   CloudOcr__ApiKey=<key>
exit

# 4. Build + đẩy code từ máy Windows (backend + frontend)
.\deploy\publish.ps1 -Server ubuntu@<IP> -Key D:\Projects\lottery.key
```

## Bước 8 — Kiểm tra

```powershell
curl https://dove-so.duckdns.org/health                  # {"status":"ok",...}
curl https://dove-so.duckdns.org/api/results/available   # danh sách ngày × đài
```

Lần đầu khởi động, app tự cào 30 ngày kết quả (~40 giây) — xem log:

```bash
ssh -i D:\Projects\lottery.key ubuntu@<IP>
journalctl -u lottery-api -n 40 --no-pager
```

Rồi mở `https://dove-so.duckdns.org` **trên điện thoại** → quét thử 1 vé.

Các lần sau chỉ cần: `.\deploy\publish.ps1 -Server ubuntu@<IP> -Key D:\Projects\lottery.key`.

---

## Lưu ý để không mất VM

- **Oracle có thể thu hồi VM Always Free "nhàn rỗi"**: theo chính sách của Oracle, instance free
  mà CPU/mạng/RAM đều rất thấp suốt 7 ngày có thể bị reclaim. App này ít người dùng nên dễ rơi vào
  diện đó. Theo dõi email từ Oracle; nếu bị cảnh báo, cách phổ biến là nâng tài khoản lên
  **Pay As You Go** (tài nguyên Always Free vẫn miễn phí, và không bị reclaim vì nhàn rỗi) — nhớ
  đặt Budget alert ở Bước 1 trước khi nâng.
- Không vượt **2 OCPU / 12 GB** ARM và **200 GB** ổ đĩa tổng — vượt là bị tính phí hoặc bị terminate.
- Không cần backup DB: `lottery.db` chỉ là cache 30 ngày, mất thì app tự cào lại khi khởi động.
