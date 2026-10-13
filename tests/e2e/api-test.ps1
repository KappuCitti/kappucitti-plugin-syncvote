<#
.SYNOPSIS
    End-to-end test of the SyncVote HTTP API against a running Jellyfin server (with the plugin installed).
.DESCRIPTION
    Uses the admin account plus a temporary user "svtest" (removed at the end) and a real SyncPlay group.
    The server needs at least one movie library.
#>
param([string]$Base = "http://127.0.0.1:8096", [string]$AdminUser = "root", [string]$AdminPassword = "root", [switch]$KeepUser)

. "$PSScriptRoot\lib\jf.ps1" -Base $Base

$script:fail = 0
function Check($name, $cond, $detail = "") {
    if ($cond) { Write-Host "PASS  $name" -ForegroundColor Green }
    else { Write-Host "FAIL  $name  $detail" -ForegroundColor Red; $script:fail++ }
}

$root = Login $AdminUser $AdminPassword "devA"
# Remove stale rooms left by an interrupted run.
foreach ($r in @((Call $root GET "/SyncVote/Rooms").Body)) { Call $root DELETE "/SyncVote/Room/$($r.Id)" | Out-Null }

# Temporary non-admin user, removed at the end.
$existing = Invoke-RestMethod "$Base/Users" -Headers $root.Headers | Where-Object Name -eq "svtest"
if ($existing) { Invoke-RestMethod "$Base/Users/$($existing.Id)" -Method Delete -Headers $root.Headers | Out-Null }
$created = Invoke-RestMethod "$Base/Users/New" -Method Post -Headers $root.Headers -ContentType "application/json" -Body (@{ Name = "svtest"; Password = "svtest-pw" } | ConvertTo-Json)
$sv = Login "svtest" "svtest-pw" "devB"

try {
    Write-Host "== auth / assets"
    $anon = Invoke-WebRequest "$Base/SyncVote/Rooms" -SkipHttpErrorCheck
    Check "API without token is rejected" ($anon.StatusCode -eq 401) "status $($anon.StatusCode)"
    $js = Invoke-WebRequest "$Base/SyncVote/Client/syncvote.js" -SkipHttpErrorCheck
    Check "client script served anonymously" ($js.StatusCode -eq 200 -and $js.Content.Length -gt 1000) "status $($js.StatusCode)"
    $bad = Invoke-WebRequest "$Base/SyncVote/Client/../../etc/passwd" -SkipHttpErrorCheck
    Check "unknown asset is 404" ($bad.StatusCode -eq 404) "status $($bad.StatusCode)"
    $idx = Invoke-WebRequest "$Base/web/index.html"
    Check "index.html carries the SyncVote script" ($idx.Content -match "SyncVote/Client/syncvote\.js")
    $idx2 = Invoke-WebRequest "$Base/web/"
    Check "/web/ carries the SyncVote script" ($idx2.Content -match "SyncVote/Client/syncvote\.js")
    Check "index.html is still the web client" ($idx.Content -match "</html>")

    Write-Host "== permissions"
    $p = Call $root GET "/SyncVote/Permissions"
    Check "own permissions" ($p.Status -eq 200 -and $p.Body.CanOrganize -and $p.Body.CanVote) "status $($p.Status)"
    $p2 = Call $sv GET "/SyncVote/Permissions?userId=$($root.UserId)"
    Check "non-admin cannot read others' permissions" ($p2.Status -eq 403) "status $($p2.Status)"
    $p3 = Call $root GET "/SyncVote/Permissions?userId=$($sv.UserId)"
    Check "admin can read others' permissions" ($p3.Status -eq 200) "status $($p3.Status)"

    Write-Host "== library"
    $c = Call $sv GET "/SyncVote/Library/Collections"
    Check "collections" ($c.Status -eq 200 -and @($c.Body).Count -ge 1) "status $($c.Status)"
    $g = Call $sv GET "/SyncVote/Library/Genres"
    Check "genres" ($g.Status -eq 200 -and @($g.Body).Count -ge 1) "status $($g.Status)"
    $r = Call $sv GET "/SyncVote/Library/ParentalRatings"
    Check "parental ratings" ($r.Status -eq 200 -and @($r.Body).Count -ge 1) "status $($r.Status)"
    $col = @($c.Body)[0]
    Write-Host "      collection '$($col.Name)' has $($col.ItemCount) items; $(@($g.Body).Count) genres; $(@($r.Body).Count) ratings"

    Write-Host "== no SyncPlay group yet"
    $i0 = Call $root GET "/SyncVote/SyncPlayInfo"
    Check "no group -> GroupId null" ($i0.Status -eq 200 -and -not $i0.Body.GroupId)

    Write-Host "== SyncPlay group with both users"
    $new = Call $root POST "/SyncPlay/New" @{ GroupName = "svtest-group" }
    Check "group created" ($new.Status -eq 204 -or $new.Status -eq 200) "status $($new.Status)"
    $groups = Call $root GET "/SyncPlay/List"
    $gid = @($groups.Body | Where-Object GroupName -eq "svtest-group")[0].GroupId
    Check "group listed" ([bool]$gid)
    $j = Call $sv POST "/SyncPlay/Join" @{ GroupId = $gid }
    Check "second user joined" ($j.Status -eq 204 -or $j.Status -eq 200) "status $($j.Status)"
    $i1 = Call $root GET "/SyncVote/SyncPlayInfo"
    Check "plugin sees the group" ($i1.Body.GroupId -eq ([guid]$gid).ToString("N")) "got '$($i1.Body.GroupId)' expected '$gid'"
    Check "plugin counts 2 members" ($i1.Body.MemberCount -eq 2) "count $($i1.Body.MemberCount)"
    $acc = Call $root POST "/SyncVote/CheckAccess" @{ CollectionIds = @($col.Id) }
    Check "access check answers" ($acc.Status -eq 200 -and $acc.Body.HasAccessIssues -eq $false) "status $($acc.Status)"

    Write-Host "== room lifecycle"
    $create = Call $root POST "/SyncVote/Room" @{ Name = "Serata"; TimeLimit = 3; SortBy = "Random"; SelectedCollections = @($col.Id); SelectedGenres = @(); ItemTypes = @("Movie") }
    Check "room created" ($create.Status -eq 200 -and $create.Body.Id) "status $($create.Status) $($create.Body | ConvertTo-Json -Compress)"
    $room = $create.Body
    Check "room bound to the SyncPlay group" ($room.SyncPlayGroupId -eq ([guid]$gid).ToString("N"))
    $empty = Call $root POST "/SyncVote/Room" @{ Name = "  "; SelectedCollections = @($col.Id) }
    Check "blank name rejected" ($empty.Status -eq 400) "status $($empty.Status)"
    $noLib = Call $root POST "/SyncVote/Room" @{ Name = "Senza libreria"; SelectedCollections = @() }
    Check "room without a library rejected" ($noLib.Status -eq 400 -and $noLib.Body.Error -eq "NoCollectionSelected") "status $($noLib.Status) $($noLib.Body | ConvertTo-Json -Compress)"
    $noLib2 = Call $root POST "/SyncVote/Room" @{ Name = "Senza libreria" }
    Check "room with the field missing rejected" ($noLib2.Status -eq 400) "status $($noLib2.Status)"

    $rooms = Call $sv GET "/SyncVote/Rooms"
    Check "other member sees the room" (@($rooms.Body | Where-Object Id -eq $room.Id).Count -eq 1)
    Check "the group info tells the room is in the lobby, to every member" ((Call $root GET "/SyncVote/SyncPlayInfo").Body.RoomState -eq "lobby" -and (Call $sv GET "/SyncVote/SyncPlayInfo").Body.RoomState -eq "lobby")
    $dup = Call $sv POST "/SyncVote/Room" @{ Name = "Altra"; SelectedCollections = @($col.Id) }
    Check "second organizer in same group refused" ($dup.Status -eq 409) "status $($dup.Status)"

    $cand0 = Call $sv GET "/SyncVote/Room/$($room.Id)/Candidates?limit=5"
    Check "non-member cannot read candidates" ($cand0.Status -eq 403) "status $($cand0.Status)"
    $join = Call $sv POST "/SyncVote/Room/$($room.Id)/Join"
    Check "member joins" ($join.Status -eq 200 -and @($join.Body.MemberIds).Count -eq 2) "status $($join.Status)"
    $join2 = Call $sv POST "/SyncVote/Room/$($room.Id)/Join"
    Check "joining twice is fine" ($join2.Status -eq 200) "status $($join2.Status)"

    $cand1 = Call $root GET "/SyncVote/Room/$($room.Id)/Candidates?limit=10"
    $cand2 = Call $sv GET "/SyncVote/Room/$($room.Id)/Candidates?limit=10"
    Check "candidates returned" ($cand1.Status -eq 200 -and @($cand1.Body.Items).Count -eq 10) "status $($cand1.Status)"
    Check "same order for every member" ((@($cand1.Body.Items).Id -join ",") -eq (@($cand2.Body.Items).Id -join ","))
    Check "candidates are movies with name" (@($cand1.Body.Items | Where-Object { $_.Name -and $_.Type -eq "Movie" }).Count -eq 10)
    $page2 = Call $root GET "/SyncVote/Room/$($room.Id)/Candidates?skip=10&limit=10"
    Check "paging gives other items" (@($page2.Body.Items | Where-Object { @($cand1.Body.Items).Id -contains $_.Id }).Count -eq 0)
    $items = @($cand1.Body.Items)

    $early = Call $sv POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[0].Id; IsLike = $true }
    Check "vote before start refused" ($early.Status -eq 400) "status $($early.Status)"
    $notOrg = Call $sv POST "/SyncVote/Room/$($room.Id)/StartVoting"
    Check "non-organizer cannot start" ($notOrg.Status -eq 403) "status $($notOrg.Status)"
    $start = Call $root POST "/SyncVote/Room/$($room.Id)/StartVoting"
    Check "organizer starts voting" ($start.Status -eq 200 -and $start.Body.IsVotingActive -and $start.Body.SecondsRemaining -gt 100) "status $($start.Status) rem=$($start.Body.SecondsRemaining)"
    $again = Call $root POST "/SyncVote/Room/$($room.Id)/StartVoting"
    Check "starting twice is a conflict" ($again.Status -eq 409) "status $($again.Status)"
    Check "the group info tells voting is on" ((Call $sv GET "/SyncVote/SyncPlayInfo").Body.RoomState -eq "voting")

    $v1 = Call $root POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[0].Id; IsLike = $true }
    $v2 = Call $sv POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[0].Id; IsLike = $true }
    $v3 = Call $root POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[1].Id; IsLike = $true }
    $v4 = Call $sv POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[1].Id; IsLike = $false }
    $v5 = Call $sv POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[2].Id; IsLike = $false }
    Check "votes accepted" (@($v1,$v2,$v3,$v4,$v5 | Where-Object { $_.Status -ne 204 }).Count -eq 0) "statuses $($v1.Status),$($v2.Status),$($v3.Status),$($v4.Status),$($v5.Status)"
    $emptyId = Call $root POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = [guid]::Empty; IsLike = $true }
    Check "vote with an empty item id is a 400, not a crash" ($emptyId.Status -eq 400) "status $($emptyId.Status)"
    $bogus = Call $root POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = [guid]::NewGuid(); IsLike = $true }
    Check "vote for unknown item refused" ($bogus.Status -eq 400) "status $($bogus.Status)"
    $other = Call $root POST "/SyncVote/Vote" @{ RoomId = [guid]::NewGuid(); ItemId = $items[0].Id; IsLike = $true }
    Check "vote in unknown room is 404" ($other.Status -eq 404) "status $($other.Status)"

    $left = Call $sv GET "/SyncVote/Room/$($room.Id)/Candidates?limit=10"
    Check "voted items are not offered again" (@($left.Body.Items | Where-Object { $_.Id -in @($items[0].Id, $items[1].Id, $items[2].Id) }).Count -eq 0)

    $res = Call $sv GET "/SyncVote/Room/$($room.Id)/Results"
    Check "results ranked" ($res.Status -eq 200 -and $res.Body.Winner.ItemId -eq $items[0].Id -and $res.Body.Winner.VoteCount -eq 2) "winner=$($res.Body.Winner.ItemId) votes=$($res.Body.Winner.VoteCount)"
    Check "second place has the dislike" (@($res.Body.LikedItems)[1].ItemId -eq $items[1].Id -and @($res.Body.LikedItems)[1].DislikeCount -eq 1)
    Check "winner name resolved" ($res.Body.Winner.Name -eq $items[0].Name)

    $endNo = Call $sv POST "/SyncVote/Room/$($room.Id)/EndVoting"
    Check "non-organizer cannot end" ($endNo.Status -eq 403) "status $($endNo.Status)"
    $end = Call $root POST "/SyncVote/Room/$($room.Id)/EndVoting"
    Check "organizer ends voting" ($end.Status -eq 200 -and $end.Body.IsVotingFinished -and -not $end.Body.IsVotingActive) "status $($end.Status)"
    Check "the group info tells the room is showing the results" ((Call $sv GET "/SyncVote/SyncPlayInfo").Body.RoomState -eq "results")
    $late = Call $sv POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[3].Id; IsLike = $true }
    Check "vote after end refused" ($late.Status -eq 400) "status $($late.Status)"

    Write-Host "== play the winner in SyncPlay"
    $playNo = Call $sv POST "/SyncVote/Room/$($room.Id)/PlayWinner"
    Check "only organizer plays winner" ($playNo.Status -eq 403) "status $($playNo.Status)"
    $play = Call $root POST "/SyncVote/Room/$($room.Id)/PlayWinner"
    Check "winner handed to SyncPlay" ($play.Status -eq 204) "status $($play.Status) $($play.Body | ConvertTo-Json -Compress)"
    Start-Sleep 1
    $sess = Invoke-RestMethod "$Base/Sessions" -Headers $root.Headers
    $queued = @($sess | Where-Object { $_.UserName -in @("root","svtest") -and $_.NowPlayingQueue }).Count
    Write-Host "      sessions with a play queue after PlayWinner: $queued (informational)"

    Write-Host "== runoff: vote again on the liked titles"
    # The round above ended with two titles liked: items[0] (2 likes) and items[1] (1 like).
    $roNo = Call $sv POST "/SyncVote/Room/$($room.Id)/StartVoting?onlyLiked=true"
    Check "non-organizer cannot start a runoff" ($roNo.Status -eq 403) "status $($roNo.Status)"
    $ro = Call $root POST "/SyncVote/Room/$($room.Id)/StartVoting?onlyLiked=true"
    Check "organizer starts a runoff" ($ro.Status -eq 200 -and $ro.Body.IsVotingActive -and $ro.Body.IsRunoff -and @($ro.Body.RunoffItemIds).Count -eq 2) "status $($ro.Status) $($ro.Body | ConvertTo-Json -Compress)"
    $roCand1 = Call $root GET "/SyncVote/Room/$($room.Id)/Candidates?limit=100"
    $roCand2 = Call $sv GET "/SyncVote/Room/$($room.Id)/Candidates?limit=100"
    $finalists = ((@($items[0].Id, $items[1].Id) | Sort-Object) -join ",")
    Check "the runoff offers only the two liked titles" (@($roCand1.Body.Items).Count -eq 2 -and ((@($roCand1.Body.Items).Id | Sort-Object) -join ",") -eq $finalists) "got $(@($roCand1.Body.Items).Name -join ', ')"
    Check "same order for every member in the runoff" ((@($roCand1.Body.Items).Id -join ",") -eq (@($roCand2.Body.Items).Id -join ","))
    $roFresh = Call $root GET "/SyncVote/Room/$($room.Id)/Results"
    Check "the runoff starts without the old votes" (@($roFresh.Body.LikedItems).Count -eq 0)
    $outside = Call $root POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[3].Id; IsLike = $true }
    Check "a vote for a title outside the runoff is refused" ($outside.Status -eq 400) "status $($outside.Status)"
    Call $root POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[1].Id; IsLike = $true } | Out-Null
    Call $root POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[0].Id; IsLike = $false } | Out-Null
    Call $sv POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[1].Id; IsLike = $true } | Out-Null
    Call $sv POST "/SyncVote/Vote" @{ RoomId = $room.Id; ItemId = $items[0].Id; IsLike = $false } | Out-Null
    Call $root POST "/SyncVote/Room/$($room.Id)/EndVoting" | Out-Null
    $roRes = Call $root GET "/SyncVote/Room/$($room.Id)/Results"
    Check "the runoff has its own winner" ($roRes.Body.Winner.ItemId -eq $items[1].Id -and $roRes.Body.Winner.VoteCount -eq 2) "winner=$($roRes.Body.Winner.Name)"
    $roAgain = Call $root POST "/SyncVote/Room/$($room.Id)/StartVoting?onlyLiked=true"
    Check "a runoff with a single liked title is refused" ($roAgain.Status -eq 400 -and $roAgain.Body.Error -eq "NotEnoughLikedTitles") "status $($roAgain.Status) $($roAgain.Body | ConvertTo-Json -Compress)"
    Check "the refused runoff leaves the results alone" ((Call $root GET "/SyncVote/Room/$($room.Id)").Body.IsVotingFinished -and (Call $root GET "/SyncVote/Room/$($room.Id)/Results").Body.Winner.ItemId -eq $items[1].Id)
    $plain = Call $root POST "/SyncVote/Room/$($room.Id)/StartVoting"
    $plainCand = Call $root GET "/SyncVote/Room/$($room.Id)/Candidates?limit=1"
    Check "a normal new round goes back to every title" ($plain.Status -eq 200 -and -not $plain.Body.IsRunoff -and $plainCand.Body.TotalCount -gt 2) "runoff=$($plain.Body.IsRunoff) total=$($plainCand.Body.TotalCount)"
    Call $root POST "/SyncVote/Room/$($room.Id)/EndVoting" | Out-Null

    Write-Host "== new round and close"
    $restart = Call $root POST "/SyncVote/Room/$($room.Id)/StartVoting"
    Check "organizer can start a new round" ($restart.Status -eq 200)
    $fresh = Call $root GET "/SyncVote/Room/$($room.Id)/Results"
    Check "new round starts without old votes" (@($fresh.Body.LikedItems).Count -eq 0)
    $closeNo = Call $sv DELETE "/SyncVote/Room/$($room.Id)"
    Check "non-organizer cannot close" ($closeNo.Status -eq 403) "status $($closeNo.Status)"
    $close = Call $root DELETE "/SyncVote/Room/$($room.Id)"
    Check "organizer closes the room" ($close.Status -eq 204) "status $($close.Status)"
    $gone = Call $root GET "/SyncVote/Room/$($room.Id)"
    Check "closed room is gone" ($gone.Status -eq 404) "status $($gone.Status)"
    Check "the group info has no room state any more" ($null -eq (Call $sv GET "/SyncVote/SyncPlayInfo").Body.RoomState)

    Write-Host "== filters"
    $f = Call $root POST "/SyncVote/Room" @{ Name = "Filtri"; TimeLimit = 1; SortBy = "Title"; SelectedCollections = @($col.Id); SelectedGenres = @(@($g.Body)[0]); ItemTypes = @("Movie") }
    Check "filtered room created" ($f.Status -eq 200) "status $($f.Status)"
    $fc = Call $root GET "/SyncVote/Room/$($f.Body.Id)/Candidates?limit=100"
    $allWithGenre = @($fc.Body.Items | Where-Object { $_.Genres -contains @($g.Body)[0] }).Count
    Check "every candidate matches the genre" ($fc.Status -eq 200 -and $fc.Body.TotalCount -gt 0 -and $allWithGenre -eq @($fc.Body.Items).Count) "total=$($fc.Body.TotalCount) matching=$allWithGenre of $(@($fc.Body.Items).Count)"
    $native = (Invoke-RestMethod "$Base/Items?Recursive=true&IncludeItemTypes=Movie&Genres=$([uri]::EscapeDataString(@($g.Body)[0]))&SortBy=SortName&SortOrder=Ascending&Limit=100" -Headers $root.Headers).Items.Id
    Check "title sort matches Jellyfin's own order" ((@($fc.Body.Items).Id -join ",") -eq ($native -join ","))
    Call $root DELETE "/SyncVote/Room/$($f.Body.Id)" | Out-Null

    Write-Host "== collections (box sets) and the title filter"
    $five = @((Invoke-RestMethod "$Base/Items?Recursive=true&IncludeItemTypes=Movie&Limit=5" -Headers $root.Headers).Items).Id
    $script:boxLibraryExisted = (@(Invoke-RestMethod "$Base/Library/VirtualFolders" -Headers $root.Headers | Where-Object { "$($_.CollectionType)" -eq "boxsets" }).Count -gt 0)
    $box = Invoke-RestMethod "$Base/Collections?name=svtest-collection&ids=$($five -join ',')" -Method Post -Headers $root.Headers
    $script:boxId = $box.Id
    $listed = @((Call $sv GET "/SyncVote/Library/Collections").Body) | Where-Object { $_.Type -eq "boxset" -and $_.Name -eq "svtest-collection" }
    Check "the collection is offered as a collection, with its size" ($listed -and $listed.ItemCount -eq 5) ($listed | ConvertTo-Json -Compress)
    $cr = Call $root POST "/SyncVote/Room" @{ Name = "Collection"; SelectedCollections = @($box.Id); ItemTypes = @("Movie") }
    Check "a room can be limited to a collection" ($cr.Status -eq 200) "status $($cr.Status)"
    $cc = Call $root GET "/SyncVote/Room/$($cr.Body.Id)/Candidates?limit=50"
    $got = @($cc.Body.Items).Id
    Check "only the titles of the collection are offered" ($cc.Body.TotalCount -eq 5 -and @($got | Where-Object { $five -contains $_ }).Count -eq 5) "total $($cc.Body.TotalCount)"
    Call $root DELETE "/SyncVote/Room/$($cr.Body.Id)" | Out-Null
    $both = Call $root POST "/SyncVote/Room" @{ Name = "Folder and collection"; SelectedCollections = @($col.Id, $box.Id); ItemTypes = @("Movie") }
    $bothList = Call $root GET "/SyncVote/Room/$($both.Body.Id)/Candidates?limit=100"
    Check "a folder and a collection together do not repeat titles" (@($bothList.Body.Items).Id.Count -eq (@($bothList.Body.Items).Id | Select-Object -Unique).Count)
    Call $root DELETE "/SyncVote/Room/$($both.Body.Id)" | Out-Null

    $all = Call $root POST "/SyncVote/Room" @{ Name = "All"; SelectedCollections = @($col.Id); ItemTypes = @("Movie") }
    $allList = Call $root GET "/SyncVote/Room/$($all.Body.Id)/Candidates?limit=1"
    Call $root DELETE "/SyncVote/Room/$($all.Body.Id)" | Out-Null
    $firstName = (Invoke-RestMethod "$Base/Items/$($five[0])" -Headers $root.Headers).Name
    $tf = Call $root POST "/SyncVote/Room" @{ Name = "Title"; SelectedCollections = @($col.Id); ItemTypes = @("Movie"); TitleFilter = "  $($firstName.ToUpper())  " }
    Check "the room keeps the (trimmed) title filter" ($tf.Status -eq 200 -and $tf.Body.TitleFilter -eq $firstName.ToUpper()) ($tf.Body | ConvertTo-Json -Compress)
    $tfList = Call $root GET "/SyncVote/Room/$($tf.Body.Id)/Candidates?limit=100"
    Check "the title filter narrows the list, ignoring case" ($tfList.Body.TotalCount -ge 1 -and $tfList.Body.TotalCount -lt $allList.Body.TotalCount -and @($tfList.Body.Items | Where-Object { $_.Name -like "*$($firstName.Split(' ')[0])*" }).Count -eq @($tfList.Body.Items).Count) "filtered $($tfList.Body.TotalCount) of $($allList.Body.TotalCount) for '$firstName'"
    Call $root DELETE "/SyncVote/Room/$($tf.Body.Id)" | Out-Null
    $none = Call $root POST "/SyncVote/Room" @{ Name = "None"; SelectedCollections = @($col.Id); ItemTypes = @("Movie"); TitleFilter = "zzzzqqqq" }
    $noneList = Call $root GET "/SyncVote/Room/$($none.Body.Id)/Candidates?limit=10"
    Check "a title nobody has gives an empty list, not an error" ($noneList.Status -eq 200 -and $noneList.Body.TotalCount -eq 0)
    Call $root DELETE "/SyncVote/Room/$($none.Body.Id)" | Out-Null

    Write-Host "== leave group"
    $lv = Call $sv POST "/SyncPlay/Leave"
    $i2 = Call $sv GET "/SyncVote/SyncPlayInfo"
    Check "after leaving, no group" (-not $i2.Body.GroupId) "got $($i2.Body.GroupId)"
    Call $root POST "/SyncPlay/Leave" | Out-Null
}
finally {
    if ($script:boxId) {
        # Deleting a collection can take a moment to settle (the library may bring it back once): delete until it is gone.
        for ($i = 0; $i -lt 8; $i++) {
            if (-not @((Invoke-RestMethod "$Base/Items?Recursive=true&IncludeItemTypes=BoxSet&SearchTerm=svtest-collection" -Headers $root.Headers).Items).Count) { break }
            Invoke-WebRequest "$Base/Items/$($script:boxId)" -Method Delete -Headers $root.Headers -SkipHttpErrorCheck | Out-Null
            Start-Sleep 2
        }
        if (-not $script:boxLibraryExisted) {
            # Jellyfin created the "Collections" library for our collection: remove what we caused.
            $lib = @(Invoke-RestMethod "$Base/Library/VirtualFolders" -Headers $root.Headers | Where-Object { "$($_.CollectionType)" -eq "boxsets" })[0]
            if ($lib) { Invoke-WebRequest "$Base/Library/VirtualFolders?name=$([uri]::EscapeDataString($lib.Name))&refreshLibrary=false" -Method Delete -Headers $root.Headers -SkipHttpErrorCheck | Out-Null }
        }
    }
    if (-not $KeepUser) { Invoke-RestMethod "$Base/Users/$($created.Id)" -Method Delete -Headers $root.Headers | Out-Null; Write-Host "temporary user removed" }
}

Write-Host ""
if ($script:fail -eq 0) { Write-Host "ALL CHECKS PASSED" -ForegroundColor Green } else { Write-Host "$($script:fail) CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
