# 音频文件验证脚本
# 用于快速检查recorded_audio.wav的基本信息

$audioFile = "recorded_audio.wav"

if (Test-Path $audioFile) {
    $fileInfo = Get-Item $audioFile
    $fileSize = $fileInfo.Length
    
    # WAV文件基本计算
    $headerSize = 44
    $audioDataSize = $fileSize - $headerSize
    $samplesCount = $audioDataSize / 2  # 16-bit = 2 bytes per sample
    $durationSeconds = $samplesCount / 16000  # 16kHz sample rate
    
    Write-Host "=== 音频文件验证结果 ===" -ForegroundColor Green
    Write-Host "文件名: $audioFile"
    Write-Host "创建时间: $($fileInfo.CreationTime)"
    Write-Host "文件大小: $([math]::Round($fileSize/1024, 1)) KB"
    Write-Host ""
    Write-Host "=== 音频参数 ===" -ForegroundColor Yellow
    Write-Host "格式: 16kHz, 1ch, 16-bit PCM WAV"
    Write-Host "采样点数: $([math]::Round($samplesCount, 0))"
    Write-Host "录音时长: $([math]::Round($durationSeconds, 2)) 秒"
    Write-Host "音频数据: $audioDataSize bytes"
    Write-Host ""
    Write-Host "=== 质量检查建议 ===" -ForegroundColor Cyan
    Write-Host "1. 播放测试: 双击文件用媒体播放器播放"
    Write-Host "2. 检查清晰度: 语音应清晰无失真"
    Write-Host "3. 验证格式: 确认为16kHz单声道"
    Write-Host "4. 兼容性: 该格式直接兼容Opus编码器"
    Write-Host ""
    
    # 检查文件是否太小（可能录音失败）
    if ($durationSeconds -lt 1) {
        Write-Host "⚠️  警告: 录音时长过短，请重新录音" -ForegroundColor Red
    } else {
        Write-Host "✅ 录音时长正常，可以进行音质测试" -ForegroundColor Green
    }
    
    # 检查文件大小是否合理
    $expectedSize = $durationSeconds * 16000 * 2 + 44  # 理论文件大小
    $sizeDiff = [math]::Abs($fileSize - $expectedSize)
    if ($sizeDiff -lt 100) {
        Write-Host "✅ 文件大小正确，数据完整" -ForegroundColor Green
    } else {
        Write-Host "⚠️  文件大小异常，请检查录音" -ForegroundColor Yellow
    }
} else {
    Write-Host "❌ 未找到音频文件: $audioFile" -ForegroundColor Red
    Write-Host "请先运行 'dotnet run' 进行录音测试"
}

Write-Host ""
Write-Host "要重新录音，请运行: dotnet run" -ForegroundColor Magenta
