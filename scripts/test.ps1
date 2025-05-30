#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Test script for XiaoZhi .NET project
.DESCRIPTION
    This script runs all tests in the XiaoZhi solution with comprehensive reporting
.PARAMETER Configuration
    Build configuration (Debug or Release). Default is Debug for testing.
.PARAMETER Coverage
    Generate code coverage reports
.PARAMETER Watch
    Run tests in watch mode
.PARAMETER Filter
    Filter tests by name or category
.EXAMPLE
    .\test.ps1 -Coverage
    .\test.ps1 -Filter "OpusTest" -Watch
#>

param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Coverage,
    [switch]$Watch,
    [string]$Filter = $null
)

$ErrorActionPreference = 'Stop'

# Get the script directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir

Write-Host "Running XiaoZhi .NET Tests" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Root Directory: $RootDir" -ForegroundColor Yellow

Set-Location $RootDir

try {
    # Ensure solution is built
    Write-Host "`nBuilding solution for testing..." -ForegroundColor Cyan
    dotnet build XiaoZhi.sln --configuration $Configuration

    # Prepare test command
    $testCommand = "dotnet test XiaoZhi.sln --no-build --configuration $Configuration --verbosity normal"
    
    if ($Filter) {
        $testCommand += " --filter `"$Filter`""
        Write-Host "Filter: $Filter" -ForegroundColor Yellow
    }

    if ($Coverage) {
        Write-Host "Code coverage enabled" -ForegroundColor Yellow
        $testCommand += " --collect:`"XPlat Code Coverage`" --results-directory .\build\coverage"
        
        # Ensure coverage directory exists
        New-Item -Path ".\build\coverage" -ItemType Directory -Force | Out-Null
    }

    if ($Watch) {
        Write-Host "Running tests in watch mode..." -ForegroundColor Cyan
        Write-Host "Press 'q' to quit, 'r' to run all tests, or 'h' for help" -ForegroundColor Gray
        dotnet watch test --project tests --configuration $Configuration
    } else {
        # Run tests
        Write-Host "`nRunning tests..." -ForegroundColor Cyan
        Invoke-Expression $testCommand

        if ($Coverage) {
            Write-Host "`nGenerating coverage report..." -ForegroundColor Cyan
            
            # Find coverage files
            $coverageFiles = Get-ChildItem -Path ".\build\coverage" -Recurse -Filter "coverage.cobertura.xml"
            
            if ($coverageFiles.Count -gt 0) {
                Write-Host "Coverage files found:" -ForegroundColor Green
                foreach ($file in $coverageFiles) {
                    Write-Host "  $($file.FullName)" -ForegroundColor Gray
                }
                
                # Generate HTML report if reportgenerator is available
                try {
                    $reportGenerator = Get-Command reportgenerator -ErrorAction SilentlyContinue
                    if ($reportGenerator) {
                        $coveragePath = $coverageFiles[0].FullName
                        $reportPath = ".\build\coverage\html"
                        
                        Write-Host "Generating HTML coverage report..." -ForegroundColor Yellow
                        reportgenerator -reports:$coveragePath -targetdir:$reportPath -reporttypes:Html
                        
                        Write-Host "Coverage report generated at: $reportPath\index.html" -ForegroundColor Green
                    } else {
                        Write-Host "Install reportgenerator for HTML reports: dotnet tool install -g dotnet-reportgenerator-globaltool" -ForegroundColor Yellow
                    }
                } catch {
                    Write-Host "Could not generate HTML report: $($_.Exception.Message)" -ForegroundColor Yellow
                }
            } else {
                Write-Host "No coverage files found" -ForegroundColor Yellow
            }
        }
    }

    if (-not $Watch) {
        Write-Host "`nTests completed successfully!" -ForegroundColor Green
    }

} catch {
    Write-Host "`nTests failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
