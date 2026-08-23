#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Cài toàn bộ server cho Dò Vé Số trên Ubuntu 24.04 (ARM64 hoặc x64).
#
#   sudo bash deploy/setup-server.sh <domain>
#   vd: sudo bash deploy/setup-server.sh dove-so.duckdns.org
#
# Chạy lại nhiều lần được (idempotent) — an toàn khi cần sửa domain hoặc cài lại.
# Chi tiết từng bước: .claude/deploy-guide.md §2, §3, §5, §6
# ---------------------------------------------------------------------------
set -euo pipefail

DOMAIN="${1:-}"
if [[ -z "$DOMAIN" ]]; then
    echo "Thiếu domain. Ví dụ: sudo bash $0 dove-so.duckdns.org" >&2
    exit 1
fi
if [[ $EUID -ne 0 ]]; then
    echo "Phải chạy bằng sudo: sudo bash $0 $DOMAIN" >&2
    exit 1
fi

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_USER="${SUDO_USER:-ubuntu}"
ARCHDIR="/usr/lib/$(uname -m)-linux-gnu"

echo "==> Domain: $DOMAIN | user: $APP_USER | arch: $(uname -m)"

# ---------------------------------------------------------------------------
echo "==> [1/6] Mở port 80/443 trong iptables"
# Oracle image chặn sẵn ở iptables, mở Security List trên Console là CHƯA đủ.
# ---------------------------------------------------------------------------
for port in 80 443; do
    if iptables -C INPUT -p tcp --dport "$port" -j ACCEPT 2>/dev/null; then
        echo "    port $port đã mở, bỏ qua"
    else
        iptables -I INPUT 1 -p tcp --dport "$port" -j ACCEPT
        echo "    mở port $port"
    fi
done
echo iptables-persistent iptables-persistent/autosave_v4 boolean true | debconf-set-selections
echo iptables-persistent iptables-persistent/autosave_v6 boolean true | debconf-set-selections
DEBIAN_FRONTEND=noninteractive apt-get install -y -qq iptables-persistent >/dev/null
netfilter-persistent save >/dev/null
echo "    đã lưu rule (giữ nguyên sau reboot)"

# ---------------------------------------------------------------------------
echo "==> [2/6] .NET 10 ASP.NET Core runtime"
# ---------------------------------------------------------------------------
if command -v dotnet >/dev/null 2>&1 && dotnet --list-runtimes | grep -q "Microsoft.AspNetCore.App 10\."; then
    echo "    đã có, bỏ qua"
else
    tmpdir="$(mktemp -d)"
    wget -q -O "$tmpdir/ms-prod.deb" \
        https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb
    dpkg -i "$tmpdir/ms-prod.deb" >/dev/null
    rm -rf "$tmpdir"
    apt-get update -qq
    apt-get install -y -qq aspnetcore-runtime-10.0
fi
dotnet --list-runtimes | grep "Microsoft.AspNetCore.App 10\." | sed 's/^/    /'

# ---------------------------------------------------------------------------
echo "==> [3/6] Tesseract + symlink native lib"
# Tesseract NuGet 5.2.0 chỉ đóng gói native cho Windows (x64/tesseract50.dll,
# x64/leptonica-1.82.0.dll) nên trên Linux nó DllImport theo đúng 2 tên đó,
# còn Ubuntu lại đặt tên libtesseract.so.5 / liblept.so.5 -> phải symlink.
# Thiếu bước này app crash ngay lần scan đầu.
# ---------------------------------------------------------------------------
apt-get install -y -qq tesseract-ocr libtesseract-dev libleptonica-dev

tess_real="$(ls "$ARCHDIR"/libtesseract.so.* 2>/dev/null | head -1 || true)"
lept_real="$(ls "$ARCHDIR"/liblept.so.* "$ARCHDIR"/libleptonica.so.* 2>/dev/null | head -1 || true)"

if [[ -z "$tess_real" ]]; then
    echo "    LỖI: không thấy libtesseract.so.* trong $ARCHDIR" >&2
    ls "$ARCHDIR" | grep -i tesseract >&2 || true
    exit 1
fi
if [[ -z "$lept_real" ]]; then
    echo "    LỖI: không thấy liblept*.so.* trong $ARCHDIR" >&2
    ls "$ARCHDIR" | grep -i lept >&2 || true
    exit 1
fi

ln -sf "$tess_real" "$ARCHDIR/libtesseract50.so"
ln -sf "$lept_real" "$ARCHDIR/libleptonica-1.82.0.so"
ldconfig
echo "    libtesseract50.so       -> $tess_real"
echo "    libleptonica-1.82.0.so  -> $lept_real"

# ---------------------------------------------------------------------------
echo "==> [4/6] Caddy (reverse proxy + SSL tự động)"
# ---------------------------------------------------------------------------
if command -v caddy >/dev/null 2>&1; then
    echo "    đã có, bỏ qua"
else
    apt-get install -y -qq debian-keyring debian-archive-keyring apt-transport-https curl gnupg
    rm -f /usr/share/keyrings/caddy-stable-archive-keyring.gpg
    curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
        | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
    curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
        > /etc/apt/sources.list.d/caddy-stable.list
    apt-get update -qq
    apt-get install -y -qq caddy
fi

# ---------------------------------------------------------------------------
echo "==> [5/6] Thư mục + file cấu hình"
# ---------------------------------------------------------------------------
mkdir -p /opt/lottery-api /var/www/lottery /var/lib/lottery
chown -R "$APP_USER:$APP_USER" /opt/lottery-api /var/www/lottery /var/lib/lottery
chmod 755 /var/www/lottery      # caddy chạy user khác, cần đọc được

if [[ -f /etc/lottery-api.env ]]; then
    echo "    /etc/lottery-api.env đã có, giữ nguyên"
else
    printf 'CloudOcr__ApiKey=\n' > /etc/lottery-api.env
    chmod 600 /etc/lottery-api.env
    echo "    tạo /etc/lottery-api.env (CHƯA có key — xem phần việc còn lại ở dưới)"
fi

sed "s|__APP_USER__|$APP_USER|g" "$HERE/lottery-api.service" \
    > /etc/systemd/system/lottery-api.service
sed "s|__DOMAIN__|$DOMAIN|g" "$HERE/Caddyfile.template" > /etc/caddy/Caddyfile
echo "    đã ghi lottery-api.service + /etc/caddy/Caddyfile ($DOMAIN)"

# ---------------------------------------------------------------------------
echo "==> [6/6] Bật service"
# ---------------------------------------------------------------------------
systemctl daemon-reload
systemctl enable lottery-api >/dev/null 2>&1
systemctl reload-or-restart caddy

if [[ -f /opt/lottery-api/LotteryChecker.Api.dll ]]; then
    systemctl restart lottery-api
    sleep 3
    systemctl is-active --quiet lottery-api \
        && echo "    lottery-api: active" \
        || echo "    lottery-api CHƯA chạy được — xem: journalctl -u lottery-api -n 50"
else
    echo "    chưa có code trong /opt/lottery-api (bình thường ở lần chạy đầu)"
fi

cat <<EOF

===========================================================================
Server sẵn sàng. Việc còn lại:

1. Điền key OCR.space vào /etc/lottery-api.env  (lấy key đang dùng ở máy dev:
   dotnet user-secrets list  — trong backend/LotteryChecker.Api)
       sudo nano /etc/lottery-api.env
       sudo systemctl restart lottery-api

2. Từ máy Windows, đẩy code lên:
       .\deploy\publish.ps1 -Server $APP_USER@<IP> -Key D:\Projects\lottery.key

3. Kiểm tra:
       curl http://127.0.0.1:5000/health
       journalctl -u lottery-api -n 40 --no-pager | grep "Khởi động"
       curl https://$DOMAIN/health
===========================================================================
EOF
