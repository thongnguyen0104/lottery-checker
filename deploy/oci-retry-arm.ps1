<#
  Thu tao VM ARM (VM.Standard.A1.Flex) lien tuc cho toi khi Oracle co capacity.
  Chay nen tren may Windows, gap "Out of capacity" thi ngu roi thu lai.

      .\deploy\oci-retry-arm.ps1 -KeyPub D:\Projects\lottery.key.pub

  Tuy chon:
      -Ocpus 1 -MemoryGb 6        # mac dinh; 1 OCPU de chen vao capacity phan manh hon
      -IntervalSeconds 90         # nhip thu lai
      -SubnetName "public subnet-vcn-lottery"
      -DisplayName lottery

  Yeu cau: OCI CLI da cau hinh (~/.oci/config). Cai + cau hinh:
      winget install --id Oracle.OCI-CLI
      oci setup config            # dan tenancy OCID / user OCID / region, no tu tao API key
      # roi vao Console: Profile -> User settings -> API keys -> Add API key -> paste public key
  Kiem tra: oci iam region list

  LUU Y: file nay giu thuan ASCII (PowerShell 5.1 doc .ps1 theo ANSI).
#>
param(
    [string]$KeyPub          = 'D:\Projects\lottery.key.pub',
    [string]$KeyPrivate      = 'D:\Projects\lottery.key',
    [int]$Ocpus              = 1,
    [int]$MemoryGb           = 6,
    [int]$IntervalSeconds    = 90,
    [string]$SubnetName      = 'public subnet-vcn-lottery',
    [string]$DisplayName     = 'lottery',
    [string]$Shape           = 'VM.Standard.A1.Flex'
)

$ErrorActionPreference = 'Stop'

function Step([string]$m) { Write-Host "==> $m" -ForegroundColor Cyan }
function Info([string]$m) { Write-Host "    $m" -ForegroundColor DarkGray }

if (-not (Get-Command oci -ErrorAction SilentlyContinue)) {
    Write-Host "Chua co OCI CLI. Cai roi cau hinh:" -ForegroundColor Red
    Write-Host "    winget install --id Oracle.OCI-CLI"
    Write-Host "    oci setup config"
    exit 1
}

# ------------------------------------------------------- public key cho SSH
if (-not (Test-Path $KeyPub)) {
    if (Test-Path $KeyPrivate) {
        Step "Chua co public key -> sinh tu private key"
        ssh-keygen -y -f $KeyPrivate | Out-File -FilePath $KeyPub -Encoding ascii
        if ($LASTEXITCODE -ne 0) { throw "ssh-keygen that bai" }
        Info "da tao $KeyPub"
    }
    else {
        throw "Khong thay $KeyPub lan $KeyPrivate. Can public key de SSH vao VM."
    }
}

# --------------------------------------------------------------- tenancy id
Step "Doc tenancy OCID tu ~/.oci/config"
$cfgPath = Join-Path $env:USERPROFILE '.oci\config'
if (-not (Test-Path $cfgPath)) { throw "Khong thay $cfgPath. Chay: oci setup config" }
$tenancy = (Select-String -Path $cfgPath -Pattern '^\s*tenancy\s*=\s*(\S+)' |
            Select-Object -First 1).Matches[0].Groups[1].Value
if (-not $tenancy) { throw "Khong doc duoc tenancy trong $cfgPath" }
Info $tenancy

function Invoke-Oci([string[]]$OciArgs) {
    $raw = & oci @OciArgs 2>&1
    $text = ($raw | Out-String)
    return [pscustomobject]@{ Text = $text; Code = $LASTEXITCODE }
}

# --------------------------------------------------- availability domain(s)
Step "Liet ke availability domain"
$r = Invoke-Oci @('iam', 'availability-domain', 'list', '--compartment-id', $tenancy)
if ($r.Code -ne 0) { throw "Loi goi OCI CLI:`n$($r.Text)" }
$ads = ($r.Text | ConvertFrom-Json).data | ForEach-Object { $_.name }
foreach ($ad in $ads) { Info $ad }

# ------------------------------------------------------------------- subnet
Step "Tim subnet '$SubnetName'"
$r = Invoke-Oci @('network', 'subnet', 'list', '--compartment-id', $tenancy,
                  '--display-name', $SubnetName)
if ($r.Code -ne 0) { throw "Loi tim subnet:`n$($r.Text)" }
$subnet = ($r.Text | ConvertFrom-Json).data | Select-Object -First 1
if (-not $subnet) { throw "Khong thay subnet ten '$SubnetName'. Kiem tra lai ten trong Console." }
$subnetId = $subnet.id
Info $subnetId

# -------------------------------------------------------------------- image
Step "Tim image Ubuntu 24.04 moi nhat cho $Shape"
$r = Invoke-Oci @('compute', 'image', 'list', '--compartment-id', $tenancy,
                  '--operating-system', 'Canonical Ubuntu',
                  '--operating-system-version', '24.04',
                  '--shape', $Shape,
                  '--sort-by', 'TIMECREATED', '--sort-order', 'DESC', '--limit', '5')
if ($r.Code -ne 0) { throw "Loi tim image:`n$($r.Text)" }
$image = ($r.Text | ConvertFrom-Json).data | Select-Object -First 1
if (-not $image) { throw "Khong tim thay image Ubuntu 24.04 cho $Shape" }
$imageId = $image.id
Info "$($image.'display-name')"

# ------------------------------------------------- shape config qua file://
# Truyen JSON tren dong lenh Windows rat de bi mangled -> ghi ra file cho chac.
$shapeFile = Join-Path $env:TEMP 'oci-shape-config.json'
"{""ocpus"":$Ocpus,""memoryInGBs"":$MemoryGb}" | Out-File -FilePath $shapeFile -Encoding ascii
$shapeArg = 'file://' + ($shapeFile -replace '\\', '/')

Write-Host ""
Step "Bat dau thu tao: $Shape - $Ocpus OCPU / $MemoryGb GB - moi $IntervalSeconds giay"
Info "Ctrl+C de dung. Cu de cua so nay chay nen."
Write-Host ""

$attempt = 0
while ($true) {
    foreach ($ad in $ads) {
        $attempt++
        $stamp = (Get-Date).ToString('HH:mm:ss')
        Write-Host "[$stamp] lan $attempt - $ad ... " -NoNewline

        $r = Invoke-Oci @('compute', 'instance', 'launch',
                          '--availability-domain', $ad,
                          '--compartment-id', $tenancy,
                          '--shape', $Shape,
                          '--shape-config', $shapeArg,
                          '--subnet-id', $subnetId,
                          '--assign-public-ip', 'true',
                          '--image-id', $imageId,
                          '--display-name', $DisplayName,
                          '--ssh-authorized-keys-file', $KeyPub,
                          '--wait-for-state', 'RUNNING')

        if ($r.Code -eq 0) {
            Write-Host "TAO DUOC!" -ForegroundColor Green
            $inst = ($r.Text | ConvertFrom-Json).data
            $instId = $inst.id

            $v = Invoke-Oci @('compute', 'instance', 'list-vnics', '--instance-id', $instId)
            $ip = ($v.Text | ConvertFrom-Json).data | Select-Object -First 1 |
                  ForEach-Object { $_.'public-ip' }

            Write-Host ""
            Write-Host "=========================================================" -ForegroundColor Green
            Write-Host " VM da chay. Public IP: $ip" -ForegroundColor Green
            Write-Host "=========================================================" -ForegroundColor Green
            Write-Host ""
            Write-Host "Viec tiep theo (xem .claude/deploy-guide.md):"
            Write-Host "  1. Doi Public IP sang Reserved (Console: Instance > Attached VNICs > IPv4)"
            Write-Host "  2. Security List cua vcn-lottery: mo Ingress TCP 80 va 443"
            Write-Host "  3. DuckDNS: dat current ip = $ip  (roi nslookup dove-so.duckdns.org)"
            Write-Host "  4. scp -i $KeyPrivate -r .\deploy ubuntu@${ip}:~/"
            Write-Host "  5. ssh -i $KeyPrivate ubuntu@$ip"
            Write-Host "     sudo bash ~/deploy/setup-server.sh dove-so.duckdns.org"
            Write-Host "  6. .\deploy\publish.ps1 -Server ubuntu@$ip -Key $KeyPrivate"
            return
        }

        if ($r.Text -match 'Out of (host )?capacity') {
            Write-Host "het capacity" -ForegroundColor DarkYellow
        }
        elseif ($r.Text -match 'TooManyRequests|429') {
            Write-Host "bi throttle -> ngu them 5 phut" -ForegroundColor Yellow
            Start-Sleep -Seconds 300
        }
        elseif ($r.Text -match 'LimitExceeded|QuotaExceeded') {
            Write-Host "vuot han muc" -ForegroundColor Red
            Write-Host $r.Text
            Write-Host "Han muc Always Free ARM hien la 2 OCPU / 12 GB TONG cho ca tenancy." -ForegroundColor Yellow
            Write-Host "Neu dang co VM ARM khac thi xoa hoac giam -Ocpus/-MemoryGb." -ForegroundColor Yellow
            return
        }
        else {
            Write-Host "loi khac" -ForegroundColor Red
            Write-Host $r.Text
            return
        }
    }
    Start-Sleep -Seconds $IntervalSeconds
}
