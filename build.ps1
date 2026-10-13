<#
.SYNOPSIS
    Builds SyncVote for every supported Jellyfin generation and packs one zip per generation.
.DESCRIPTION
    Jellyfin plugins are not binary compatible across server generations, so the same source is compiled
    once per target (see the matrix below). Each zip only holds the plugin dll: Jellyfin provides the rest.
    The 4th digit of the version follows the generation (…0 = 10.10, …1 = 10.11, …2 = 12), so the plugin
    catalog of any server offers the build made for it.

    Requirements: .NET SDKs matching the targets (8 for 10.10, 9 for 10.11, 10 for 12).
.PARAMETER ReleaseVersion
    Release number "X.Y.Z" (default: the one in the csproj).
.PARAMETER OutDir
    Where the zips and versions.json are written.
.PARAMETER Targets
    Only build these Jellyfin generations, e.g. -Targets 10.10,12.
.PARAMETER SkipTests
    Do not run the unit tests first.
.PARAMETER UpdateManifest
    Add the new builds to manifest.json.
.PARAMETER DownloadBaseUrl
    Base URL the zips will be downloaded from (needed with -UpdateManifest).
.EXAMPLE
    ./build.ps1 -ReleaseVersion 0.2.0
.EXAMPLE
    ./build.ps1 -ReleaseVersion 0.2.0 -UpdateManifest -DownloadBaseUrl https://github.com/me/repo/releases/download/0.2.0
#>
param(
    [string]$ReleaseVersion,
    [string]$OutDir = "artifacts",
    [string[]]$Targets,
    [switch]$SkipTests,
    [switch]$UpdateManifest,
    [string]$DownloadBaseUrl
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# Jellyfin generation -> package version to compile against (the oldest we test), target ABI, version digit.
$matrix = @(
    @{ Generation = "10.10"; Jellyfin = "10.10.7"; Abi = "10.10.0.0"; Index = 0; Sdk = 8 },
    @{ Generation = "10.11"; Jellyfin = "10.11.0"; Abi = "10.11.0.0"; Index = 1; Sdk = 9 },
    @{ Generation = "12"; Jellyfin = "12.0.0"; Abi = "12.0.0.0"; Index = 2; Sdk = 10 }
)
if ($Targets) { $matrix = $matrix | Where-Object { $Targets -contains $_.Generation } }
if (-not $matrix) { throw "No target selected." }

$project = "KappuCitti.Plugin.SyncVote/KappuCitti.Plugin.SyncVote.csproj"
if (-not $ReleaseVersion) {
    # Select-Xml gives the text even when the element has attributes (it has a Condition); the property syntax would give the element.
    $ReleaseVersion = (Select-Xml -Path $project -XPath "//ReleaseVersion" | Select-Object -First 1).Node.InnerText
}
if ($ReleaseVersion -notmatch '^\d+\.\d+\.\d+$') { throw "ReleaseVersion must look like 1.2.3 (got '$ReleaseVersion')." }

if (-not $SkipTests) {
    Write-Host "== unit tests" -ForegroundColor Cyan
    dotnet test KappuCitti.Plugin.SyncVote.Tests -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Unit tests failed." }
}

$out = Join-Path $PSScriptRoot $OutDir
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null
$entries = @()

foreach ($t in $matrix) {
    $version = "$ReleaseVersion.$($t.Index)"
    Write-Host "== Jellyfin $($t.Generation): $version (targetAbi $($t.Abi), compiled against $($t.Jellyfin))" -ForegroundColor Cyan
    $publish = Join-Path $out "publish-$($t.Generation)"
    dotnet publish $project -c Release "-p:JellyfinVersion=$($t.Jellyfin)" "-p:ReleaseVersion=$ReleaseVersion" -o $publish -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build for Jellyfin $($t.Generation) failed (is the .NET $($t.Sdk) SDK installed?)." }

    $zip = Join-Path $out "$version-SyncVote.zip"
    Compress-Archive -Path (Join-Path $publish "KappuCitti.Plugin.SyncVote.dll") -DestinationPath $zip -Force
    Remove-Item -Recurse -Force $publish

    $entries += [ordered]@{
        version   = $version
        targetAbi = $t.Abi
        file      = (Split-Path $zip -Leaf)
        checksum  = (Get-FileHash $zip -Algorithm MD5).Hash.ToLowerInvariant()
        timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    }
    Write-Host "   $(Split-Path $zip -Leaf)  md5 $($entries[-1].checksum)"
}

ConvertTo-Json -InputObject @($entries) -Depth 5 | Set-Content (Join-Path $out "versions.json")

if ($UpdateManifest) {
    if (-not $DownloadBaseUrl) { throw "-UpdateManifest needs -DownloadBaseUrl." }
    $manifestPath = Join-Path $PSScriptRoot "manifest.json"
    $manifest = @(Get-Content $manifestPath -Raw | ConvertFrom-Json)
    $existing = @($manifest[0].versions | Where-Object { $entries.version -notcontains $_.version })
    $new = $entries | ForEach-Object {
        [ordered]@{
            version   = $_.version
            changelog = "See release notes on GitHub"
            targetAbi = $_.targetAbi
            sourceUrl = "$($DownloadBaseUrl.TrimEnd('/'))/$($_.file)"
            checksum  = $_.checksum
            timestamp = $_.timestamp
        }
    }
    # Newest first; within a release the newest generation first.
    $manifest[0].versions = @($new | Sort-Object { [version]$_.version } -Descending) + $existing
    ConvertTo-Json -InputObject @($manifest) -Depth 10 | Set-Content $manifestPath -Encoding utf8
    Write-Host "manifest.json updated" -ForegroundColor Green
}

Write-Host "Done: $out" -ForegroundColor Green
