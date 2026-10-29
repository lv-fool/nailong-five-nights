# 奶龙的五夜后宫 —— 自动更新并启动
# 朋友双击「更新.bat」就会调这个脚本。

param(
    [string]$Repo = "lv-fool/nailong-five-nights",
    [switch]$NoLaunch
)

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$gameDir   = Join-Path $scriptDir "game"
$verFile   = Join-Path $gameDir ".version"


# 拿 GitHub API 的 JSON：先试 curl，失败再试 PowerShell 内建 TLS。
# 两条路都失败就返回 $null（调用方处理）。
function Get-GitHubJson([string]$Url) {
    # 路线 1：curl.exe（Win10 1803+ 自带）
    try {
        $out = curl.exe -s -L --max-time 30 -H "Accept: application/vnd.github+json" -H "User-Agent: nailong-updater" $Url 2>$null
        if ($out -and $out.Length -gt 10 -and $out -notmatch "^s*$") {
            return $out | ConvertFrom-Json
        }
    } catch { }

    # 路线 2：PowerShell 内建（.NET TLS）
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $r = Invoke-RestMethod -Uri $Url -TimeoutSec 30 -Headers @{ "User-Agent" = "nailong-updater" }
        if ($r) { return $r }
    } catch { }

    return $null
}

function Start-Game {
    $exe = Get-ChildItem $gameDir -Filter "*.exe" -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -notmatch "UnityCrashHandler" } |
           Select-Object -First 1
    if (-not $exe) { Write-Host "找不到游戏 exe。" -ForegroundColor Red; return }
    Write-Host "启动: $($exe.Name)" -ForegroundColor Cyan
    Start-Process -FilePath $exe.FullName -WorkingDirectory $gameDir
}

Write-Host ""
Write-Host "=== 奶龙的五夜后宫 . 自动更新 ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "检查更新中..." -ForegroundColor Yellow

# 用列表接口而不是 /latest：latest 不返回 prerelease 且有缓存延迟
$apiUrl = "https://api.github.com/repos/$Repo/releases"
$releases = Get-GitHubJson $apiUrl

if (-not $releases) {
    Write-Host "连不上 GitHub，跳过更新检查。" -ForegroundColor Yellow
    if (Test-Path $gameDir) { Start-Game } else { Write-Host "也没有本地游戏，无法启动。" -ForegroundColor Red }
    exit 1
}

# 取最新一个「已发布且有 zip」的。
# ⚠️ 只有一个 release 时 ConvertFrom-Json 返回单个对象而不是数组，
#   所以要强制包成数组，否则 .assets 的判断会失误（实测选不中）。
$list = @($releases)

$release = $null
foreach ($r in $list) {
    if ($r.draft) { continue }
    $zipAssets = @($r.assets | Where-Object { $_.name -like "*.zip" })
    if ($zipAssets.Length -eq 0) { continue }
    $release = $r
    break
}

if (-not $release) {
    Write-Host "还没有可用的发布版本。" -ForegroundColor Yellow
    if (Test-Path $gameDir) { Start-Game }
    exit 1
}

$latest = $release.tag_name
Write-Host "最新版本: $latest" -ForegroundColor Cyan

$current = ""
if (Test-Path $verFile) { $current = (Get-Content $verFile -Raw).Trim() }

if ($current -eq $latest) {
    Write-Host "已经是最新版本，直接启动。" -ForegroundColor Green
    if (-not $NoLaunch) { Start-Game }
    exit 0
}

if ($current) { Write-Host "本地: $current  ->  更新到: $latest" -ForegroundColor Yellow }
else { Write-Host "首次运行，下载完整游戏。" -ForegroundColor Yellow }

$asset = $release.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1
$sizeMB = [math]::Round($asset.size / 1MB, 1)
Write-Host "下载 $($asset.name)（$sizeMB MB）..." -ForegroundColor Yellow

$tmpZip = Join-Path $env:TEMP "nailong-update-$latest.zip"
if (Test-Path $tmpZip) { Remove-Item $tmpZip -Force }

# 下载也做两条路线
$downloaded = $false
try {
    curl.exe -L --max-time 900 --progress-bar -o $tmpZip $asset.browser_download_url
    if ((Test-Path $tmpZip) -and (Get-Item $tmpZip).Length -gt 1000000) { $downloaded = $true }
} catch { }

if (-not $downloaded) {
    Write-Host "curl 失败，改用 PowerShell 下载..." -ForegroundColor Yellow
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $tmpZip -TimeoutSec 900
        if ((Test-Path $tmpZip) -and (Get-Item $tmpZip).Length -gt 1000000) { $downloaded = $true }
    } catch { }
}

if (-not $downloaded) {
    Write-Host "下载失败（检查网络）。" -ForegroundColor Red
    Read-Host "按回车退出"
    exit 1
}

Write-Host "解压中..." -ForegroundColor Yellow

# 先解到临时目录再整体替换，避免半残
$staging = Join-Path $env:TEMP "nailong-staging-$latest"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null
Expand-Archive -Path $tmpZip -DestinationPath $staging -Force

if (Test-Path $gameDir) {
    $running = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like "*五夜*" }
    if ($running) {
        Write-Host ""
        Write-Host "游戏正在运行，请先关掉它再运行这个脚本。" -ForegroundColor Red
        Read-Host "按回车退出"
        exit 1
    }
    Remove-Item $gameDir -Recurse -Force
}

Move-Item $staging $gameDir
$latest | Out-File -FilePath $verFile -Encoding ascii -NoNewline
Remove-Item $tmpZip -Force -ErrorAction SilentlyContinue

Write-Host "更新完成: $latest" -ForegroundColor Green

if (-not $NoLaunch) { Start-Game }