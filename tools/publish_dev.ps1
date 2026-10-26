# 发布 Development 版本到 GitHub Release
#
# 用法（在项目根目录）：
#     .\tools\publish_dev.ps1
#
# 做的事：
#   1. 把 Builds/Development 打成 zip
#   2. 用当前 git 短哈希做标签（如 dev-8ac71a5）
#   3. 发到 GitHub Release（没有就创建）
#   4. 朋友那侧的 update_and_run.ps1 会自动发现并更新

param(
    [string]$BuildDir = "Builds/Development",
    [string]$Repo = "lv-fool/nailong-five-nights",
    [switch]$SkipUpload
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# ---------- 1. 取版本号（git 短哈希） ----------
$hash = (git rev-parse --short HEAD).Trim()
$dirty = (git status --porcelain)
if ($dirty) {
    Write-Warning "工作区有未提交改动 —— 打出来的包和 tag 对不上，建议先提交。"
}
$tag = "dev-$hash"
Write-Host "版本标签: $tag" -ForegroundColor Cyan

# ---------- 2. 检查构建产物 ----------
if (-not (Test-Path $BuildDir)) {
    throw "找不到构建目录: $BuildDir（先在 Unity 里构建 Development 版）"
}

$exe = Get-ChildItem $BuildDir -Filter "*.exe" |
       Where-Object { $_.Name -notmatch "UnityCrashHandler" } |
       Select-Object -First 1
if (-not $exe) { throw "$BuildDir 里没有 exe" }
Write-Host "游戏: $($exe.Name)" -ForegroundColor Cyan

# ---------- 3. 打包 ----------
$outDir = "Builds/_releases"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$zip = Join-Path $outDir "$tag.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Write-Host "打包中（284MB 左右，要一会儿）..." -ForegroundColor Yellow
Compress-Archive -Path "$BuildDir\*" -DestinationPath $zip -CompressionLevel Optimal

$sizeMB = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "打包完成: $zip ($sizeMB MB)" -ForegroundColor Green

if ($SkipUpload) {
    Write-Host "已跳过上传（-SkipUpload）。" -ForegroundColor Yellow
    return
}

# ---------- 4. 上传到 GitHub Release ----------
# 用 GitHub API。注意本机 PowerShell 的 TLS 有 schannel 凭证问题，
# 所以统一走 curl.exe（自带 OpenSSL）。
$token = $env:GITHUB_TOKEN
if (-not $token) {
    Write-Host ""
    Write-Host "没有 GITHUB_TOKEN，无法自动上传。" -ForegroundColor Red
    Write-Host "两种办法：" -ForegroundColor Yellow
    Write-Host "  1. 设置 $env:GITHUB_TOKEN = ghp_xxx 再跑一次"
    Write-Host "  2. 手动把 zip 拖到 https://github.com/$Repo/releases/new"
    Write-Host ""
    Write-Host "zip 已经打好了，在: $((Resolve-Path $zip).Path)" -ForegroundColor Cyan
    return
}

Write-Host "创建 Release $tag ..." -ForegroundColor Cyan

$body = @{
    tag_name = $tag
    name = "Development $tag"
    body = "自动发布的开发版构建。提交: $hash  构建时间: $(Get-Date -Format yyyy-MM-dd HH:mm)"
    draft = $false
    prerelease = $true
} | ConvertTo-Json

$createUrl = "https://api.github.com/repos/$Repo/releases"
$resp = curl.exe -s -X POST $createUrl -H "Authorization: Bearer $token" -H "Accept: application/vnd.github+json" -H "Content-Type: application/json" -d $body
$release = $resp | ConvertFrom-Json
if (-not $release.id) {
    Write-Warning "创建 Release 失败: $resp"
    return
}
Write-Host "Release id = $($release.id)" -ForegroundColor Green

$assetName = "nailong-dev.zip"
$uploadUrl = "https://uploads.github.com/repos/$Repo/releases/$($release.id)/assets?name=$assetName"

Write-Host "上传中（可能要几分钟）..." -ForegroundColor Yellow
curl.exe -s -X POST $uploadUrl -H "Authorization: Bearer $token" -H "Content-Type: application/zip" -H "Accept: application/vnd.github+json" --data-binary "@$zip" | Out-Null

Write-Host ""
Write-Host "发布完成: https://github.com/$Repo/releases/tag/$tag" -ForegroundColor Green
Write-Host "朋友那边运行 tools/update_and_run.ps1 就会自动更新到这一版。" -ForegroundColor Cyan