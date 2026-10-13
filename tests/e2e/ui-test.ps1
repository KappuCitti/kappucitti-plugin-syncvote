<#
.SYNOPSIS
    Drives the real Jellyfin web client in headless Chrome: SyncPlay menu, room creation, voting, results, play.
    Screenshots go to -OutDir. Needs a SyncPlay-capable server with one movie library and the plugin installed.
.PARAMETER LogDir
    The log folder of the server under test (used to confirm SyncPlay received the play request).
.PARAMETER Layout
    Which web client layout to test. "default" is what the web client picks by itself (the new Material UI layout on 12, the
    classic one before). "legacy" forces the classic layout and "modern" the Material UI one (the "experimental" layout in 10.x).
    Run the test once for each of them: the SyncPlay menu is a different piece of the page in each.
#>
param([string]$Base = "http://127.0.0.1:8096", [string]$AdminUser = "root", [string]$AdminPassword = "root", [string]$Tag = "",
      [ValidateSet("default", "legacy", "modern")][string]$Layout = "default",
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

# The web client stores the layout in localStorage. 12 renamed the classic one "desktop-legacy" and made the Material UI layout the
# default (no value); before 12 the classic one is the default and the Material UI one is called "experimental".
$serverMajor = [int]((Invoke-RestMethod "$Base/System/Info/Public").Version -split '\.')[0]
$layoutValue = switch ($Layout) {
    "legacy" { if ($serverMajor -ge 12) { "desktop-legacy" } else { "desktop" } }
    "modern" { if ($serverMajor -ge 12) { "" } else { "experimental" } }
    default { $null }
}

# True when the Material UI header is the one on screen (the classic header stays in the page, hidden, in that case).
function Test-ModernHeader { [bool](Js "(function(){ const s = document.querySelector('.MuiAppBar-root svg[data-testid=GroupsIcon]'); return !!s && s.getBoundingClientRect().width > 0 })()") }
function Open-SyncPlayMenu {
    Js "(function(){ const s = document.querySelector('.MuiAppBar-root svg[data-testid=GroupsIcon]'); const modern = !!s && s.getBoundingClientRect().width > 0; (modern ? s.closest('button') : document.querySelector('.headerSyncButton')).click(); return true })()" | Out-Null
    Wait-Js "!!document.querySelector('.syncPlayGroupMenu.opened') || !!document.querySelector('#app-sync-play-menu:not(.MuiModal-hidden) ul[role=menu]')" 8 | Out-Null
    Start-Sleep -Milliseconds 600
}
# The SyncPlay menu is closed when neither the classic action sheet nor the Material UI popover is open.
$menuClosed = "!document.querySelector('.syncPlayGroupMenu.opened') && !document.querySelector('#app-sync-play-menu:not(.MuiModal-hidden)')"
# Icon and text of every button in the footer of the dialog: are they on one line, centered with each other?
$footerButtons = "(function(){ return JSON.stringify(Array.from(document.querySelectorAll('.svDialog .formDialogFooter .formDialogFooterItem')).map(b => { const icon = b.querySelector('.material-icons'); const text = b.querySelector('span:not(.material-icons)'); const range = document.createRange(); range.selectNodeContents(text); const i = icon ? icon.getBoundingClientRect() : null, tr = range.getBoundingClientRect(), br = b.getBoundingClientRect(); return { label: text.textContent.trim(), lines: range.getClientRects().length, dy: i ? Math.round(Math.abs((i.top + i.height / 2) - (tr.top + tr.height / 2)) * 10) / 10 : 0, inside: br.left <= (i ? i.left : tr.left) + 0.5 && tr.right <= br.right + 0.5 } })) })()"

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
$T = if ($serverIsItalian) { @{ Create = "Crea stanza"; Close = "Chiudi stanza"; Folders = "Cartelle libreria"; Collections = "Collezioni"; Genres = "Generi"; NoCollections = "Ancora nessuna collezione"; Runoff = "ballottaggio" } } else { @{ Create = "Create room"; Close = "Close room"; Folders = "Library folders"; Collections = "Collections"; Genres = "Genres"; NoCollections = "No collections yet"; Runoff = "runoff" } }
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
    if ($null -ne $layoutValue) {
        # The layout is read when the page starts: set it, then load the page again.
        Js $(if ($layoutValue) { "localStorage.setItem('layout', '$layoutValue'); true" } else { "localStorage.removeItem('layout'); true" }) | Out-Null
        Send-Cdp "Page.navigate" @{ url = "$Base/web/index.html" } | Out-Null
        Wait-Js "!!document.querySelector('#txtManualName')" 25 | Out-Null
    }
    Js "(function(){ const n=document.querySelector('#txtManualName'); const p=document.querySelector('#txtManualPassword'); n.value='$AdminUser'; p.value='$AdminPassword'; document.querySelector('.manualLoginForm').dispatchEvent(new Event('submit',{cancelable:true,bubbles:true})); return true })()" | Out-Null
    $ready = Wait-Js "!!window.__syncVoteManager && !!window.ApiClient && !!ApiClient.accessToken()" 25
    Check "SyncVote client script loaded and user logged in" ([bool]$ready)
    Start-Sleep 3
    $modern = Test-ModernHeader
    Write-Host "      web client layout on screen: $(if ($modern) { 'modern (Material UI header)' } else { 'classic header' })"
    if ($Layout -ne "default") { Check "the web client shows the $Layout layout" ($modern -eq ($Layout -eq "modern")) "modern=$modern" }
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
    $menuItems = if ($modern) { "#app-sync-play-menu .svSyncVoteItem" } else { ".syncPlayGroupMenu.opened .svSyncVoteItem" }
    Check "SyncVote item is in the SyncPlay menu, once" ((Js "document.querySelectorAll('$menuItems').length") -eq 1) (Js "document.querySelectorAll('.svSyncVoteItem').length")
    Check "no SyncVote item is left anywhere else in the page" ((Js "document.querySelectorAll('.svSyncVoteItem').length") -eq 1)
    if ($modern) {
        # The entry is a copy of a native one: same height, same text, same padding, and an icon that is really drawn.
        $look = Js "(function(){ const sv = document.querySelector('.svSyncVoteItem'); const native = Array.from(sv.parentElement.querySelectorAll('li[role=menuitem]')).find(i => i !== sv); const cs = e => getComputedStyle(e), r = e => e.getBoundingClientRect(); const text = e => e.querySelector('.MuiListItemText-primary'); const icon = sv.querySelector('.svMenuIcon'); return JSON.stringify({ hasNative: !!native, height: native ? Math.abs(r(sv).height - r(native).height) : null, font: native ? cs(text(sv)).fontSize === cs(text(native)).fontSize : null, left: native ? cs(sv).paddingLeft === cs(native).paddingLeft : null, label: text(sv).innerText.trim(), iconWidth: Math.round(r(icon).width), iconX: native ? Math.abs(r(icon).left - r(native.querySelector('.MuiListItemIcon-root')).left) < 4 : null, listed: sv.parentElement.matches('ul[role=menu]') }) })()" | ConvertFrom-Json
        Check "the menu entry looks like the native ones (height, font, padding, icon position)" ($look.hasNative -and $look.height -le 1 -and $look.font -and $look.left -and $look.iconX -and $look.iconWidth -ge 16) ($look | ConvertTo-Json -Compress)
        Check "the menu entry is inside the list of the menu" ($look.listed -and $look.label -eq "SyncVote") ($look | ConvertTo-Json -Compress)
    }
    Save-Shot "$shots\02-menu.png"
    Js "document.querySelector('.svSyncVoteItem').click(); true" | Out-Null
    $form = Wait-Js "!!document.querySelector('#svRoomName')" 10
    Check "create dialog opens" ([bool]$form)
    Start-Sleep 1
    Check "SyncPlay menu is closed once SyncVote opens" ([bool](Js $menuClosed)) (Js "document.querySelectorAll('.syncPlayGroupMenu, #app-sync-play-menu').length")
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

    Write-Host "== buttons of the results"
    Check "the results have a button for each thing the organizer can do (play, runoff, new round, close)" ((Js "document.querySelectorAll('.svDialog .formDialogFooterItem').length") -eq 4 -and (Js "!!document.querySelector('.svPlay') && !!document.querySelector('.svRunoff') && !!document.querySelector('.svAgain') && !!document.querySelector('.svCloseRoom')"))
    foreach ($size in @(@(1100, 900, "wide"), @(420, 700, "phone"))) {
        Send-Cdp "Emulation.setDeviceMetricsOverride" @{ width = $size[0]; height = $size[1]; deviceScaleFactor = 1; mobile = $false } | Out-Null
        Start-Sleep -Milliseconds 700
        $buttons = @(Js $footerButtons | ConvertFrom-Json)
        $crooked = @($buttons | Where-Object { $_.dy -gt 1.5 -or $_.lines -ne 1 -or -not $_.inside })
        Check "icon and text of every button are on one line, centered ($($size[2]) $($size[0])x$($size[1]))" ($buttons.Count -eq 4 -and $crooked.Count -eq 0) (($buttons | ConvertTo-Json -Compress))
        Save-Shot "$shots\06-results-$($size[2]).png"
    }
    Send-Cdp "Emulation.setDeviceMetricsOverride" @{ width = 1100; height = 900; deviceScaleFactor = 1; mobile = $false } | Out-Null
    Start-Sleep -Milliseconds 700

    Write-Host "== runoff: vote again on the liked titles"
    Check "the runoff button tells how many titles it is about" ((Js "document.querySelector('.svRunoff').title") -match '2')  (Js "document.querySelector('.svRunoff').title")
    Js "document.querySelector('.svRunoff').click(); true" | Out-Null
    Check "the runoff starts a new round" ([bool](Wait-Js "!!document.querySelector('#svCard h2')" 10))
    $roRoom = (Call $root GET "/SyncVote/Room/$($room.Id)").Body
    Check "the room is in a runoff between the two liked titles" ($roRoom.IsRunoff -and $roRoom.IsVotingActive -and @($roRoom.RunoffItemIds).Count -eq 2) ($roRoom | ConvertTo-Json -Compress)
    Check "the title of the dialog says it is a runoff" ((Js "document.querySelector('.svDialog .formDialogHeaderTitle').innerText") -like "*($($T.Runoff))")
    $roFirst = Js "document.querySelector('#svCard h2').innerText"
    Js "document.querySelector('#svLike').click(); true" | Out-Null
    Check "the second runoff title shows up" ([bool](Wait-Js "document.querySelector('#svCard h2') && document.querySelector('#svCard h2').innerText !== $((ConvertTo-Json $roFirst))" 8))
    $roSecond = Js "document.querySelector('#svCard h2').innerText"
    Js "document.querySelector('#svLike').click(); true" | Out-Null
    Check "after the two titles of the runoff there is nothing left to vote on" ([bool](Wait-Js "!document.querySelector('#svCard h2')" 8))
    Check "both titles of the runoff were liked on the server" (@((Call $root GET "/SyncVote/Room/$($room.Id)/Results").Body.LikedItems).Count -eq 2)
    Check "the two cards were the two liked titles" ((@($roFirst, $roSecond) | Sort-Object) -join "|" -eq ((@($res.Body.LikedItems).Name | Sort-Object) -join "|")) "cards: $roFirst | $roSecond"
    Js "document.querySelector('.svEnd').click(); true" | Out-Null
    Check "ending the runoff shows its results" ([bool](Wait-Js "!!document.querySelector('.svRunoff') || !!document.querySelector('.svPlay')" 10))
    Check "the results of the runoff say so in the title" ((Js "document.querySelector('.svDialog .formDialogHeaderTitle').innerText") -like "*($($T.Runoff))")
    Save-Shot "$shots\06b-results-runoff.png"

    Write-Host "== new round ends by itself when the time is up"
    Js "document.querySelector('.svAgain').click(); true" | Out-Null
    Check "new round starts" ([bool](Wait-Js "!!document.querySelector('#svCard h2')" 10))
    $fullRound = (Call $root GET "/SyncVote/Room/$($room.Id)").Body
    $fullCandidates = (Call $root GET "/SyncVote/Room/$($room.Id)/Candidates?limit=1").Body
    Check "a new round is about every title again, not the runoff" (-not $fullRound.IsRunoff -and $fullCandidates.TotalCount -gt 2) "runoff=$($fullRound.IsRunoff) titles=$($fullCandidates.TotalCount)"
    Check "the title of the dialog no longer says runoff" ((Js "document.querySelector('.svDialog .formDialogHeaderTitle').innerText") -notlike "*($($T.Runoff))")
    Js "document.querySelector('#svLike').click(); true" | Out-Null
    Start-Sleep 2
    $auto = Wait-Js "!!document.querySelector('.svPlay')" 70
    Check "results appear without anyone ending the vote" ([bool]$auto)
    Check "with a single liked title there is no runoff to offer" (-not (Js "!!document.querySelector('.svRunoff')"))
    Check "the other three buttons are still there" ((Js "document.querySelectorAll('.svDialog .formDialogFooterItem').length") -eq 3)

    Write-Host "== play the winner"
    Js "document.querySelector('.svPlay').click(); true" | Out-Null
    Start-Sleep 2
    Check "panel closes and a message is shown on top" ((Js "!document.querySelector('.svDialog')") -and (Js "!!document.querySelector('.svToast') && parseInt(getComputedStyle(document.querySelector('.svToast')).zIndex) > 999999"))
    Check "SyncPlay group received the play request" ((Get-Content $logFile | Select-String 'requested Play in group' | Select-Object -Last 1) -ne $null)

    Write-Host "== reopen and close the room"
    if ($ChangeServerLanguage) { Set-ServerLanguage "it-IT"; $T.Close = "Chiudi stanza" }
    Open-SyncPlayMenu
    # In the Material UI menu the entry is ours, not React's: check it answers to the keyboard too.
    if ($modern) { Js "document.querySelector('.svSyncVoteItem').dispatchEvent(new KeyboardEvent('keydown', {key: 'Enter', bubbles: true, cancelable: true})); true" | Out-Null }
    else { Js "document.querySelector('.svSyncVoteItem').click(); true" | Out-Null }
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
