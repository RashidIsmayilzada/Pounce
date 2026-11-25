# Smoke-checks build\Pounce-windows-x64\Pounce\Pounce.exe on a temporary home, whose name has letters outside the ANSI
# code page, and a temporary Watched Folder (never your real folders):
#   1. Pounce.exe starts its bundled Engine, which indexes the folder and answers a search through the API, with
#      ONNX Runtime's DLLs loaded from the bundle's native\onnxruntime rather than unpacked into the temp folder;
#   2. closing Pounce (Pounce.exe --quit, which exits with 0) also stops the Engine;
#   3. force-killing Pounce, once its Engine answers, still stops the Engine.
# Build it first with scripts/build-windows.sh, then, in Windows PowerShell 5.1 or PowerShell 7:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\smoke-windows.ps1
# Port 7431 must be free, so quit any running Pounce first. POUNCE_NO_DIALOGS keeps every message box away, so nothing
# waits for a click.
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

# Waits until the Engine of this Launcher has indexed the Watched Folder and finds falconry.md; returns the seconds taken.
# Only then has the Engine finished starting up: closing a Spring context waits for a startup still in progress, so an
# Engine whose Launcher dies mid-startup outlives it by most of that startup.
function Wait-SearchAnswered([System.Diagnostics.Process]$Launcher, [string]$HomeDir, [int]$Seconds) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $lastProblem = 'falconry.md not in the results'
    while ($true) {
        if ($clock.Elapsed.TotalSeconds -ge $Seconds) { throw "no search result after $Seconds s (last: $lastProblem)" }
        if ($Launcher.HasExited) { throw "Pounce.exe exited with code $($Launcher.ExitCode)" }
        try {
            if ((Search-Engine $HomeDir 'falcon glove') -contains 'falconry.md') { return $clock.Elapsed.TotalSeconds }
            $lastProblem = 'falconry.md not in the results'
        } catch {
            $lastProblem = $_.Exception.Message   # not up yet, or still indexing
        }
        Start-Sleep -Seconds 1
    }
}

function Wait-Gone([int]$Id, [int]$Seconds) {
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $Seconds) {
        if (-not (Get-Process -Id $Id -ErrorAction SilentlyContinue)) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
}

# The onnxruntime-java* folders in the temp folder Java uses (GetTempPath: %TMP%, else %TEMP%). ONNX Runtime makes one on
# every start, even with -Donnxruntime.native.path; only without it does it unpack its DLLs (about 20 MB) into it, and
# then they stay there, because a loaded DLL can't be deleted.
$TempDir = [System.IO.Path]::GetTempPath()
function Get-OrtTempFolders {
    Get-ChildItem -LiteralPath $TempDir -Directory -Filter 'onnxruntime-java*' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.FullName }
}

$launchers = New-Object System.Collections.ArrayList
# Starts Pounce.exe with these arguments and this process's environment. Process.Start, unlike Start-Process in Windows
# PowerShell 5.1, holds the process handle from the start, so ExitCode stays readable however soon it exits.
function Start-Pounce([string]$Arguments = '') {
    $info = New-Object System.Diagnostics.ProcessStartInfo $Exe, $Arguments
    $info.UseShellExecute = $false
    $process = [System.Diagnostics.Process]::Start($info)
    $null = $launchers.Add($process)   # cleaned up at the end if it is still running
    return $process
}

# The last lines of a log of this run, or a line saying it is not there.
function Show-LogTail([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        Write-Host "--- last lines of $Path"
        Get-Content -LiteralPath $Path -Tail 30 | ForEach-Object { Write-Host $_ }
    } else {
        Write-Host "(no $Path)"
    }
}

$base = $env:RUNNER_TEMP
if (-not $base) { $base = [System.IO.Path]::GetTempPath() }
$Tmp = Join-Path $base ('pounce-smoke-' + [System.Guid]::NewGuid().ToString('N').Substring(0, 8))
# A home named like a user the ANSI code page can't spell (Cyrillic, and the Azerbaijani schwa), as the Engine's
# home must work for any user name:
# java.exe would get it as "?" on its command line, so the Launcher passes it in the environment. Written as character
# codes because Windows PowerShell 5.1 reads a script without a byte order mark as ANSI.
$unicodeName = (-join [char[]](0x0420, 0x0430, 0x0448, 0x0438, 0x0434)) + ' ' + [char]0x018F + 'li'
$HomeDir = Join-Path $Tmp ('home ' + $unicodeName)
$Watched = Join-Path $Tmp 'watched'
$savedPounceHome = $env:POUNCE_HOME
$savedNoDialogs = $env:POUNCE_NO_DIALOGS
$exitCode = 0

try {
    New-Item -ItemType Directory -Force -Path $HomeDir, $Watched | Out-Null
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText((Join-Path $Watched 'falconry.md'),
        "# Falconry`nThe falcon returned to the glove after the hunt.`n", $utf8)
    # Forward slashes in single quotes: YAML takes the path literally, and Java accepts / on Windows.
    $watchedYaml = ($Watched -replace '\\', '/') -replace "'", "''"
    [System.IO.File]::WriteAllText((Join-Path $HomeDir 'pounce.yml'), "pounce:`n  watched-folders: '$watchedYaml'`n", $utf8)
    # Inherited by every process started below: this run's home, and no message box that would wait for a click.
    $env:POUNCE_HOME = $HomeDir
    $env:POUNCE_NO_DIALOGS = '1'

    # 1. Starts the Engine, which indexes the folder and answers a search.
    $ortBefore = @(Get-OrtTempFolders)
    $launcher = Start-Pounce
    $seconds = Wait-SearchAnswered $launcher $HomeDir 90
    Write-Host ('ok   bundled Engine answered a search {0:N0} s after launch' -f $seconds)

    # ONNX Runtime is loaded by now (the search embedded the query), so any DLLs it unpacked are there.
    $ortNew = @(Get-OrtTempFolders | Where-Object { $ortBefore -notcontains $_ })
    $unpacked = @($ortNew | ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -Force -File -ErrorAction SilentlyContinue })
    if ($unpacked.Count -gt 0) {
        throw ('ONNX Runtime unpacked its DLLs into the temp folder instead of loading them from native\onnxruntime: ' +
            (($unpacked | ForEach-Object { $_.FullName }) -join ', '))
    }
    Write-Host ('ok   ONNX Runtime unpacked nothing into {0} ({1} new onnxruntime-java folder(s), empty)' -f $TempDir, $ortNew.Count)

    $engine = Get-EnginePid $launcher.Id
    if (-not $engine) { throw 'no Engine child process found' }
    $workingSet = (Get-Process -Id $engine).WorkingSet64 / 1MB
    Write-Host ('ok   Engine runs as pid {0} with {1:N0} MB' -f $engine, $workingSet)

    # 2. Closing Pounce politely: a second Pounce.exe --quit asks the first to exit and waits up to 20 s for it; it exits
    #    with 0 once the first has exited.
    $quit = Start-Pounce '--quit'
    if (-not $quit.WaitForExit(25000)) { throw 'Pounce.exe --quit did not return within 25 s' }
    if ($quit.ExitCode -ne 0) { throw "Pounce.exe --quit exited with code $($quit.ExitCode): the running Pounce did not quit" }
    if (-not $launcher.WaitForExit(15000)) { throw 'Pounce.exe still running 15 s after Pounce.exe --quit returned 0' }
    if (-not (Wait-Gone $engine 15)) { throw 'Engine still running 15 s after Pounce quit' }
    Write-Host 'ok   quitting Pounce stopped the Engine'
    # An Engine that exits through System.exit removes its empty folder; one that is killed leaves it (0 bytes).
    $ortLeft = @($ortNew | Where-Object { Test-Path -LiteralPath $_ })
    if ($ortLeft.Count -gt 0) {
        Write-Host ('note: {0} empty onnxruntime-java folder(s) left in {1}: the Engine did not exit cleanly' -f $ortLeft.Count, $TempDir)
    }

    # 3. Force-killing Pounce still stops the Engine: once it has started up (see Wait-SearchAnswered); the Engine then
    #    shuts down through Spring, which may take a while for an Engine still indexing.
    $launcher = Start-Pounce
    $null = Wait-SearchAnswered $launcher $HomeDir 90
    $engine = Get-EnginePid $launcher.Id
    if (-not $engine) { throw 'no Engine child process found' }
    Stop-Process -Id $launcher.Id -Force
    if (-not (Wait-Gone $engine 30)) { throw 'orphaned Engine still running 30 s after Pounce.exe was killed' }
    Write-Host 'ok   force-killing Pounce still stopped the Engine'

    Write-Host 'PASS'
} catch {
    Write-Host "FAIL: $($_.Exception.Message)"
    Show-LogTail (Join-Path $HomeDir 'logs\launcher.log')
    Show-LogTail (Join-Path $HomeDir 'logs\engine.log')
    $exitCode = 1
} finally {
    foreach ($process in $launchers) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }
    }
    # Any Engine of this run: one started by one of its launchers (the home is not on its command line).
    $ours = @($launchers | ForEach-Object { $_.Id })
    Get-CimInstance Win32_Process -Filter "Name = 'java.exe' OR Name = 'javaw.exe'" |
        Where-Object {
            $commandLine = $_.CommandLine
            $commandLine -and @($ours | Where-Object { $commandLine -match ('pounce\.parent-pid=' + $_ + '(\D|$)') }).Count -gt 0
        } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    $env:POUNCE_HOME = $savedPounceHome
    $env:POUNCE_NO_DIALOGS = $savedNoDialogs
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
