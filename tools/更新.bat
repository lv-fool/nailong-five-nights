@echo off
chcp 65001 >nul
setlocal

rem ============================================
rem  奶龙的五夜后宫 - 自动更新并启动
rem  双击即可。需要 Windows + 能访问 GitHub。
rem  不需要安装 Unity。
rem ============================================

cd /d "%~dp0"

echo.
echo  ===== 奶龙的五夜后宫 . 检查更新 =====
echo.

rem 用 PowerShell 跑真正的更新逻辑（-ExecutionPolicy Bypass 绕过脚本限制）
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0update_and_run.ps1"

set EXITCODE=%ERRORLEVEL%

if %EXITCODE% neq 0 (
    echo.
    echo  --------------------------------------------------
    echo   出错了（退出码 %EXITCODE%）。把上面的信息截图发给作者。
    echo  --------------------------------------------------
    echo.
    pause
)

endlocal