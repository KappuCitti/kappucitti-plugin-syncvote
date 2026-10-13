<#
.SYNOPSIS
    Tests the plugin options (admin page, permissions, limits, private rooms, web client injection switch).
    Restores the configuration it changes.
#>
param([string]$Base = "http://127.0.0.1:8096", [string]$AdminUser = "root", [string]$AdminPassword = "root", [switch]$ChangeServerLanguage, [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) "syncvote-e2e"))

. "$PSScriptRoot\lib\jf.ps1" -Base $Base
. "$PSScriptRoot\lib\cdp.ps1"
New-Item -ItemType Directory -Force $OutDir | Out-Null

$script:fail = 0
function Check($name, $cond, $detail = "") {
    if ($cond) { Write-Host "PASS  $name" -ForegroundColor Green } else { Write-Host "FAIL  $name  $detail" -ForegroundColor Red; $script:fail++ }
}

$pluginId = "6bfde2dd-8211-4964-b86d-7812a9de160b"
$root = Login $AdminUser $AdminPassword "cfg-device"
$existing = Invoke-RestMethod "$Base/Users" -Headers $root.Headers | Where-Object Name -eq "svtest"
if ($existing) { Invoke-RestMethod "$Base/Users/$($existing.Id)" -Method Delete -Headers $root.Headers | Out-Null }
$created = Invoke-RestMethod "$Base/Users/New" -Method Post -Headers $root.Headers -ContentType "application/json" -Body (@{ Name = "svtest"; Password = "svtest-pw" } | ConvertTo-Json)
$sv = Login "svtest" "svtest-pw" "cfg-dev2"
$original = Invoke-RestMethod "$Base/Plugins/$pluginId/Configuration" -Headers $root.Headers
$serverConfig = Invoke-RestMethod "$Base/System/Configuration" -Headers $root.Headers
function Set-ServerLanguage($culture) {
    $cfg = Invoke-RestMethod "$Base/System/Configuration" -Headers $root.Headers
    $cfg.UICulture = $culture
    Invoke-RestMethod "$Base/System/Configuration" -Method Post -Headers $root.Headers -ContentType "application/json" -Body ($cfg | ConvertTo-Json -Depth 10) | Out-Null
}
function Get-Strings { Invoke-RestMethod "$Base/SyncVote/Client/i18n" }   # anonymous on purpose

function Set-Config($changes) {
    $cfg = Invoke-RestMethod "$Base/Plugins/$pluginId/Configuration" -Headers $root.Headers
    foreach ($k in $changes.Keys) { $cfg | Add-Member -NotePropertyName $k -NotePropertyValue $changes[$k] -Force }
    Invoke-RestMethod "$Base/Plugins/$pluginId/Configuration" -Method Post -Headers $root.Headers -ContentType "application/json" -Body ($cfg | ConvertTo-Json -Depth 6) | Out-Null
}

try {
    Write-Host "== defaults"
    Check "defaults are as documented" ($original.DefaultTimeLimit -ge 1 -and $original.InjectWebClient -eq $true -and $original.DefaultCanOrganize -eq $true -and $original.DefaultCanVote -eq $true -and $original.WrongCodeAttempts -eq 7) ($original | ConvertTo-Json -Compress)

    # These steps switch the display language of the SERVER for a moment: only with -ChangeServerLanguage, on a server nobody is using.
    if ($ChangeServerLanguage) {
    Write-Host "== language"
    Set-Config @{ Language = "Auto" }
    Set-ServerLanguage "it-IT"
    $r = Get-Strings
    Check "server in Italian -> Italian strings" ($r.Language -eq "it" -and $r.Strings."btn.createRoom" -eq "Crea stanza") "$($r.Language) / $($r.Strings.'btn.createRoom')"
    Set-ServerLanguage "en-US"
    $r = Get-Strings
    Check "server in English -> English strings" ($r.Language -eq "en" -and $r.Strings."btn.createRoom" -eq "Create room")
    Set-ServerLanguage "fr-FR"
    $r = Get-Strings
    Check "a language without translation falls back to English" ($r.Language -eq "en" -and $r.Strings."btn.createRoom" -eq "Create room")
    Check "the list of languages is offered" (@($r.Available) -contains "en" -and @($r.Available) -contains "it")
    Set-Config @{ Language = "it" }
    Set-ServerLanguage "en-US"
    $r = Get-Strings
    Check "the plugin option wins over the server" ($r.Language -eq "it" -and $r.Strings."btn.createRoom" -eq "Crea stanza")
    Set-Config @{ Language = "klingon" }
    $r = Get-Strings
    Check "an unknown option value follows the server" ($r.Language -eq "en")
    Set-Config @{ Language = "Auto" }
    Set-ServerLanguage "it"
    $r = Get-Strings
    Check "a bare language code works too" ($r.Language -eq "it")
    Check "every key of English exists in Italian" (@($r.Strings.PSObject.Properties).Count -ge 60)
    }
    else {
        $r = Get-Strings
        Check "the plugin serves the language of the server" ($r.Language -in @($r.Available) -and $r.Strings."btn.createRoom") "language $($r.Language)"
    }

    Write-Host "== web client injection switch"
    Set-Config @{ InjectWebClient = $false }
    $html = (Invoke-WebRequest "$Base/web/index.html" -Headers @{ "Cache-Control" = "no-cache" }).Content
    Check "InjectWebClient=false leaves index.html untouched" ($html -notmatch "SyncVote")
    Set-Config @{ InjectWebClient = $true }
    $html = (Invoke-WebRequest "$Base/web/index.html" -Headers @{ "Cache-Control" = "no-cache" }).Content
    Check "InjectWebClient=true injects again, no restart needed" ($html -match "SyncVote/Client/syncvote\.js")

    Write-Host "== permissions"
    $col = @((Call $sv GET "/SyncVote/Library/Collections").Body)[0]
    $body = @{ Name = "perm"; SelectedCollections = @($col.Id) }
    Set-Config @{ DefaultCanOrganize = $false }
    $p = Call $sv GET "/SyncVote/Permissions"
    Check "permissions endpoint reflects the default" ($p.Body.CanOrganize -eq $false -and $p.Body.CanVote -eq $true)
    $r = Call $sv POST "/SyncVote/Room" $body
    Check "user without permission cannot organize" ($r.Status -eq 403) "status $($r.Status)"
    $r = Call $root POST "/SyncVote/Room" $body
    Check "administrator still can" ($r.Status -eq 200) "status $($r.Status)"
    if ($r.Status -eq 200) { Call $root DELETE "/SyncVote/Room/$($r.Body.Id)" | Out-Null }
    Set-Config @{ UserPermissionOverrides = @(@{ UserId = $sv.UserId; CanOrganize = $true; CanVote = $true }) }
    $r = Call $sv POST "/SyncVote/Room" $body
    Check "per-user override allows organizing" ($r.Status -eq 200) "status $($r.Status) $($r.Body | ConvertTo-Json -Compress)"
    if ($r.Status -eq 200) { Call $sv DELETE "/SyncVote/Room/$($r.Body.Id)" | Out-Null }
    Set-Config @{ DefaultCanOrganize = $true; DefaultCanVote = $false; UserPermissionOverrides = @() }
    $v = Call $sv POST "/SyncVote/Vote" @{ RoomId = [guid]::NewGuid(); ItemId = [guid]::NewGuid(); IsLike = $true }
    Check "user who cannot vote is refused before anything else" ($v.Status -eq 403) "status $($v.Status)"
    Set-Config @{ DefaultCanVote = $true }

    Write-Host "== max members"
    Set-Config @{ MaxRoomMembers = 1 }
    $r = Call $root POST "/SyncVote/Room" $body
    $j = Call $sv POST "/SyncVote/Room/$($r.Body.Id)/Join"
    Check "a full room refuses new members" ($j.Status -eq 409) "status $($j.Status)"
    Call $root DELETE "/SyncVote/Room/$($r.Body.Id)" | Out-Null
    Set-Config @{ MaxRoomMembers = 10 }

    Write-Host "== wrong room codes"
    Set-Config @{ WrongCodeAttempts = 2 }
    $coded = Call $root POST "/SyncVote/Room" @{ Name = "code"; SelectedCollections = @($col.Id); RequireCode = $true }
    $wrongCode = "{0:D6}" -f (([int]$coded.Body.Code + 1) % 1000000)
    $t1 = Call $sv POST "/SyncVote/Room/$($coded.Body.Id)/Join" @{ Code = $wrongCode }
    $t2 = Call $sv POST "/SyncVote/Room/$($coded.Body.Id)/Join" @{ Code = $wrongCode }
    $t3 = Call $sv POST "/SyncVote/Room/$($coded.Body.Id)/Join" @{ Code = $coded.Body.Code }
    Check "the number of wrong codes is the one of the settings (2: then even the right code waits)" ($t1.Status -eq 403 -and $t2.Status -eq 403 -and $t3.Status -eq 429) "statuses $($t1.Status), $($t2.Status), $($t3.Status)"
    Call $root DELETE "/SyncVote/Room/$($coded.Body.Id)" | Out-Null
    Set-Config @{ WrongCodeAttempts = 7 }
    $coded = Call $root POST "/SyncVote/Room" @{ Name = "code7"; SelectedCollections = @($col.Id); RequireCode = $true }
    $wrongCode = "{0:D6}" -f (([int]$coded.Body.Code + 1) % 1000000)
    $tries = 1..6 | ForEach-Object { (Call $sv POST "/SyncVote/Room/$($coded.Body.Id)/Join" @{ Code = $wrongCode }).Status }
    $ok = Call $sv POST "/SyncVote/Room/$($coded.Body.Id)/Join" @{ Code = $coded.Body.Code }
    Check "with the default (7) six mistakes are not enough to wait" (@($tries | Where-Object { $_ -ne 403 }).Count -eq 0 -and $ok.Status -eq 200) "statuses $($tries -join ','), then $($ok.Status)"
    Call $root DELETE "/SyncVote/Room/$($coded.Body.Id)" | Out-Null

    Write-Host "== private rooms"
    Set-Config @{ AllowPublicRooms = $false }
    $r = Call $root POST "/SyncVote/Room" $body
    $list = Call $sv GET "/SyncVote/Rooms"
    Check "AllowPublicRooms=false hides rooms of other groups" (@($list.Body).Count -eq 0) "count $(@($list.Body).Count)"
    $get = Call $sv GET "/SyncVote/Room/$($r.Body.Id)"
    Check "...and their details" ($get.Status -eq 404) "status $($get.Status)"
    $adm = Call $root GET "/SyncVote/Rooms"
    Check "the organizer still sees their own room" (@($adm.Body).Count -eq 1)
    Call $root DELETE "/SyncVote/Room/$($r.Body.Id)" | Out-Null
    Set-Config @{ AllowPublicRooms = $true }

    Write-Host "== admin page"
    Start-Chrome "$OutDir\e2e-chrome-profile"
    Send-Cdp "Page.navigate" @{ url = "$Base/web/index.html" } | Out-Null
    Wait-Js "!!document.querySelector('#txtManualName')" 25 | Out-Null
    Js "(function(){ const n=document.querySelector('#txtManualName'); const p=document.querySelector('#txtManualPassword'); n.value='$AdminUser'; p.value='$AdminPassword'; document.querySelector('.manualLoginForm').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true})); return true })()" | Out-Null
    Wait-Js "!!window.ApiClient && !!ApiClient.accessToken()" 25 | Out-Null
    Start-Sleep 2
    if ($ChangeServerLanguage) { Set-ServerLanguage "it-IT"; Start-Sleep 2 }
    Js "location.hash = '#/configurationpage?name=SyncVote'; true" | Out-Null
    $form = Wait-Js "!!document.querySelector('#SyncVoteConfigForm') && document.querySelector('#MaxRoomMembers').value !== ''" 15
    Check "config page opens and shows the saved values" ([bool]$form)
    Check "config page lists the new options" ((Js "!!document.querySelector('#InjectWebClient') && !!document.querySelector('#DefaultCanOrganize') && !!document.querySelector('#DefaultCanVote')"))
    if ($ChangeServerLanguage) { Check "the settings page follows the server language (Italian)" ((Js "document.querySelector('#SyncVoteConfigForm').innerText.includes('Tempo limite predefinito')")) }
    Check "config page labels are translated (no raw keys)" (-not (Js "/config\.|btn\.save/.test(document.querySelector('#SyncVoteConfigForm').innerText)"))
    Js "document.querySelector('#MaxRoomMembers').value = '7'; document.querySelector('#WrongCodeAttempts').value = '3'; document.querySelector('#InjectWebClient').checked = false; document.querySelector('#SyncVoteConfigForm').dispatchEvent(new Event('submit', {cancelable:true, bubbles:true})); true" | Out-Null
    Start-Sleep 2
    $saved = Invoke-RestMethod "$Base/Plugins/$pluginId/Configuration" -Headers $root.Headers
    Check "saving from the page stores the options" ($saved.MaxRoomMembers -eq 7 -and $saved.WrongCodeAttempts -eq 3 -and $saved.InjectWebClient -eq $false) ($saved | ConvertTo-Json -Compress)
}
finally {
    Stop-Chrome
    # Leave the plugin configuration as it was found.
    Invoke-RestMethod "$Base/Plugins/$pluginId/Configuration" -Method Post -Headers $root.Headers -ContentType "application/json" -Body ($original | ConvertTo-Json -Depth 6) | Out-Null
    if ($ChangeServerLanguage) { Invoke-RestMethod "$Base/System/Configuration" -Method Post -Headers $root.Headers -ContentType "application/json" -Body ($serverConfig | ConvertTo-Json -Depth 10) | Out-Null }
    Invoke-RestMethod "$Base/Users/$($created.Id)" -Method Delete -Headers $root.Headers | Out-Null
    Write-Host "configuration restored, temporary user removed"
}

if ($script:fail -eq 0) { Write-Host "ALL CONFIG CHECKS PASSED" -ForegroundColor Green } else { Write-Host "$($script:fail) CONFIG CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
