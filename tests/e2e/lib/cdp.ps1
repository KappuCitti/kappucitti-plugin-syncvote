# Minimal Chrome DevTools Protocol client for headless UI tests.
$script:ws = $null
$script:msgId = 0
$script:console = New-Object System.Collections.Generic.List[string]

function Find-Browser {
    $candidates = @(
        $env:CHROME_PATH,
        "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
        "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
        "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser"
    ) | Where-Object { $_ -and (Test-Path $_) }
    if (-not $candidates) { throw "Chrome or Edge not found: set CHROME_PATH." }
    return @($candidates)[0]
}

function Start-Chrome($profileDir, $port = 9333) {
    Remove-Item -Recurse -Force $profileDir -ErrorAction SilentlyContinue
    $chrome = Find-Browser
    $script:chromeProc = Start-Process $chrome -PassThru -ArgumentList @("--headless=new", "--disable-gpu", "--window-size=1100,900", "--user-data-dir=$profileDir", "--remote-debugging-port=$port", "--remote-allow-origins=*", "about:blank")
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 500
        try { $targets = Invoke-RestMethod "http://127.0.0.1:$port/json" -TimeoutSec 2; break } catch { }
    }
    $page = $targets | Where-Object type -eq "page" | Select-Object -First 1
    $script:ws = [System.Net.WebSockets.ClientWebSocket]::new()
    $script:ws.ConnectAsync([Uri]$page.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()
    Send-Cdp "Runtime.enable" | Out-Null
    Send-Cdp "Page.enable" | Out-Null
}

function Receive-Cdp {
    $buffer = New-Object byte[] 1048576
    $sb = New-Object System.Text.StringBuilder
    do {
        $seg = [ArraySegment[byte]]::new($buffer)
        $r = $script:ws.ReceiveAsync($seg, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
        [void]$sb.Append([Text.Encoding]::UTF8.GetString($buffer, 0, $r.Count))
    } while (-not $r.EndOfMessage)
    return $sb.ToString() | ConvertFrom-Json
}

function Send-Cdp($method, $params = @{}) {
    $script:msgId++
    $id = $script:msgId
    $json = @{ id = $id; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $script:ws.SendAsync([ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [Threading.CancellationToken]::None).Wait()
    while ($true) {
        $m = Receive-Cdp
        if ($m.method -eq "Runtime.consoleAPICalled") {
            $text = ($m.params.args | ForEach-Object { if ($_.value) { $_.value } else { $_.description } }) -join " "
            $script:console.Add("[$($m.params.type)] $text")
        }
        elseif ($m.method -eq "Runtime.exceptionThrown") {
            $script:console.Add("[exception] $($m.params.exceptionDetails.text) $($m.params.exceptionDetails.exception.description)")
        }
        if ($m.id -eq $id) {
            if ($m.error) { throw "CDP $method failed: $($m.error.message)" }
            return $m.result
        }
    }
}

# Evaluates JS in the page; promises are awaited. Returns the value.
function Js($expr) {
    $r = Send-Cdp "Runtime.evaluate" @{ expression = $expr; awaitPromise = $true; returnByValue = $true }
    if ($r.exceptionDetails) { throw "JS error: $($r.exceptionDetails.exception.description)" }
    return $r.result.value
}

function Wait-Js($expr, $seconds = 10) {
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        try { $v = Js $expr; if ($v) { return $v } } catch { }
        Start-Sleep -Milliseconds 300
    }
    return $null
}

function Save-Shot($path) {
    $r = Send-Cdp "Page.captureScreenshot" @{ format = "png" }
    [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String($r.data))
}

function Stop-Chrome {
    try { $script:ws.Dispose() } catch { }
    if ($script:chromeProc) { Stop-Process -Id $script:chromeProc.Id -Force -ErrorAction SilentlyContinue }
    Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | Where-Object { $_.CommandLine -match 'e2e-chrome-profile' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}
