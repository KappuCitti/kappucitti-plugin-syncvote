<#
.SYNOPSIS
    Installs SyncVote from the PUBLISHED catalog on an isolated test server and checks which version the server gets.
.DESCRIPTION
    What a user does: add the repository, look at the catalog, install, restart. The server is one made by new-server.ps1
    (it removes any SyncVote it had). Afterwards run api-test.ps1 against it to check the installed package works.
.PARAMETER Root
    Folder of the isolated server (see new-server.ps1).
.PARAMETER Expect
    The version the catalog must give to this server, e.g. 0.1.0.1 for a Jellyfin 10.11 server.
#>
param([Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][int]$Port, [Parameter(Mandatory)][string]$Expect, [string]$Exe, [string]$WebDir,
      [string]$ManifestUrl = "https://raw.githubusercontent.com/KappuCitti/kappucitti-plugin-syncvote/master/manifest.json")
$ErrorActionPreference = "Stop"
$manifestUrl = $ManifestUrl
$guid = "6bfde2dd-8211-4964-b86d-7812a9de160b"
$data = "$Root\data"; $base = "http://127.0.0.1:$Port"
if (-not $Exe) { $Exe = "$Root\server\jellyfin\jellyfin.exe" }
. "$PSScriptRoot\lib\jf.ps1" -Base $base

function Stop-Mine { Get-CimInstance Win32_Process -Filter "Name='jellyfin.exe'" | Where-Object { $_.CommandLine -like "*$data*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }; Start-Sleep 3 }
function Start-Mine {
    $a = @("--datadir", "`"$data`""); if ($WebDir) { $a += @("--webdir", "`"$WebDir`"") }
    Start-Process $Exe -ArgumentList $a -WorkingDirectory (Split-Path $Exe) -WindowStyle Hidden | Out-Null
    for ($i = 0; $i -lt 90; $i++) { Start-Sleep 2; try { $r = Invoke-WebRequest "$base/System/Info/Public" -TimeoutSec 5 -SkipHttpErrorCheck; if ([int]$r.StatusCode -eq 200 -and $r.Headers["Content-Type"] -match "json") { return } } catch { } }
    throw "server did not start"
}

function Login-Ready { for ($i = 0; $i -lt 60; $i++) { try { return (Login "root" "root" "catalog") } catch { Start-Sleep 3 } }; throw "cannot log in" }

# A server that has never seen the plugin.
Stop-Mine
Get-ChildItem "$data\plugins" -Directory -Filter "SyncVote_*" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
Remove-Item "$data\plugins\configurations\KappuCitti.Plugin.SyncVote.xml" -ErrorAction SilentlyContinue
Start-Mine
$adm = Login-Ready
$server = (Invoke-RestMethod "$base/System/Info/Public").Version

# What a user does: add the repository, look at the catalog, install.
# Right after a start the server can still answer 401 to an admin token: log in again until it does not.
$repos = $null
for ($i = 0; $i -lt 30 -and $null -eq $repos; $i++) { try { $repos = @(Invoke-RestMethod "$base/Repositories" -Headers $adm.Headers | ForEach-Object { $_ }) } catch { Start-Sleep 4; $adm = Login-Ready } }
if ($null -eq $repos) { throw "the server never accepted the admin token" }
if (-not ($repos | Where-Object { $_.Url -eq $manifestUrl })) { $repos += [pscustomobject]@{ Name = "SyncVote"; Url = $manifestUrl; Enabled = $true } }
$body = "[" + ((@($repos) | ForEach-Object { ConvertTo-Json -InputObject $_ -Compress -Depth 3 }) -join ",") + "]"
Invoke-RestMethod "$base/Repositories" -Method Post -Headers $adm.Headers -ContentType "application/json" -Body $body | Out-Null
$pkg = $null; for ($i = 0; $i -lt 10 -and -not $pkg; $i++) { $pkg = @(Invoke-RestMethod "$base/Packages" -Headers $adm.Headers | ForEach-Object { $_ }) | Where-Object { ($_.guid -replace '-','') -eq ($guid -replace '-','') }; if (-not $pkg) { Start-Sleep 2 } }
if (-not $pkg) { throw "the catalog does not list SyncVote" }
"Jellyfin $server : the catalog lists SyncVote with versions: " + (($pkg.versions | ForEach-Object { "$($_.version) (abi $($_.targetAbi))" }) -join ", ")
Invoke-RestMethod "$base/Packages/Installed/SyncVote?assemblyGuid=$guid&repositoryUrl=$([uri]::EscapeDataString($manifestUrl))" -Method Post -Headers $adm.Headers | Out-Null
for ($i = 0; $i -lt 30; $i++) { Start-Sleep 2; if (Get-ChildItem "$data\plugins" -Directory -Filter "SyncVote_*" -ErrorAction SilentlyContinue) { break } }
$folder = Get-ChildItem "$data\plugins" -Directory -Filter "SyncVote_*" | Select-Object -First 1
"installed folder: $($folder.Name)   files: $((Get-ChildItem $folder.FullName | ForEach-Object Name) -join ', ')"
Stop-Mine; Start-Mine
$adm = Login-Ready
$p = @(Invoke-RestMethod "$base/Plugins" -Headers $adm.Headers | ForEach-Object { $_ }) | Where-Object { ($_.Id -replace '-','') -eq ($guid -replace '-','') }
"after restart: SyncVote $($p.Version) status=$($p.Status) image=$($p.HasImage)   expected version $Expect : $(if ($p.Version -eq $Expect -and $p.Status -eq 'Active') { 'OK' } else { 'MISMATCH' })"
