# Smoke-checks build\Pounce-windows-x64\Pounce\Pounce.exe on a temporary home and Watched Folder (never your real folders):
#   1. Pounce.exe starts its bundled Engine, which indexes the folder and answers a search through the API;
#   2. closing Pounce (Pounce.exe --quit) also stops the Engine;
#   3. force-killing Pounce still stops the Engine.
# Build it first with scripts/build-windows.sh, then, in Windows PowerShell 5.1 or PowerShell 7:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\smoke-windows.ps1
# Port 7431 must be free, so quit any running Pounce first.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$Root = Split-Path -Parent $PSScriptRoot
$Exe = Join-Path $Root 'build\Pounce-windows-x64\Pounce\Pounce.exe'
$Api = 'http://127.0.0.1:7431'

if (-not (Test-Path -LiteralPath $Exe)) {
    Write-Host 'build it first: scripts/build-windows.sh'
    exit 2
}

# True when something accepts connections on the Engine's port.
function Test-EnginePort {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $attempt = $client.BeginConnect('127.0.0.1', 7431, $null, $null)
        if (-not $attempt.AsyncWaitHandle.WaitOne(1000)) { return $false }
        $client.EndConnect($attempt)
        return $true
    } catch {
        return $false
    } finally {
        $client.Close()
    }
}

if (Test-EnginePort) {
    Write-Host 'Something already answers on 127.0.0.1:7431; stop it first so the check tests the bundled Engine.'
    exit 2
}

# One HTTP request to the Engine; returns the response body. Throws on connection errors and non-2xx answers.
function Send-EngineRequest([string]$Method, [string]$Path, [string]$Token, [string]$Json) {
    $request = [System.Net.WebRequest]::Create($Api + $Path)   # an HttpWebRequest for http: URLs
    $request.Method = $Method
    $request.Proxy = $null
    $request.Timeout = 15000
    $request.ReadWriteTimeout = 15000
    if ($Token) {
        $request.Headers['Authorization'] = 'Bearer ' + $Token
        $request.Accept = 'text/event-stream'
    }
    if ($Json) {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Json)
        $request.ContentType = 'application/json'
        $request.ContentLength = $bytes.Length
        $body = $request.GetRequestStream()
        try { $body.Write($bytes, 0, $bytes.Length) } finally { $body.Close() }
    }
    $response = $request.GetResponse()
    try {
        $reader = New-Object System.IO.StreamReader($response.GetResponseStream(), [System.Text.Encoding]::UTF8)
        return $reader.ReadToEnd()
    } finally {
        $response.Close()
    }
}

# The file names in the `final` event of a search, as the Launcher would see them.
function Search-Engine([string]$HomeDir, [string]$Query) {
    $secret = [System.IO.File]::ReadAllText((Join-Path $HomeDir 'bootstrap-secret')).Trim()
    $login = Send-EngineRequest 'POST' '/auth/token' '' (ConvertTo-Json @{ secret = $secret })
    $token = (ConvertFrom-Json $login).token
    $stream = Send-EngineRequest 'GET' ('/search?q=' + [System.Uri]::EscapeDataString($Query)) $token ''
    $eventName = ''
    $names = @()
    foreach ($line in ($stream -split "`r?`n")) {
        if ($line.StartsWith('event:')) {
            $eventName = $line.Substring(6).Trim()
        } elseif ($line.StartsWith('data:') -and $eventName -eq 'final') {
            foreach ($result in (ConvertFrom-Json $line.Substring(5))) { $names += [string]$result.name }
        }
    }
    return $names
}

# The process id of the Engine started by the Launcher with this process id, or 0.
function Get-EnginePid([int]$LauncherPid) {
    $pattern = 'pounce\.parent-pid=' + $LauncherPid + '(\D|$)'
    $engine = Get-CimInstance Win32_Process -Filter "Name = 'java.exe' OR Name = 'javaw.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine -match $pattern } |
        Select-Object -First 1
    if ($engine) { return [int]$engine.ProcessId }
    return 0
}

function Wait-EnginePid([System.Diagnostics.Process]$Launcher, [int]$Seconds) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        $id = Get-EnginePid $Launcher.Id
        if ($id) { return $id }
        if ($Launcher.HasExited) { throw "Pounce.exe exited with code $($Launcher.ExitCode) before starting the Engine" }
        Start-Sleep -Milliseconds 500
    }
    throw "no Engine child process found within $Seconds s"
}

function Wait-Gone([int]$Id, [int]$Seconds) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        if (-not (Get-Process -Id $Id -ErrorAction SilentlyContinue)) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

$launchers = New-Object System.Collections.ArrayList
function Start-Pounce {
    $process = Start-Process -FilePath $Exe -PassThru
    $null = $process.Handle   # keeps the handle, so ExitCode is still readable after it exits
    $null = $launchers.Add($process)
    return $process
}

$base = $env:RUNNER_TEMP
if (-not $base) { $base = [System.IO.Path]::GetTempPath() }
$Tmp = Join-Path $base ('pounce-smoke-' + [System.Guid]::NewGuid().ToString('N').Substring(0, 8))
$HomeDir = Join-Path $Tmp 'home'
$Watched = Join-Path $Tmp 'watched'
$savedPounceHome = $env:POUNCE_HOME
$exitCode = 0

try {
    New-Item -ItemType Directory -Force -Path $HomeDir, $Watched | Out-Null
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $Watched 'falconry.md'),
        "# Falconry`nThe falcon returned to the glove after the hunt.`n", $utf8)
    # Forward slashes in single quotes: YAML takes the path literally, and Java accepts / on Windows.
    $watchedYaml = ($Watched -replace '\\', '/') -replace "'", "''"
    [System.IO.File]::WriteAllText((Join-Path $HomeDir 'pounce.yml'), "pounce:`n  watched-folders: '$watchedYaml'`n", $utf8)
    $env:POUNCE_HOME = $HomeDir

    # 1. Starts the Engine, which indexes the folder and answers a search.
    $launcher = Start-Pounce
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $found = $false
    $lastProblem = 'falconry.md not in the results'
    while (-not $found) {
        if ($clock.Elapsed.TotalSeconds -ge 90) { throw "no search result after 90 s (last: $lastProblem)" }
        if ($launcher.HasExited) { throw "Pounce.exe exited with code $($launcher.ExitCode)" }
        try {
            $found = (Search-Engine $HomeDir 'falcon glove') -contains 'falconry.md'
            $lastProblem = 'falconry.md not in the results'
        } catch {
            $lastProblem = $_.Exception.Message   # not up yet, or still indexing
        }
        if (-not $found) { Start-Sleep -Seconds 1 }
    }
    Write-Host ('ok   bundled Engine answered a search {0:N0} s after launch' -f $clock.Elapsed.TotalSeconds)

    $engine = Get-EnginePid $launcher.Id
    if (-not $engine) { throw 'no Engine child process found' }
    $workingSet = (Get-Process -Id $engine).WorkingSet64 / 1MB
    Write-Host ('ok   Engine runs as pid {0} with {1:N0} MB' -f $engine, $workingSet)

    # 2. Closing Pounce politely: a second Pounce.exe --quit asks the first to exit.
    $quit = Start-Process -FilePath $Exe -ArgumentList '--quit' -PassThru
    $null = $launchers.Add($quit)   # cleaned up like the others if it hangs
    if (-not $quit.WaitForExit(15000)) { throw 'Pounce.exe --quit did not return within 15 s' }
    if (-not $launcher.WaitForExit(15000)) { throw 'Pounce.exe still running 15 s after Pounce.exe --quit' }
    if (-not (Wait-Gone $engine 15)) { throw 'Engine still running 15 s after Pounce quit' }
    Write-Host 'ok   quitting Pounce stopped the Engine'

    # 3. Force-killing Pounce still stops the Engine.
    $launcher = Start-Pounce
    $engine = Wait-EnginePid $launcher 90
    Start-Sleep -Seconds 3
    Stop-Process -Id $launcher.Id -Force
    if (-not (Wait-Gone $engine 15)) { throw 'orphaned Engine still running 15 s after Pounce.exe was killed' }
    Write-Host 'ok   force-killing Pounce still stopped the Engine'

    Write-Host 'PASS'
} catch {
    Write-Host "FAIL: $($_.Exception.Message)"
    $log = Join-Path $HomeDir 'logs\engine.log'
    if (Test-Path -LiteralPath $log) {
        Write-Host "--- last lines of $log"
        Get-Content -LiteralPath $log -Tail 20 | ForEach-Object { Write-Host $_ }
    } else {
        Write-Host "(no $log)"
    }
    $exitCode = 1
} finally {
    foreach ($process in $launchers) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
    # Any Engine of this run: started by one of its launchers, or using its home.
    $ours = @($launchers | ForEach-Object { $_.Id })
    Get-CimInstance Win32_Process -Filter "Name = 'java.exe' OR Name = 'javaw.exe'" |
        Where-Object {
            $commandLine = $_.CommandLine
            $commandLine -and ($commandLine.Contains($HomeDir) -or
                @($ours | Where-Object { $commandLine -match ('pounce\.parent-pid=' + $_ + '(\D|$)') }).Count -gt 0)
        } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    $env:POUNCE_HOME = $savedPounceHome
    for ($try = 0; (Test-Path -LiteralPath $Tmp) -and $try -lt 10; $try++) {
        Remove-Item -LiteralPath $Tmp -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $Tmp) { Start-Sleep -Milliseconds 500 }
    }
    if (Test-Path -LiteralPath $Tmp) { Write-Host "warning: could not remove $Tmp" }
}

if (Test-Path -LiteralPath (Join-Path $env:SystemRoot 'System32\msvcp140_1.dll')) {
    Write-Host ('note: this machine has the Visual C++ runtime (msvcp140_1.dll) in System32, ' +
        'so this check cannot tell whether a clean Windows install would load ONNX Runtime')
}
exit $exitCode
