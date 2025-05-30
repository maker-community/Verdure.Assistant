@echo off
REM XiaoZhi .NET 构建脚本
REM 用于在Windows环境下构建项目

echo 🚀 开始构建 XiaoZhi .NET 项目...

REM 检查.NET版本
echo 📋 检查 .NET 版本...
dotnet --version
if errorlevel 1 (
    echo ❌ 错误：未找到 .NET SDK
    exit /b 1
)

REM 清理之前的构建
echo 🧹 清理构建目录...
if exist build rmdir /s /q build
mkdir build\console build\winui build\packages

REM 还原NuGet包
echo 📦 还原 NuGet 包...
dotnet restore
if errorlevel 1 (
    echo ❌ 错误：NuGet包还原失败
    exit /b 1
)

REM 构建解决方案
echo 🔨 构建解决方案...
dotnet build --configuration Release --no-restore
if errorlevel 1 (
    echo ❌ 错误：构建失败
    exit /b 1
)

REM 运行测试
echo 🧪 运行测试...
dotnet test --configuration Release --no-build --verbosity normal
if errorlevel 1 (
    echo ⚠️ 警告：测试失败，但继续构建
)

REM 发布控制台应用
echo 📱 发布控制台应用...
dotnet publish src\XiaoZhi.Console\XiaoZhi.Console.csproj ^
    --configuration Release ^
    --output build\console\win-x64 ^
    --runtime win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true

dotnet publish src\XiaoZhi.Console\XiaoZhi.Console.csproj ^
    --configuration Release ^
    --output build\console\win-x86 ^
    --runtime win-x86 ^
    --self-contained true ^
    -p:PublishSingleFile=true

REM 发布WinUI应用
echo 🖥️ 发布 WinUI 应用...
dotnet publish src\XiaoZhi.WinUI\XiaoZhi.WinUI.csproj ^
    --configuration Release ^
    --output build\winui\win-x64 ^
    --runtime win-x64 ^
    --self-contained true

REM 创建NuGet包
echo 📦 创建 NuGet 包...
dotnet pack src\XiaoZhi.Core\XiaoZhi.Core.csproj ^
    --configuration Release ^
    --output build\packages ^
    --no-build

echo ✅ 构建完成！
echo 📁 构建产物位于 build\ 目录

REM 显示构建结果
echo 📊 构建结果：
echo 控制台应用：
dir build\console\win-x64\
echo WinUI应用：
dir build\winui\win-x64\
echo NuGet包：
dir build\packages\

pause
