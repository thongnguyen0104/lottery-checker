<#
  Build backend + frontend, day len server, restart service. Chay tu may Windows.

      .\deploy\publish.ps1 -Server ubuntu@1.2.3.4 -Key D:\Projects\lottery.key

  Chi day 1 phan:
      .\deploy\publish.ps1 -Server ... -Key ... -BackendOnly
      .\deploy\publish.ps1 -Server ... -Key ... -FrontendOnly

  Dong goi thanh 1 file .tgz roi moi scp (nhanh hon scp -r nhieu file vi tessdata ~31MB).

  LUU Y: file nay co y giu thuan ASCII. Windows PowerShell 5.1 doc .ps1 theo ANSI,
  tieng Viet co dau khong kem BOM se lam vo cu phap script.
#>
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$Key,
    [switch]$BackendOnly,
    [switch]$FrontendOnly
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$api  = Join-Path $root 'backend\LotteryChecker.Api'
$fe   = Join-Path $root 'frontend'
$work = Join-Path $env:TEMP 'lottery-deploy'

function Assert-Ok([string]$label) {
    if ($LASTEXITCODE -ne 0) { throw "$label that bai (exit $LASTEXITCODE)" }
}

function Step([string]$msg) {
    Write-Host "==> $msg" -ForegroundColor Cyan
}

if (-not (Test-Path $Key)) { throw "Khong thay private key: $Key" }
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Path $work | Out-Null

$doBackend  = -not $FrontendOnly
$doFrontend = -not $BackendOnly

# ---------------------------------------------------------------- backend
if ($doBackend) {
    Step "Publish backend"
    dotnet publish $api -c Release -o "$work\api" --nologo
    Assert-Ok "dotnet publish"

    if (-not (Test-Path "$work\api\tessdata\vie.traineddata")) {
        throw "publish thieu tessdata/vie.traineddata - OCR se chet tren server"
    }

    Step "Nen + day backend"
    tar -czf "$work\api.tgz" -C "$work\api" .
    Assert-Ok "tar backend"
    scp -i $Key "$work\api.tgz" "${Server}:/tmp/api.tgz"
    Assert-Ok "scp backend"

    Step "Giai nen + restart service"
    $remote = 'tar xzf /tmp/api.tgz -C /opt/lottery-api; rm -f /tmp/api.tgz; sudo systemctl restart lottery-api'
    ssh -i $Key $Server $remote
    Assert-Ok "restart lottery-api"
}

# --------------------------------------------------------------- frontend
if ($doFrontend) {
    Step "Build frontend"
    Push-Location $fe
    try {
        # VITE_API_URL phai rong: FE goi /api cung origin, Caddy lo phan con lai
        npm run build
        Assert-Ok "npm run build"
    }
    finally { Pop-Location }

    Step "Nen + day frontend"
    tar -czf "$work\web.tgz" -C "$fe\dist" .
    Assert-Ok "tar frontend"
    scp -i $Key "$work\web.tgz" "${Server}:/tmp/web.tgz"
    Assert-Ok "scp frontend"
    ssh -i $Key $Server 'tar xzf /tmp/web.tgz -C /var/www/lottery; rm -f /tmp/web.tgz'
    Assert-Ok "giai nen frontend"
}

# ------------------------------------------------------------------ verify
Step "Kiem tra"
# Khong grep theo tu tieng Viet (log co dau, pattern ASCII se khong match) - in thang 20 dong cuoi
ssh -i $Key $Server 'curl -s -m 10 http://127.0.0.1:5000/health; echo; journalctl -u lottery-api -n 20 --no-pager'

Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "==> Xong. Mo https://<domain> tren dien thoai de test camera." -ForegroundColor Green
