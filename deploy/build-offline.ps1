# Build sẵn API + frontend trên máy dev (có Internet) và đóng gói để chép lên máy chủ không có Internet.
# Chạy trong PowerShell tại thư mục DTNTB:  .\deploy\build-offline.ps1
# Kết quả: deploy\out\dtntb-api.tar.gz và deploy\out\dtntb-frontend.tar.gz
$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent                       # ...\DTNTB
$fe = Join-Path (Split-Path $root -Parent) 'DTNTB-Frontend'     # ...\DTNTB-Frontend
$out = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force -Path $out | Out-Null

Write-Host '== 1/3 Publish API (Release) ==' -ForegroundColor Cyan
$publish = Join-Path $root 'publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'DTNTB.API\DTNTB.API.csproj') -c Release -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish lỗi' }
# Không mang cấu hình/bí mật của máy dev lên máy chủ.
Remove-Item (Join-Path $publish 'appsettings.Local.json'), (Join-Path $publish 'appsettings.Development.json') -ErrorAction SilentlyContinue
if (-not (Test-Path (Join-Path $publish 'Fonts\DejaVuSans.ttf'))) { throw 'Thiếu Fonts\DejaVuSans.ttf trong bản publish' }

Write-Host '== 2/3 Build frontend ==' -ForegroundColor Cyan
Push-Location $fe
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci lỗi (chạy npm install một lần để cập nhật package-lock.json rồi thử lại)' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'npm run build lỗi' }
} finally { Pop-Location }

Write-Host '== 3/3 Đóng gói ==' -ForegroundColor Cyan
tar -czf (Join-Path $out 'dtntb-api.tar.gz') -C $root publish
tar -czf (Join-Path $out 'dtntb-frontend.tar.gz') -C $fe dist
Get-ChildItem $out | Format-Table Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
Write-Host 'Xong. Chép 2 file trong deploy\out lên máy chủ, xem deploy\OFFLINE_DEPLOY.md' -ForegroundColor Green
