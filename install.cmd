@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo.
echo   虛幻競技場 99 — 安裝
echo   ============================================
echo.
echo   [1/2] 建置遊戲...
echo.

dotnet publish src\Unreal99\Unreal99.csproj -c Release -r win-x64 --self-contained false -o dist
if errorlevel 1 goto :failed

echo.
echo   [2/2] 建立開始選單捷徑...
echo.

"%~dp0dist\Unreal99.exe" --install-shortcut
if errorlevel 1 goto :failed

echo.
echo   完成。在開始選單搜尋「虛幻競技場」即可啟動。
echo.
pause
exit /b 0

:failed
echo.
echo   安裝失敗，請檢查上方訊息。
echo.
pause
exit /b 1
