@echo off
chcp 65001 >nul
title To Love · 下载依赖
cd /d "%~dp0"

echo.
echo   这个脚本会下载运行需要的引擎和模型（约 4.4 GB），
echo   下载完成后就能直接双击「启动本地AI对话.exe」。
echo.
echo   1) 引擎 + 模型（默认）
echo   2) 只补引擎（模型已有）
echo   3) 走 Ollama 方案（安装 Ollama 并拉 qwen2.5:7b）
echo   4) 只检查现状，不下载
echo.
set /p choice=请输入 1/2/3/4 然后回车（直接回车 = 1）: 

if "%choice%"=="" set choice=1

if "%choice%"=="1" powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1"
if "%choice%"=="2" powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -SkipModel
if "%choice%"=="3" powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -Mode ollama
if "%choice%"=="4" powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1" -VerifyOnly

echo.
echo   跑完了。日志在 setup.log
pause
