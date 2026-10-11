param([string]$Base = "http://127.0.0.1:8096")

function Login($name, $pw, $device) {
    $h = @{ "Authorization" = "MediaBrowser Client=`"SyncVoteTest`", Device=`"$device`", DeviceId=`"$device-id`", Version=`"1.0`"" }
    $r = Invoke-RestMethod "$Base/Users/AuthenticateByName" -Method Post -Headers $h -ContentType "application/json" -Body (@{ Username = $name; Pw = $pw } | ConvertTo-Json)
    return @{ Token = $r.AccessToken; UserId = $r.User.Id; Name = $name; Device = $device; IsAdmin = $r.User.Policy.IsAdministrator
        Headers = @{ "Authorization" = "MediaBrowser Client=`"SyncVoteTest`", Device=`"$device`", DeviceId=`"$device-id`", Version=`"1.0`", Token=`"$($r.AccessToken)`"" } }
}

# Calls the API and returns @{ Status; Body } without throwing on 4xx/5xx.
function Call($u, $method, $path, $body = $null) {
    $p = @{ Uri = "$Base$path"; Method = $method; Headers = $u.Headers; SkipHttpErrorCheck = $true; StatusCodeVariable = "sc" }
    if ($null -ne $body) { $p.Body = ($body | ConvertTo-Json -Depth 6); $p.ContentType = "application/json" }
    $r = Invoke-RestMethod @p
    return @{ Status = $sc; Body = $r }
}
