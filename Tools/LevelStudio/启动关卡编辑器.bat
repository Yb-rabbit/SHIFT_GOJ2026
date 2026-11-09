@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [X] 没找到 dotnet^: 先装 .NET SDK ^(8 或更高^)，再双击这个文件。
  pause
  exit /b 1
)

echo 正在启动 BitFlow 关卡编辑器……
echo 关掉这个窗口 = 关闭编辑器。
echo.

dotnet run -c Release --project "%~dp0ShiftGoLevelStudio.csproj"

if errorlevel 1 pause
