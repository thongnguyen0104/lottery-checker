<#
  Dua app ra internet qua Cloudflare Tunnel, chay tu chinh may Windows nay.
  Dung khi chua co VM (vd Oracle ARM dang het capacity) ma muon co link HTTPS ngay.

      .\deploy\tunnel.ps1

  Script se:
    1. Doc CloudOcr:ApiKey tu dotnet user-secrets (khong can ban dan tay)
    2. Chay backend o che do Production tren 127.0.0.1:5177
    3. Build frontend roi chay `vite preview` (ban build that, service worker PWA hoat dong)
    4. Mo Cloudflare Tunnel tro vao vite preview -> in ra link https://....trycloudflare.com

  Ctrl+C de dung tat ca. Yeu cau: cloudflared (winget install --id Cloudflare.cloudflared)

  LUU Y: file nay giu thuan ASCII (PowerShell 5.1 doc .ps1 theo ANSI).
#>
param(
    [int]$ApiPort = 5177,
    [int]$WebPort = 4173,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$api  = Join-Path $root 'backend\LotteryChecker.Api'
$fe   = Join-Path $root 'frontend'
$procs = @()

function Step([string]$msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Warn([string]$msg) { Write-Host "!!  $msg" -ForegroundColor Yellow }

if (-not (Get-Command cloudflared -ErrorAction SilentlyContinue)) {
    Write-Host "Chua co cloudflared. Cai bang lenh sau roi mo lai PowerShell:" -ForegroundColor Red
    Write-Host "    winget install --id Cloudflare.cloudflared"
    exit 1
}

# ------------------------------------------------- key OCR tu user-secrets
Step "Doc CloudOcr:ApiKey tu user-secrets"
$csproj = Join-Path $api 'LotteryChecker.Api.csproj'
$idMatch = Select-String -Path $csproj -Pattern '<UserSecretsId>(.+?)</UserSecretsId>'
if ($idMatch) {
    $secretsId   = $idMatch.Matches[0].Groups[1].Value
    $secretsFile = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId\secrets.json"
    if (Test-Path $secretsFile) {
        $secrets = Get-Content $secretsFile -Raw | ConvertFrom-Json
        $key = $secrets.'CloudOcr:ApiKey'
        if ($key) {
            $env:CloudOcr__ApiKey = $key
            Write-Host "    tim thay key (dai $($key.Length) ky tu)"
        }
    }
}
if (-not $env:CloudOcr__ApiKey) {
    Warn "Khong tim thay CloudOcr:ApiKey -> cloud OCR se tat, chi dung Tesseract cuc bo."
    Warn "Set bang: dotnet user-secrets set 'CloudOcr:ApiKey' '<key>'  (trong backend\LotteryChecker.Api)"
}

try {
    # ------------------------------------------------------------- backend
    Step "Chay backend (Production) tren http://127.0.0.1:$ApiPort"
    # Production de KHONG mo /api/admin/* va /scalar/v1 ra internet qua tunnel.
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    $env:ASPNETCORE_URLS        = "http://127.0.0.1:$ApiPort"
    $procs += Start-Process -FilePath 'dotnet' `
        -ArgumentList 'run', '--no-launch-profile', '--project', "`"$api`"" `
        -WorkingDirectory $api -PassThru

    # ------------------------------------------------------------ frontend
    if (-not $SkipBuild) {
        Step "Build frontend"
        Push-Location $fe
        try {
            npm run build
            if ($LASTEXITCODE -ne 0) { throw "npm run build that bai" }
        }
        finally { Pop-Location }
    }

    Step "Chay vite preview tren http://127.0.0.1:$WebPort"
    $procs += Start-Process -FilePath 'npm' `
        -ArgumentList 'run', 'preview' `
        -WorkingDirectory $fe -PassThru

    Step "Cho backend san sang"
    $ready = $false
    foreach ($i in 1..30) {
        Start-Sleep -Seconds 2
        try {
            $r = Invoke-WebRequest "http://127.0.0.1:$ApiPort/health" -TimeoutSec 3 -UseBasicParsing
            if ($r.StatusCode -eq 200) { $ready = $true; break }
        }
        catch { }
    }
    if ($ready) {
        Write-Host "    backend OK" -ForegroundColor Green
    }
    else {
        Warn "Backend chua tra /health sau 60s - tunnel van mo, xem log o cua so dotnet."
    }

    # -------------------------------------------------------------- tunnel
    Step "Mo Cloudflare Tunnel (link https:// se hien ngay duoi day)"
    Write-Host "    Ctrl+C de dung tat ca." -ForegroundColor DarkGray
    Write-Host ""
    cloudflared tunnel --url "http://localhost:$WebPort"
}
finally {
    Write-Host ""
    Step "Dong backend + vite preview"
    foreach ($p in $procs) {
        if ($p -and -not $p.HasExited) {
            try { Stop-Process -Id $p.Id -Force -ErrorAction Stop } catch { }
        }
    }
}
