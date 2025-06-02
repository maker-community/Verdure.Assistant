#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build script for Verdure Assistant .NET project
.DESCRIPTION
    This script builds the entire Verdure Assistant solution including all projects and tests
.PARAMETER Configuration
    Build configuration (Debug or Release). Default is Release.
.PARAMETER Clean
    Clean before building
.PARAMETER Test
    Run tests after building
.PARAMETER Pack
    Create NuGet packages
.PARAMETER Publish
    Publish applications
.EXAMPLE
    .\build.ps1 -Configuration Release -Test -Pack
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Clean,
    [switch]$Test,
    [switch]$Pack,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'

# Get the script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "Building Verdure Assistant .NET Project" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Root Directory: $RootDir" -ForegroundColor Yellow

Set-Location $RootDir

try {    # Clean if requested
    if ($Clean) {
        Write-Host "`nCleaning solution..." -ForegroundColor Cyan
        dotnet clean Verdure.Assistant.sln --configuration $Configuration
          # Remove bin and obj directories
        Get-ChildItem -Path . -Recurse -Directory -Name "bin", "obj" | ForEach-Object {
            $path = Join-Path $pwd $_
            if (Test-Path $path) {
                Write-Host "Removing $path" -ForegroundColor Gray
                Remove-Item $path -Recurse -Force
            }
        }
    }# Restore dependencies
    Write-Host "`nRestoring dependencies..." -ForegroundColor Cyan
    dotnet restore Verdure.Assistant.sln

    # Build solution
    Write-Host "`nBuilding solution..." -ForegroundColor Cyan
    dotnet build Verdure.Assistant.sln --no-restore --configuration $Configuration

    # Run tests if requested
    if ($Test) {
        Write-Host "`nRunning tests..." -ForegroundColor Cyan
        dotnet test Verdure.Assistant.sln --no-build --configuration $Configuration --verbosity normal --collect:"XPlat Code Coverage"
    }

    # Create packages if requested
    if ($Pack) {
        Write-Host "`nCreating NuGet packages..." -ForegroundColor Cyan
        dotnet pack src\Verdure.Assistant.Core\Verdure.Assistant.Core.csproj --no-build --configuration $Configuration --output .\build\packages
    }

    # Publish applications if requested
    if ($Publish) {
        Write-Host "`nPublishing applications..." -ForegroundColor Cyan
        
        $PublishDir = ".\build\publish"
        New-Item -Path $PublishDir -ItemType Directory -Force | Out-Null

        # Publish Console App for multiple platforms
        Write-Host "Publishing Console App..." -ForegroundColor Yellow
        dotnet publish src\Verdure.Assistant.Console\Verdure.Assistant.Console.csproj -c $Configuration -o "$PublishDir\console-win-x64" --self-contained true -r win-x64
        dotnet publish src\Verdure.Assistant.Console\Verdure.Assistant.Console.csproj -c $Configuration -o "$PublishDir\console-linux-x64" --self-contained true -r linux-x64
        dotnet publish src\Verdure.Assistant.Console\Verdure.Assistant.Console.csproj -c $Configuration -o "$PublishDir\console-osx-x64" --self-contained true -r osx-x64

        # Publish WinUI App
        Write-Host "Publishing WinUI App..." -ForegroundColor Yellow
        dotnet publish src\Verdure.Assistant.WinUI\Verdure.Assistant.WinUI.csproj -c $Configuration -o "$PublishDir\winui" --self-contained true -r win-x64
    }

    Write-Host "`nBuild completed successfully!" -ForegroundColor Green

} catch {
    Write-Host "`nBuild failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
