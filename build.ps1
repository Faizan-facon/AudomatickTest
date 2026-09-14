[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Version = "1.0.0",

    [switch]$Pack,

    [string]$OutputDir = "./artifacts"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " AUDOMATICK DESKTOP - VELOPACK BUILD AND PACKAGING" -ForegroundColor Cyan
Write-Host " Configuration: $Configuration" -ForegroundColor Gray
Write-Host " Version:       $Version" -ForegroundColor Gray
Write-Host " Pack:          $Pack" -ForegroundColor Gray
Write-Host " Output Dir:    $OutputDir" -ForegroundColor Gray
Write-Host "=================================================================" -ForegroundColor Cyan

# 1. Build Solution
Write-Host "`n[1/4] Building Solution..." -ForegroundColor Green
dotnet build Audomatick.Desktop.sln -c $Configuration /p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# 2. Run Tests
Write-Host "`n[2/4] Running Unit and Manifest Tests..." -ForegroundColor Green
dotnet test tests/Audomatick.Desktop.Tests/Audomatick.Desktop.Tests.csproj -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

# 3. Publish Windows Desktop
$publishDir = Join-Path $OutputDir "publish"
Write-Host "`n[3/4] Publishing Audomatick.Desktop to $publishDir..." -ForegroundColor Green
dotnet publish src/Audomatick.Desktop/Audomatick.Desktop.csproj -c $Configuration -r win-x64 --self-contained false -o $publishDir /p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# 4. Packaging with Velopack 1.2.0
if ($Pack) {
    Write-Host "`n[4/4] Packaging with Velopack 1.2.0..." -ForegroundColor Green
    $releasesDir = Join-Path $OutputDir "releases"
    if (-not (Test-Path $releasesDir)) { New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null }

    $vpkCmd = Get-Command "vpk" -ErrorAction SilentlyContinue
    if (-not $vpkCmd) {
        Write-Warning "Velopack CLI 'vpk' was not found in PATH. Install via: dotnet tool install -g vpk"
    } else {
        Write-Host "  -> Running 'vpk pack' for Audomatick.Desktop (v$Version)..." -ForegroundColor Yellow

        vpk pack `
            --packId "Audomatick.Desktop" `
            --packVersion $Version `
            --packDir $publishDir `
            --mainExe "Audomatick.Desktop.exe" `
            --outputDir $releasesDir `
            --packTitle "Audomatick Desktop" `
            --packAuthors "Faizan-facon"

        if ($LASTEXITCODE -ne 0) { throw "Velopack packaging failed." }

        Write-Host "`n[OK] Packaging Succeeded! Releases available in: $releasesDir" -ForegroundColor Green
        Get-ChildItem -Path $releasesDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
    }
} else {
    Write-Host "`n[OK] Build and Tests Completed! (Pass -Pack to generate Velopack releases)" -ForegroundColor Green
}
