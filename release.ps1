<#
.SYNOPSIS
    Build and release SyncVote plugin
.DESCRIPTION
    Builds the plugin, creates a zip, updates manifest.json, and optionally creates a git tag
.PARAMETER Version
    The version number (e.g., 0.0.0.2)
.PARAMETER Push
    Push the tag and changes to GitHub
.PARAMETER CreateRelease
    Create a GitHub release (requires gh CLI)
.EXAMPLE
    .\release.ps1 -Version "0.0.0.2"
.EXAMPLE
    .\release.ps1 -Version "0.0.0.2" -Push -CreateRelease
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [switch]$Push,

    [switch]$CreateRelease
)

$ErrorActionPreference = "Stop"
$ProjectDir = "KappuCitti.Plugin.SyncVote"
$OutputDir = ".\publish"
$ZipName = "$Version-SyncVote.zip"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host " SyncVote Release Script v$Version" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Step 1: Clean previous builds
Write-Host "`n[1/7] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path "$ProjectDir\bin") { Remove-Item -Recurse -Force "$ProjectDir\bin" }
if (Test-Path "$ProjectDir\obj") { Remove-Item -Recurse -Force "$ProjectDir\obj" }
if (Test-Path $OutputDir) { Remove-Item -Recurse -Force $OutputDir }
if (Test-Path $ZipName) { Remove-Item -Force $ZipName }

# Step 2: Update version in csproj
Write-Host "[2/7] Updating version in .csproj..." -ForegroundColor Yellow
$csprojPath = "$ProjectDir\$ProjectDir.csproj"
$csproj = Get-Content $csprojPath -Raw
$csproj = $csproj -replace '<AssemblyVersion>.*</AssemblyVersion>', "<AssemblyVersion>$Version</AssemblyVersion>"
$csproj = $csproj -replace '<FileVersion>.*</FileVersion>', "<FileVersion>$Version</FileVersion>"
Set-Content $csprojPath $csproj

# Step 3: Build
Write-Host "[3/7] Building Release..." -ForegroundColor Yellow
dotnet restore
dotnet publish "$ProjectDir\$ProjectDir.csproj" -c Release -o $OutputDir
if ($LASTEXITCODE -ne 0) { throw "Build failed!" }

# Step 4: Create zip
Write-Host "[4/7] Creating plugin zip..." -ForegroundColor Yellow
Compress-Archive -Path "$OutputDir\*" -DestinationPath $ZipName -Force

# Step 5: Calculate checksum
Write-Host "[5/7] Calculating checksum..." -ForegroundColor Yellow
$hash = Get-FileHash $ZipName -Algorithm MD5
$checksum = $hash.Hash.ToLower()
Write-Host "  Checksum: $checksum" -ForegroundColor Gray

# Step 6: Update manifest.json
Write-Host "[6/7] Updating manifest.json..." -ForegroundColor Yellow
$manifestContent = Get-Content "manifest.json" -Raw
$manifest = $manifestContent | ConvertFrom-Json
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

# Get repo URL from git remote
$repoUrl = git remote get-url origin
$repoUrl = $repoUrl -replace '\.git$', ''
$repoUrl = $repoUrl -replace 'git@github.com:', 'https://github.com/'

$newVersion = [PSCustomObject]@{
    version = $Version
    timestamp = $timestamp
    targetAbi = "10.10.0.0"
    sourceUrl = "$repoUrl/releases/download/$Version/$ZipName"
    checksum = $checksum
    changelog = "See release notes on GitHub"
}

# Add new version at the beginning of versions array
$manifest[0].versions = @($newVersion) + @($manifest[0].versions)

# Force array output with @() and use -AsArray parameter
$jsonOutput = ConvertTo-Json -InputObject @($manifest) -Depth 10
Set-Content "manifest.json" -Value $jsonOutput -Encoding UTF8

Write-Host "[7/7] Summary:" -ForegroundColor Green
Write-Host "  Version: $Version"
Write-Host "  Zip: $ZipName"
Write-Host "  Checksum: $checksum"
Write-Host "  Manifest updated: Yes"

if ($Push) {
    Write-Host "`nPushing to GitHub..." -ForegroundColor Yellow
    git add manifest.json "$ProjectDir\$ProjectDir.csproj"
    git commit -m "Release v$Version"
    git tag -a $Version -m "Release v$Version"
    git push origin master --tags
    Write-Host "  Pushed tag: $Version" -ForegroundColor Green
}

if ($CreateRelease -and $Push) {
    Write-Host "`nCreating GitHub Release..." -ForegroundColor Yellow
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        gh release create $Version $ZipName --title "SyncVote v$Version" --generate-notes
        Write-Host "  Release created!" -ForegroundColor Green
    } else {
        Write-Host "  GitHub CLI (gh) not found. Install it to create releases automatically." -ForegroundColor Red
        Write-Host "  You can manually upload $ZipName to GitHub Releases." -ForegroundColor Yellow
    }
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host " Release completed successfully!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan

if (-not $Push) {
    Write-Host "`nNext steps:" -ForegroundColor Yellow
    Write-Host "  1. Review changes: git diff"
    Write-Host "  2. Run again with -Push to push to GitHub"
    Write-Host "  3. Add -CreateRelease to also create a GitHub release"
}
