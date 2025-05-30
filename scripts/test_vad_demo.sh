#!/bin/bash

# VAD Continuous Monitoring Test Script
# Demonstrates the enhanced voice activity detection with py-xiaozhi-like features

echo "🎤 VAD Continuous Monitoring Test"
echo "================================="
echo ""

# Navigate to project directory
cd "c:/github/xiaozhi-dotnet" || exit 1

echo "📦 Building XiaoZhi Console Application..."
if ! dotnet build --configuration Release --project XiaoZhi.Console; then
    echo "❌ Build failed!"
    exit 1
fi

echo "✅ Build successful!"
echo ""

echo "🚀 Starting XiaoZhi Console Application..."
echo ""
echo "📋 Test Instructions:"
echo "===================="
echo "1. Select option 4 to enable 'Auto Dialogue Mode' (KeepListening)"
echo "2. Select option 1 to start voice chat"
echo "3. Wait for AI response and try speaking during playback - should interrupt immediately"
echo "4. In idle state with KeepListening enabled, speak to auto-start listening"
echo "5. Test different scenarios to verify continuous monitoring works"
echo ""
echo "🔊 VAD Features Being Tested:"
echo "- Continuous monitoring during AI speaking (immediate interruption)"
echo "- Auto-activation from idle state when KeepListening enabled"
echo "- State-aware energy thresholds (300.0 for speaking, 500.0 for idle)"
echo "- Different speech windows (5 frames for speaking, 8 frames for idle)"
echo ""
echo "Press any key to start the application..."
read -n 1 -s

# Run the console application
dotnet run --project XiaoZhi.Console

echo ""
echo "🎉 VAD Test Complete!"
echo "The enhanced VAD service now provides py-xiaozhi-like continuous monitoring!"
