#!/usr/bin/env pwsh

# InterruptArchitectureTest VAD演示脚本
# 测试新实现的真实VAD功能

Write-Host "=== InterruptArchitectureTest VAD演示 ===" -ForegroundColor Green
Write-Host "正在启动真实VAD测试程序..." -ForegroundColor Yellow

# 进入项目目录
Set-Location "C:\github\Verdure.Assistant\tests\InterruptArchitectureTest"

# 检查依赖项
Write-Host "`n检查项目依赖..." -ForegroundColor Yellow
if (Test-Path "InterruptArchitectureTest.csproj") {
    Write-Host "✅ 项目文件存在" -ForegroundColor Green
} else {
    Write-Host "❌ 项目文件不存在" -ForegroundColor Red
    exit 1
}

# 构建项目
Write-Host "`n构建项目..." -ForegroundColor Yellow
$buildResult = dotnet build --configuration Release 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ 项目构建成功" -ForegroundColor Green
} else {
    Write-Host "❌ 项目构建失败:" -ForegroundColor Red
    Write-Host $buildResult -ForegroundColor Red
    exit 1
}

Write-Host "`n=== VAD功能演示 ===" -ForegroundColor Cyan
Write-Host "本演示将展示以下功能:" -ForegroundColor White
Write-Host "1. 🎤 真实麦克风录音和VAD检测" -ForegroundColor Yellow
Write-Host "2. 🔊 基于SoundFlow的音频处理" -ForegroundColor Yellow  
Write-Host "3. ⚡ 实时语音活动打断触发" -ForegroundColor Yellow
Write-Host "4. 📊 音频统计和VAD参数调整" -ForegroundColor Yellow
Write-Host "5. 🎯 多种打断源协同工作" -ForegroundColor Yellow

Write-Host "`n准备开始演示..." -ForegroundColor Yellow
Write-Host "请确保:" -ForegroundColor White
Write-Host "- 麦克风已连接并工作正常" -ForegroundColor Gray
Write-Host "- 音频驱动已安装" -ForegroundColor Gray
Write-Host "- 环境安静，便于测试VAD" -ForegroundColor Gray

Write-Host "`n按任意键开始演示，或按Ctrl+C取消..." -ForegroundColor Green
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

Write-Host "`n启动InterruptArchitectureTest..." -ForegroundColor Yellow
Write-Host "==========================================`n" -ForegroundColor Green

# 运行程序
dotnet run --configuration Release

Write-Host "`n演示结束。" -ForegroundColor Green
Write-Host "感谢使用InterruptArchitectureTest VAD演示！" -ForegroundColor Cyan
