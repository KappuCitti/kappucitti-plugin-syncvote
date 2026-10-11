# Creates and starts an isolated Jellyfin server from the official portable zip, to test the plugin on any version.
# Admin root/root, one movie library of 30 empty files with NFO metadata (3 genres), SyncPlay ready.
#   1. Download jellyfin_<version>-amd64.zip from https://repo.jellyfin.org/files/server/windows/ and unzip it to <Root>\server
#   2. ./new-server.ps1 -Root <Root> -Port 8097 -PluginDll <path to KappuCitti.Plugin.SyncVote.dll>
# Everything (data, media) lives under <Root>; the server on the default port is not touched.
param(
    [Parameter(Mandatory)][string]$Root,        # folder that holds server\jellyfin
    [Parameter(Mandatory)][int]$Port,
    [Parameter(Mandatory)][string]$PluginDll,   # built plugin dll to install, or "" for none
    [string]$PluginVersion = "0.1.0.0",
    [string]$Exe,        # run another jellyfin.exe (e.g. an installed one) against this isolated data folder
    [string]$WebDir,     # web client folder to use with -Exe
    [string]$MediaPath   # use this folder of movies (one sub-folder per movie) instead of generating 30 fake ones
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$exe = if ($Exe) { $Exe } else { "$Root\server\jellyfin\jellyfin.exe" }
$data = "$Root\data"
$media = if ($MediaPath) { $MediaPath } else { "$Root\media\Movies" }
$base = "http://127.0.0.1:$Port"

# Stop a previous instance of this very server only: the one started with THIS data folder (never another server using the same exe).
Get-CimInstance Win32_Process -Filter "Name='jellyfin.exe'" | Where-Object { $_.CommandLine -like "*$data*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep 2

if (-not $MediaPath -and -not (Test-Path "$media\Movie 01 (2001)")) {
    $genres = "Action", "Comedy", "Drama"
    1..30 | ForEach-Object {
        $n = "{0:00}" -f $_
        $dir = "$media\Movie $n ($(2000 + $_))"
        New-Item -ItemType Directory -Force $dir | Out-Null
        New-Item -ItemType File -Force "$dir\Movie $n ($(2000 + $_)).mkv" | Out-Null
        $g = $genres[$_ % 3]
        Set-Content "$dir\Movie $n ($(2000 + $_)).nfo" "<movie><title>Movie $n</title><year>$(2000 + $_)</year><genre>$g</genre><plot>Fake movie number $n used to test SyncVote.</plot></movie>"
    }
}

New-Item -ItemType Directory -Force "$data\config", "$data\plugins" | Out-Null
if (-not (Test-Path "$data\config\network.xml")) {
    Set-Content "$data\config\network.xml" "<?xml version=`"1.0`" encoding=`"utf-8`"?><NetworkConfiguration><InternalHttpPort>$Port</InternalHttpPort><PublicHttpPort>$Port</PublicHttpPort><EnableUPnP>false</EnableUPnP><AutoDiscovery>false</AutoDiscovery></NetworkConfiguration>"
}
Get-ChildItem "$data\plugins" -Directory -Filter "SyncVote_*" | Remove-Item -Recurse -Force
if ($PluginDll) {
    $dest = "$data\plugins\SyncVote_$PluginVersion"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item $PluginDll $dest
}

$serverArgs = @("--datadir", "`"$data`"")
if ($WebDir) { $serverArgs += @("--webdir", "`"$WebDir`"") }
$proc = Start-Process $exe -ArgumentList $serverArgs -WorkingDirectory (Split-Path $exe) -PassThru -WindowStyle Hidden
Set-Content "$Root\pid.txt" $proc.Id
for ($i = 0; $i -lt 90; $i++) { Start-Sleep 2; try { $info = Invoke-RestMethod "$base/System/Info/Public" -TimeoutSec 3; break } catch { } }
if (-not $info) { throw "server did not start" }
# 10.11+ migrates its database first and answers with a startup page until it is done.
for ($i = 0; $i -lt 120; $i++) {
    try { $r = Invoke-WebRequest "$base/System/Info/Public" -TimeoutSec 5; if ($r.Headers["Content-Type"] -match "json") { $info = $r.Content | ConvertFrom-Json; break } } catch { }
    Start-Sleep 2
}
Write-Host "started $($info.ProductName) $($info.Version) on $Port (wizard done: $($info.StartupWizardCompleted))"

# While it starts (10.11+ also migrates its database) the server answers 503, or a startup page. Wait until the Startup API gives a real
# answer - 200 while the wizard is open, 401 once it is done - and only then read the state: early answers say "not completed".
for ($i = 0; $i -lt 90; $i++) {
    try { $r = Invoke-WebRequest "$base/Startup/Configuration" -TimeoutSec 5 -SkipHttpErrorCheck; if ([int]$r.StatusCode -in 200, 401, 403 -and "$($r.Headers['Content-Type'])" -notmatch "html") { break } } catch { }
    Start-Sleep 2
}
$info = $null
for ($i = 0; $i -lt 60 -and -not $info; $i++) { try { $info = Invoke-RestMethod "$base/System/Info/Public" -TimeoutSec 5 } catch { Start-Sleep 2 } }
if (-not $info) { throw "the server never answered" }
if (-not $info.StartupWizardCompleted) {
    function Step($name, $method, $path, $body) {
        $p = @{ Uri = "$base$path"; Method = $method; SkipHttpErrorCheck = $true }
        if ($body) { $p.Body = $body; $p.ContentType = "application/json" }
        $r = Invoke-WebRequest @p
        $text = if ($r.Content -is [byte[]]) { [Text.Encoding]::UTF8.GetString($r.Content) } else { "$($r.Content)" }
        Write-Host ("  wizard {0}: {1} {2}" -f $name, [int]$r.StatusCode, $text.Substring(0, [Math]::Min(200, $text.Length)))
        if ([int]$r.StatusCode -ge 400) { throw "wizard step $name failed" }
    }
    Step "configuration" POST "/Startup/Configuration" '{"ServerName":"test","UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}'
    Step "get user" GET "/Startup/User" $null
    Step "user" POST "/Startup/User" '{"Name":"root","Password":"root"}'
    Step "remote access" POST "/Startup/RemoteAccess" '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}'
    Step "complete" POST "/Startup/Complete" $null

    $h = @{ "Authorization" = 'MediaBrowser Client="setup", Device="setup", DeviceId="setup-id", Version="1.0"' }
    $auth = Invoke-RestMethod "$base/Users/AuthenticateByName" -Method Post -Headers $h -ContentType "application/json" -Body '{"Username":"root","Pw":"root"}'
    $h["Authorization"] += ", Token=`"$($auth.AccessToken)`""
    $expected = if ($MediaPath) { @(Get-ChildItem $MediaPath -Directory).Count } else { 30 }
    $body = @{ LibraryOptions = @{ PathInfos = @(@{ Path = $media }); EnableRealtimeMonitor = $false; MetadataSavers = @(); LocalMetadataReaderOrder = @("Nfo") } } | ConvertTo-Json -Depth 6
    Invoke-RestMethod "$base/Library/VirtualFolders?name=Film&collectionType=movies&refreshLibrary=true" -Method Post -Headers $h -ContentType "application/json" -Body $body | Out-Null
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep 2
        $count = (Invoke-RestMethod "$base/Items?Recursive=true&IncludeItemTypes=Movie&Limit=1" -Headers $h).TotalRecordCount
        if ($count -ge $expected) { break }
    }
    Write-Host "library ready: $count movies"
}
