#!/bin/bash

# Verdure Assistant .NET 构建脚本
# 用于在Linux/macOS环境下构建项目

set -e

echo "🚀 开始构建 Verdure Assistant .NET 项目..."

# 检查.NET版本
echo "📋 检查 .NET 版本..."
dotnet --version

# 清理之前的构建
echo "🧹 清理构建目录..."
rm -rf build/
mkdir -p build/{console,winui,packages}

# 还原NuGet包
echo "📦 还原 NuGet 包..."
dotnet restore

# 构建解决方案
echo "🔨 构建解决方案..."
dotnet build --configuration Release --no-restore

# 运行测试
echo "🧪 运行测试..."
dotnet test --configuration Release --no-build --verbosity normal

# 发布控制台应用
echo "📱 发布控制台应用..."
dotnet publish src/Verdure.Assistant.Console/Verdure.Assistant.Console.csproj \
    --configuration Release \
    --output build/console/linux-x64 \
    --runtime linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true

dotnet publish src/Verdure.Assistant.Console/Verdure.Assistant.Console.csproj \
    --configuration Release \
    --output build/console/osx-x64 \
    --runtime osx-x64 \
    --self-contained true \
    -p:PublishSingleFile=true

# 创建NuGet包
echo "📦 创建 NuGet 包..."
dotnet pack src/Verdure.Assistant.Core/Verdure.Assistant.Core.csproj \
    --configuration Release \
    --output build/packages \
    --no-build

echo "✅ 构建完成！"
echo "📁 构建产物位于 build/ 目录"

# 显示构建结果
echo "📊 构建结果："
echo "控制台应用："
ls -la build/console/*/
echo "NuGet包："
ls -la build/packages/
