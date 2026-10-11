<#
.SYNOPSIS
    Drives the real Jellyfin web client in headless Chrome: SyncPlay menu, room creation, voting, results, play.
    Screenshots go to -OutDir. Needs a SyncPlay-capable server with one movie library and the plugin installed.
.PARAMETER LogDir
    The log folder of the server under test (used to confirm SyncPlay received the play request).
#>
param([string]$Base = "http://127.0.0.1:8096", [string]$AdminUser = "root", [string]$AdminPassword = "root", [string]$Tag = "",
      [switch]$ChangeServerLanguage,
      [string]$LogDir = (Join-Path $env:LOCALAPPDATA "jellyfin\log"), [string]$OutDir = (Join-Path ([IO.Path]::GetTempPath()) "syncvote-e2e"))

. "$PSScriptRoot\lib\jf.ps1" -Base $Base
. "$PSScriptRoot\lib\cdp.ps1"

$shots = "$OutDir\shots$Tag"
New-Item -ItemType Directory -Force $shots | Out-Null
$script:fail = 0
function Check($name, $cond, $detail = "") {
    if ($cond) { Write-Host "PASS  $name" -ForegroundColor Green } else { Write-Host "FAIL  $name  $detail" -ForegroundColor Red; $script:fail++ }
}
function Open-SyncPlayMenu {
    Js "document.querySelector('.headerSyncButton').click(); true" | Out-Null
    Wait-Js "!!document.querySelector('.syncPlayGroupMenu.opened')" 8 | Out-Null
    Start-Sleep -Milliseconds 600
}

$root = Login $AdminUser $AdminPassword "ui-device"
$serverConfig = Invoke-RestMethod "$Base/System/Configuration" -Headers $root.Headers
function Set-ServerLanguage($culture) {
    $cfg = Invoke-RestMethod "$Base/System/Configuration" -Headers $root.Headers
    $cfg.UICulture = $culture
    Invoke-RestMethod "$Base/System/Configuration" -Method Post -Headers $root.Headers -ContentType "application/json" -Body ($cfg | ConvertTo-Json -Depth 10) | Out-Null
}
# The screens follow the language of the server. Forcing a language changes the server setting for the duration of the test:
# only do it on a test server nobody is using (-ChangeServerLanguage). Otherwise the texts are checked against the current language.
if ($ChangeServerLanguage) { Set-ServerLanguage "en-US" }
$hasBoxSets = [bool]@((Invoke-RestMethod "$Base/SyncVote/Library/Collections" -Headers $root.Headers) | Where-Object { $_.Type -eq "boxset" })
$serverIsItalian = ((Invoke-RestMethod "$Base/System/Configuration" -Headers $root.Headers).UICulture -match '^it')
$T = if ($serverIsItalian) { @{ Create = "Crea stanza"; Close = "Chiudi stanza"; Folders = "Cartelle libreria"; Collections = "Collezioni"; Genres = "Generi"; NoCollections = "Ancora nessuna collezione" } } else { @{ Create = "Create room"; Close = "Close room"; Folders = "Library folders"; Collections = "Collections"; Genres = "Genres"; NoCollections = "No collections yet" } }
$expectedSections = if ($hasBoxSets) { "$($T.Folders)|$($T.Collections)|$($T.Genres)" } else { "$($T.Folders)|$($T.Genres)" }
# Remove stale rooms left by an interrupted run.
foreach ($r in @((Call $root GET "/SyncVote/Rooms").Body)) { Call $root DELETE "/SyncVote/Room/$($r.Id)" | Out-Null }
$existing = Invoke-RestMethod "$Base/Users" -Headers $root.Headers | Where-Object Name -eq "svtest"
if ($existing) { Invoke-RestMethod "$Base/Users/$($existing.Id)" -Method Delete -Headers $root.Headers | Out-Null }
$created = Invoke-RestMethod "$Base/Users/New" -Method Post -Headers $root.Headers -ContentType "application/json" -Body (@{ Name = "svtest"; Password = "svtest-pw" } | ConvertTo-Json)
$sv = Login "svtest" "svtest-pw" "devB"
$logFile = "$LogDir\log_$(Get-Date -Format yyyyMMdd).log"

try {
    Start-Chrome "$OutDir\e2e-chrome-profile"
    Send-Cdp "Page.navigate" @{ url = "$Base/web/index.html" } | Out-Null
    Wait-Js "!!document.querySelector('#txtManualName')" 25 | Out-Null
    Js "(function(){ const n=document.querySelector('#txtManualName'); const p=document.querySelector('#txtManualPassword'); n.value='$AdminUser'; p.value='$AdminPassword'; document.querySelector('.manualLoginForm').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true})); return true })()" | Out-Null
    $ready = Wait-Js "!!window.__syncVoteManager && !!window.ApiClient && !!ApiClient.accessToken()" 25
    Check "SyncVote client script loaded and user logged in" ([bool]$ready)
    Start-Sleep 2
    Save-Shot "$shots\01-home.png"
    Check "no literal template text in the page" (-not (Js "document.body.innerText.includes('`${title}') || document.body.innerText.includes('SyncVote Plugin Styles')"))
    Check "no purple floating button" (-not (Js "!!document.querySelector('.svFloatingButton')"))

    # The browser's own session creates the SyncPlay group; the second user joins through the API.
    Js "ApiClient.ajax({type:'POST', url: ApiClient.getUrl('SyncPlay/New'), data: JSON.stringify({GroupName:'ui-group'}), contentType:'application/json'}).then(()=>true)" | Out-Null
    $gid = $null
    for ($i = 0; $i -lt 20 -and -not $gid; $i++) { Start-Sleep -Milliseconds 500; $gid = (Call $root GET "/SyncPlay/List").Body | Where-Object GroupName -eq "ui-group" | Select-Object -First 1 -ExpandProperty GroupId }
    Check "SyncPlay group created from the browser" ([bool]$gid)
    $j = Call $sv POST "/SyncPlay/Join" @{ GroupId = $gid }
    Check "second user joined the group" ($j.Status -in 200,204) "status $($j.Status)"

    Write-Host "== SyncPlay menu"
    Open-SyncPlayMenu
    Check "SyncVote item is in the SyncPlay menu" ((Js "document.querySelectorAll('.syncPlayGroupMenu.opened .svSyncVoteItem').length") -eq 1)
    Save-Shot "$shots\02-menu.png"
    Js "document.querySelector('.svSyncVoteItem').click(); true" | Out-Null
    $form = Wait-Js "!!document.querySelector('#svRoomName')" 10
    Check "create dialog opens" ([bool]$form)
    Start-Sleep 1
    Check "SyncPlay menu is closed once SyncVote opens" ((Js "document.querySelectorAll('.syncPlayGroupMenu.opened').length") -eq 0) (Js "document.querySelectorAll('.syncPlayGroupMenu').length")
    Check "dialog uses Jellyfin's own markup" ((Js "!!document.querySelector('.svDialog.formDialog .formDialogHeader') && !!document.querySelector('.svDialog .formDialogFooter') && !!document.querySelector('#svRoomName.emby-input')"))
    Save-Shot "$shots\03-create.png"
    Check "form lists collections and genres" ((Js "document.querySelectorAll('.svCollection').length") -ge 1 -and (Js "document.querySelectorAll('.svGenre').length") -ge 1)
    Check "the dialog speaks the language of the server" ((Js "document.querySelector('.svCreate').innerText.trim()") -eq $T.Create) (Js "document.querySelector('.svCreate').innerText")
    $fill = Js "(function(){ const c=document.querySelector('.svDialog .formDialogContent'), b=document.querySelector('.svCreateBody'); return JSON.stringify({w: b.getBoundingClientRect().width / c.clientWidth, h: b.getBoundingClientRect().height - c.clientHeight}) })()" | ConvertFrom-Json
    Check "the create form fills the whole dialog (width and height)" ($fill.w -ge 0.95 -and $fill.h -ge -2) ($fill | ConvertTo-Json -Compress)
    foreach ($height in 900, 640) {
        Send-Cdp "Emulation.setDeviceMetricsOverride" @{ width = 1100; height = $height; deviceScaleFactor = 1; mobile = $false } | Out-Null
        Start-Sleep -Milliseconds 600
        $g = Js "(function(){ const q=s=>document.querySelector(s).getBoundingClientRect(); const c=document.querySelector('.svDialog .formDialogContent'); const f=q('.svForm'), d=q('.svDialog'); return JSON.stringify({selectsAboveFooter: q('.svSelects').bottom <= q('.formDialogFooter').top + 1, scroll: c.scrollHeight - c.clientHeight, formWidthShare: f.width / d.width}) })()" | ConvertFrom-Json
        Check "create form at height ${height}: nothing hidden behind the footer, no scrolling, nearly full width" ($g.selectsAboveFooter -and $g.scroll -le 1 -and $g.formWidthShare -ge 0.9) ($g | ConvertTo-Json -Compress)
    }
    Send-Cdp "Emulation.setDeviceMetricsOverride" @{ width = 1100; height = 900; deviceScaleFactor = 1; mobile = $false } | Out-Null
    Start-Sleep -Milliseconds 600
    Check "sections match the server (library folders, collections only if there are any, genres)" ((Js "Array.from(document.querySelectorAll('.svCreateBody h2.sectionTitle')).map(h => h.innerText.trim()).join('|')") -eq $expectedSections) (Js "Array.from(document.querySelectorAll('.svCreateBody h2.sectionTitle')).map(h => h.innerText.trim()).join('|')")
    Check "the collections list shows up only when the server has collections" ((Js "document.querySelector('.svCreateBody').innerText.includes('$($T.Collections)')") -eq $hasBoxSets)
    Check "there is a title filter field" ((Js "!!document.querySelector('#svTitleFilter')"))
    Save-Shot "$shots\03b-create-fill.png"
    Check "no raw translation keys" (-not (Js "/field\.|dialog\.createRoom|btn\.createRoom/.test(document.querySelector('.svDialog').innerText)"))

    Js "document.querySelector('#svRoomName').value='Serata UI'; document.querySelector('#svTimeLimit').value='1'; document.querySelectorAll('.svCollection:checked').forEach(c => c.click()); document.querySelector('.svCreate').click(); true" | Out-Null
    Start-Sleep 1
    Check "creating without a library is refused with a message in the form" ((Js "!!document.querySelector('#svRoomName')") -and (Js "getComputedStyle(document.querySelector('#svFormError')).display !== 'none' && document.querySelector('#svFormError').innerText.length > 5") -and @((Call $root GET "/SyncVote/Rooms").Body).Count -eq 0)
    Js "document.querySelector('#svRoomName').value=''; document.querySelector('.svCreate').click(); true" | Out-Null
    Check "missing name shows a message in the form" ((Js "getComputedStyle(document.querySelector('#svFormError')).display !== 'none'") -and (Js "document.activeElement.id === 'svRoomName'"))
    Js "document.querySelector('#svRoomName').value='Serata UI'; true" | Out-Null
    Js "document.querySelector('.svCollection').click(); document.querySelector('.svCreate').click(); true" | Out-Null
    $lobby = Wait-Js "!!document.querySelector('#svLobbyMembers')" 10
    Check "lobby opens after creating the room" ([bool]$lobby)
    $room = (Call $root GET "/SyncVote/Rooms").Body | Where-Object Name -eq "Serata UI" | Select-Object -First 1
    Check "room exists for the SyncPlay group" ($room -and $room.SyncPlayGroupId -eq ([guid]$gid).ToString("N"))

    Call $sv POST "/SyncVote/Room/$($room.Id)/Join" | Out-Null
    $members = Wait-Js "document.querySelector('#svLobbyMembers') && document.querySelector('#svLobbyMembers').innerText.startsWith('2')" 8
    Check "lobby shows the second member" ([bool]$members) (Js "document.querySelector('#svLobbyMembers').innerText")
    Save-Shot "$shots\04-lobby.png"

    Write-Host "== voting"
    Js "document.querySelector('.svStart').click(); true" | Out-Null
    $card = Wait-Js "!!document.querySelector('#svCard h2')" 10
    Check "voting card shows a title" ([bool]$card)
    $title1 = Js "document.querySelector('#svCard h2').innerText"
    Start-Sleep 1.5
    Save-Shot "$shots\05-voting.png"
    $t1 = Js "document.querySelector('#svTime').innerText"
    Start-Sleep 3
    $t2 = Js "document.querySelector('#svTime').innerText"
    $secs = { param($x) $p = $x -split ':'; [int]$p[0] * 60 + [int]$p[1] }
    Check "timer counts down" ((& $secs $t2) -lt (& $secs $t1) -and (& $secs $t1) -le 60) "t1=$t1 t2=$t2"
    Check "progress bar follows the timer" ([double](Js "parseFloat(document.querySelector('#svProgressBar').style.width)") -lt 100)
    foreach ($size in @(@(1100, 900, "tall"), @(1100, 560, "short"), @(420, 700, "phone"))) {
        Send-Cdp "Emulation.setDeviceMetricsOverride" @{ width = $size[0]; height = $size[1]; deviceScaleFactor = 1; mobile = $false } | Out-Null
        Start-Sleep -Milliseconds 700
        $r = Js "(function(){ const q=s=>document.querySelector(s).getBoundingClientRect(); const l=q('#svLike'), d=q('#svDislike'); return JSON.stringify({likeOk: l.top>=0 && l.bottom<=innerHeight, dislikeOk: d.top>=0 && d.bottom<=innerHeight, row: getComputedStyle(document.querySelector('#svCard')).display, vh: innerHeight}) })()" | ConvertFrom-Json
        Check "vote buttons visible without scrolling ($($size[2]) $($size[0])x$($size[1]))" ($r.likeOk -and $r.dislikeOk) ($r | ConvertTo-Json -Compress)
        $m = Js "(function(){ const c=document.querySelector('.svDialog .formDialogContent'); const l=document.querySelector('#svLike').getBoundingClientRect(), d=document.querySelector('#svDislike').getBoundingClientRect(); const f=document.querySelector('.svDialog .formDialogFooter').getBoundingClientRect(); const mid=(f.left+f.right)/2; return JSON.stringify({scroll: c.scrollHeight - c.clientHeight, dy: Math.abs((l.top+l.height/2)-(d.top+d.height/2)), dx: Math.abs((mid-(d.left+d.width/2)) - ((l.left+l.width/2)-mid)), endInFooter: !!document.querySelector('.svDialog .formDialogFooter .svEnd'), endInHeader: !!document.querySelector('.svDialog .formDialogHeader .svEnd')}) })()" | ConvertFrom-Json
        Check "the voting view needs no scrolling ($($size[2]))" ($m.scroll -le 1) ($m | ConvertTo-Json -Compress)
        Check "like and dislike are aligned and centered ($($size[2]))" ($m.dy -le 1 -and $m.dx -le 3) ($m | ConvertTo-Json -Compress)
        Check "end voting is a header icon, not next to the votes ($($size[2]))" ($m.endInHeader -and -not $m.endInFooter) ($m | ConvertTo-Json -Compress)
        if ($size[2] -eq "short") { Check "short screen: poster left, text right" ($r.row -eq "flex") ($r | ConvertTo-Json -Compress) }
        Save-Shot "$shots\05-voting-$($size[2]).png"
    }
    Send-Cdp "Emulation.setDeviceMetricsOverride" @{ width = 1100; height = 900; deviceScaleFactor = 1; mobile = $false } | Out-Null
    Start-Sleep -Milliseconds 700
    $h1 = Js "document.querySelector('#svCard').offsetHeight"
    $y1 = Js "Math.round(document.querySelector('#svLike').getBoundingClientRect().top)"
    Check "poster slot leaves the skeleton state (loaded, or no poster available)" ([bool](Wait-Js "!!document.querySelector('.svPosterBox.svLoaded')" 5))

    Js "document.querySelector('#svLike').click(); true" | Out-Null
    $moved = Wait-Js "document.querySelector('#svCard h2') && document.querySelector('#svCard h2').innerText !== $((ConvertTo-Json $title1))" 8
    Check "like moves to the next title" ([bool]$moved)
    Start-Sleep 1
    $h2 = Js "document.querySelector('#svCard').offsetHeight"
    $y2 = Js "Math.round(document.querySelector('#svLike').getBoundingClientRect().top)"
    Check "the layout does not move between titles" ($h1 -eq $h2 -and $y1 -eq $y2) "card $h1 -> $h2, buttons $y1 -> $y2"
    Check "next poster was preloaded" ((Js "Object.keys(__syncVoteManager.preloaded || {}).length") -ge 3)
    Js "document.querySelector('#svDislike').click(); true" | Out-Null
    Start-Sleep 1
    Js "document.dispatchEvent(new KeyboardEvent('keydown', {key:'ArrowRight'})); true" | Out-Null
    Start-Sleep 1
    Check "three votes counted in the UI" ((Js "document.querySelector('#svVotesCount').innerText") -match '^3 ') (Js "document.querySelector('#svVotesCount').innerText")
    $res = Call $root GET "/SyncVote/Room/$($room.Id)/Results"
    Check "server recorded the two likes" (@($res.Body.LikedItems).Count -eq 2) "liked=$(@($res.Body.LikedItems).Count)"

    Call $sv POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = @($res.Body.LikedItems)[0].ItemId; IsLike = $true } | Out-Null
    Js "document.querySelector('.svEnd').click(); true" | Out-Null
    $results = Wait-Js "document.querySelectorAll('.svDialog .listItem').length >= 1" 10
    Check "ending the vote shows the results" ([bool]$results)
    Start-Sleep 1
    Save-Shot "$shots\06-results.png"
    Check "winner is the title liked by both" ((Js "document.querySelector('.svDialog .listItemBodyText').innerText") -like "$(@($res.Body.LikedItems)[0].Name)*")

    Write-Host "== new round ends by itself when the time is up"
    Js "document.querySelector('.svAgain').click(); true" | Out-Null
    Check "new round starts" ([bool](Wait-Js "!!document.querySelector('#svCard h2')" 10))
    Js "document.querySelector('#svLike').click(); true" | Out-Null
    Start-Sleep 2
    $auto = Wait-Js "!!document.querySelector('.svPlay')" 70
    Check "results appear without anyone ending the vote" ([bool]$auto)

    Write-Host "== play the winner"
    Js "document.querySelector('.svPlay').click(); true" | Out-Null
    Start-Sleep 2
    Check "panel closes and a message is shown on top" ((Js "!document.querySelector('.svDialog')") -and (Js "!!document.querySelector('.svToast') && parseInt(getComputedStyle(document.querySelector('.svToast')).zIndex) > 999999"))
    Check "SyncPlay group received the play request" ((Get-Content $logFile | Select-String 'requested Play in group' | Select-Object -Last 1) -ne $null)

    Write-Host "== reopen and close the room"
    if ($ChangeServerLanguage) { Set-ServerLanguage "it-IT"; $T.Close = "Chiudi stanza" }
    Open-SyncPlayMenu
    Js "document.querySelector('.svSyncVoteItem').click(); true" | Out-Null
    $again = Wait-Js "!!document.querySelector('.svCloseRoom')" 8
    Check "reopening from the menu shows the room" ([bool]$again)
    Check "the buttons are in the language of the server (picked up without reloading the page)" ((Js "document.querySelector('.svCloseRoom').innerText.trim()") -eq $T.Close) (Js "document.querySelector('.svCloseRoom').innerText")
    Js "document.querySelector('.svCloseRoom').click(); true" | Out-Null
    Start-Sleep 1
    Check "closing the room removes it and the dialog" ((@((Call $root GET "/SyncVote/Rooms").Body).Count -eq 0) -and (Js "!document.querySelector('.svDialog')"))

    $errors = $script:console | Where-Object { $_ -match '^\[(error|exception)\]' -and $_ -match 'SyncVote|syncvote' }
    Check "no SyncVote console errors" (-not $errors) ($errors -join " | ")
    $script:console | Where-Object { $_ -match 'SyncVote' } | Select-Object -First 5 | ForEach-Object { Write-Host "      console: $_" }

    Call $root POST "/SyncPlay/Leave" | Out-Null
}
finally {
    Stop-Chrome
    if ($ChangeServerLanguage) { Invoke-RestMethod "$Base/System/Configuration" -Method Post -Headers $root.Headers -ContentType "application/json" -Body ($serverConfig | ConvertTo-Json -Depth 10) | Out-Null }
    Invoke-RestMethod "$Base/Users/$($created.Id)" -Method Delete -Headers $root.Headers | Out-Null
    Write-Host "temporary user removed"
}

if ($script:fail -eq 0) { Write-Host "ALL UI CHECKS PASSED" -ForegroundColor Green } else { Write-Host "$($script:fail) UI CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
