<#
.SYNOPSIS
    Builds the plugin for one Jellyfin version, installs it in a (test!) server by copying the files, and restarts it.
.DESCRIPTION
    A catalog install is what users do; this is the quick way for development: the dll goes to
    <DataDir>/plugins/SyncVote_<version>. meta.json and logo.png are written too, because Jellyfin only creates
    them when it installs from a catalog and the plugin page shows the logo from there.
    Any previous SyncVote folder is removed. Never point this at a production server.
.PARAMETER JellyfinVersion
    10.10.7, 10.11.0 or 12.0.0 (what the plugin is compiled against).
.PARAMETER DataDir
    Jellyfin data directory (default: the Windows one, %LOCALAPPDATA%\jellyfin).
.PARAMETER ServerExe
    The server executable to stop and start again (default: <DataDir>\jellyfin.exe).
.PARAMETER Base
    Address of the server, used to wait until it is back.
#>
param(
    [string]$JellyfinVersion = "10.10.7",
    [string]$DataDir = (Join-Path $env:LOCALAPPDATA "jellyfin"),
    [string]$ServerExe,
    [string]$Base = "http://127.0.0.1:8096",
    [switch]$NoRestart
)

$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
if (-not $ServerExe) { $ServerExe = Join-Path $DataDir "jellyfin.exe" }
$out = Join-Path ([IO.Path]::GetTempPath()) "syncvote-install-$JellyfinVersion"

Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
dotnet publish "$repo\KappuCitti.Plugin.SyncVote" -c Release "-p:JellyfinVersion=$JellyfinVersion" -o $out -v q -nologo
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$dll = Join-Path $out "KappuCitti.Plugin.SyncVote.dll"
$info = [Reflection.AssemblyName]::GetAssemblyName($dll)
$version = $info.Version.ToString()
$abi = switch -Wildcard ($JellyfinVersion) { "10.9.*" { "10.10.0.0" } "10.10.*" { "10.10.0.0" } "10.11.*" { "10.11.0.0" } default { "12.0.0.0" } }

if (-not $NoRestart) {
    Get-Process jellyfin -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $ServerExe } | Stop-Process -Force
    Start-Sleep 3
}

Get-ChildItem (Join-Path $DataDir "plugins") -Directory -Filter "SyncVote_*" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
$dest = Join-Path $DataDir "plugins\SyncVote_$version"
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item $dll $dest
Copy-Item "$repo\images\logo.png" $dest
$manifest = (Get-Content "$repo\manifest.json" -Raw | ConvertFrom-Json)[0]
[ordered]@{
    category    = $manifest.category
    changelog   = "Installed from a development build"
    description = $manifest.description
    guid        = $manifest.guid
    name        = $manifest.name
    overview    = $manifest.overview
    owner       = $manifest.owner
    targetAbi   = $abi
    timestamp   = (Get-Date).ToUniversalTime().ToString("o")
    version     = $version
    status      = "Active"
    autoUpdate  = $false
    imagePath   = Join-Path $dest "logo.png"
    assemblies  = @()
} | ConvertTo-Json | Set-Content (Join-Path $dest "meta.json")
Write-Host "installed SyncVote $version in $dest"

if (-not $NoRestart) {
    Start-Process $ServerExe -WorkingDirectory (Split-Path $ServerExe)
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep 2
        try { $v = (Invoke-RestMethod "$Base/System/Info/Public" -TimeoutSec 3).Version; Write-Host "server up: $v"; break } catch { }
    }
}
