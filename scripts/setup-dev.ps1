#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Development environment setup script for XiaoZhi .NET project
.DESCRIPTION
    This script sets up the development environment including tools, dependencies, and git hooks
.PARAMETER SkipTools
    Skip installation of development tools
.PARAMETER SkipHooks
    Skip git hooks setup
.EXAMPLE
    .\setup-dev.ps1
    .\setup-dev.ps1 -SkipTools
#>

param(
    [switch]$SkipTools,
    [switch]$SkipHooks
)

$ErrorActionPreference = 'Stop'

# Get the script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "Setting up XiaoZhi .NET Development Environment" -ForegroundColor Green
Write-Host "Root Directory: $RootDir" -ForegroundColor Yellow

Set-Location $RootDir

try {
    # Check .NET SDK
    Write-Host "`nChecking .NET SDK..." -ForegroundColor Cyan
    $dotnetVersion = dotnet --version
    Write-Host ".NET SDK Version: $dotnetVersion" -ForegroundColor Green

    # Restore dependencies
    Write-Host "`nRestoring dependencies..." -ForegroundColor Cyan
    dotnet restore XiaoZhi.sln

    # Install development tools if not skipped
    if (-not $SkipTools) {
        Write-Host "`nInstalling development tools..." -ForegroundColor Cyan
        
        # List of useful tools for .NET development
        $tools = @(
            'dotnet-reportgenerator-globaltool',  # Code coverage reports
            'dotnet-outdated-tool',               # Check outdated packages
            'dotnet-format',                      # Code formatting
            'dotnet-trace',                       # Performance tracing
            'dotnet-counters'                     # Performance counters
        )

        foreach ($tool in $tools) {
            try {
                Write-Host "Installing $tool..." -ForegroundColor Yellow
                dotnet tool install --global $tool 2>$null
                Write-Host "✓ $tool installed" -ForegroundColor Green
            } catch {
                Write-Host "⚠ $tool already installed or failed to install" -ForegroundColor Yellow
            }
        }
    }

    # Set up git hooks if not skipped
    if (-not $SkipHooks -and (Test-Path ".git")) {
        Write-Host "`nSetting up git hooks..." -ForegroundColor Cyan
        
        $hooksDir = ".git\hooks"
        
        # Pre-commit hook
        $preCommitHook = @"
#!/bin/sh
# Pre-commit hook for XiaoZhi .NET project

echo "Running pre-commit checks..."

# Run dotnet format to ensure code formatting
echo "Checking code formatting..."
dotnet format --verify-no-changes --verbosity quiet
if [ `$? -ne 0 ]; then
    echo "❌ Code formatting issues found. Run 'dotnet format' to fix them."
    exit 1
fi

# Build the solution
echo "Building solution..."
dotnet build --configuration Debug --verbosity quiet
if [ `$? -ne 0 ]; then
    echo "❌ Build failed. Please fix build errors before committing."
    exit 1
fi

# Run quick tests
echo "Running tests..."
dotnet test --configuration Debug --verbosity quiet --no-build
if [ `$? -ne 0 ]; then
    echo "❌ Tests failed. Please fix failing tests before committing."
    exit 1
fi

echo "✅ All pre-commit checks passed!"
"@
        
        $preCommitPath = Join-Path $hooksDir "pre-commit"
        Set-Content -Path $preCommitPath -Value $preCommitHook -Encoding UTF8
        
        Write-Host "✓ Pre-commit hook installed" -ForegroundColor Green
    }

    # Create local development settings
    Write-Host "`nCreating development configuration..." -ForegroundColor Cyan
    
    $devSettings = @{
        "Logging" = @{
            "LogLevel" = @{
                "Default" = "Debug"
                "XiaoZhi" = "Debug"
            }
        }
        "Development" = @{
            "EnableDetailedErrors" = $true
            "EnableHotReload" = $true
        }
    }
    
    $devSettingsPath = "src\XiaoZhi.Console\appsettings.Development.json"
    if (-not (Test-Path $devSettingsPath)) {
        $devSettings | ConvertTo-Json -Depth 4 | Set-Content -Path $devSettingsPath -Encoding UTF8
        Write-Host "✓ Development settings created" -ForegroundColor Green
    }

    # Build everything to ensure it works
    Write-Host "`nBuilding solution..." -ForegroundColor Cyan
    dotnet build XiaoZhi.sln --configuration Debug

    Write-Host "`n✅ Development environment setup completed successfully!" -ForegroundColor Green
    Write-Host "`nNext steps:" -ForegroundColor Yellow
    Write-Host "  1. Open the solution in your IDE: XiaoZhi.sln" -ForegroundColor Gray
    Write-Host "  2. Run the console app: dotnet run --project src\XiaoZhi.Console" -ForegroundColor Gray
    Write-Host "  3. Run tests: .\scripts\test.ps1" -ForegroundColor Gray
    Write-Host "  4. Build for release: .\scripts\build.ps1 -Configuration Release" -ForegroundColor Gray

} catch {
    Write-Host "`nSetup failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
