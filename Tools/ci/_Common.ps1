# _Common.ps1 — shared helpers for the Tools/ci PowerShell harness.
# Dot-source this from the entrypoints:  . "$PSScriptRoot/_Common.ps1"
# Neutral, reusable, KHR-agnostic. Parameterized by project path + Unity path. No internal references.

$ErrorActionPreference = 'Stop'

# Repo root = two levels above Tools/ci (the Unity project lives at the repo root).
function Resolve-ProjectPath {
    param([string]$ProjectPath)
    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        $ProjectPath = Join-Path $PSScriptRoot '..' | Join-Path -ChildPath '..'
    }
    return (Resolve-Path -LiteralPath $ProjectPath).Path
}

function Get-ProjectUnityVersion {
    param([Parameter(Mandatory)][string]$ProjectPath)
    $pv = Join-Path $ProjectPath 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $pv)) { throw "ProjectVersion.txt not found at '$pv'." }
    $line = Get-Content -LiteralPath $pv | Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
    if (-not $line) { throw "m_EditorVersion not found in '$pv'." }
    return ($line -replace '^m_EditorVersion:\s*', '').Trim()
}

# Resolve the Unity editor binary: honor $env:UNITY_PATH, else probe the Hub install for the pinned version.
function Find-UnityExe {
    param([Parameter(Mandatory)][string]$ProjectPath)
    $version = Get-ProjectUnityVersion -ProjectPath $ProjectPath

    if ($env:UNITY_PATH -and (Test-Path -LiteralPath $env:UNITY_PATH)) {
        return (Resolve-Path -LiteralPath $env:UNITY_PATH).Path
    }

    $candidates = @()
    if ($env:OS -eq 'Windows_NT') {
        $candidates += "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
    } elseif ($PSVersionTable.PSVersion.Major -ge 6 -and $IsMacOS) {
        $candidates += "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity"
    } else {
        $candidates += "$HOME/Unity/Hub/Editor/$version/Editor/Unity"
        $candidates += "/opt/unity/editors/$version/Editor/Unity"
    }
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path } }

    throw "Unity '$version' not found. Set `$env:UNITY_PATH to the editor binary (the project pins $version)."
}

# Refuse to launch a second Unity on the same project (avoids the Temp/UnityLockfile deadlock).
function Assert-NoLock {
    param([Parameter(Mandatory)][string]$ProjectPath)
    $lock = Join-Path $ProjectPath 'Temp/UnityLockfile'
    if (Test-Path -LiteralPath $lock) {
        throw "Unity lockfile present ('$lock'). Another Unity is using this project (one Unity per project). Close it and retry."
    }
}

# True when the compile log is CLEAN (no compiler errors).
function Test-CompileLogClean {
    param([Parameter(Mandatory)][string]$LogFile)
    if (-not (Test-Path -LiteralPath $LogFile)) { return $false }
    $hit = Select-String -LiteralPath $LogFile -SimpleMatch -Quiet -Pattern @(
        'error CS', 'Compilation failed', 'Scripts have compiler errors'
    )
    return -not $hit
}

# Detached launch + bounded poll + hard-timeout kill. Returns the Unity exit code, or 124 on timeout.
# When -ResultsFile is given, the poll completes as soon as that file appears (then waits briefly for self-quit).
function Invoke-UnityBounded {
    param(
        [Parameter(Mandatory)][string]$UnityExe,
        [Parameter(Mandatory)][string[]]$UnityArgs,
        [Parameter(Mandatory)][string]$LogFile,
        [Parameter(Mandatory)][string]$LogDir,
        [Parameter(Mandatory)][string]$RunName,
        [string]$ResultsFile,
        [int]$TimeoutMinutes = 60,
        [int]$PollSeconds = 10
    )
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    if (Test-Path -LiteralPath $LogFile) { Remove-Item -LiteralPath $LogFile -Force }
    if ($ResultsFile -and (Test-Path -LiteralPath $ResultsFile)) { Remove-Item -LiteralPath $ResultsFile -Force }
    $pidFile = Join-Path $LogDir "$RunName.pid"
    $timeoutMarker = Join-Path $LogDir "$RunName.TIMEOUT"
    if (Test-Path -LiteralPath $timeoutMarker) { Remove-Item -LiteralPath $timeoutMarker -Force }

    $proc = Start-Process -FilePath $UnityExe -ArgumentList $UnityArgs -PassThru
    Set-Content -LiteralPath $pidFile -Value $proc.Id
    Write-Host "[ci] launched Unity '$RunName' pid=$($proc.Id) timeout=${TimeoutMinutes}m"

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ($true) {
        Start-Sleep -Seconds $PollSeconds
        if ($proc.HasExited) { break }
        if ($ResultsFile -and (Test-Path -LiteralPath $ResultsFile)) {
            for ($i = 0; $i -lt 12 -and -not $proc.HasExited; $i++) { Start-Sleep -Seconds 5 } # grace for self-quit
            break
        }
        if ((Get-Date) -gt $deadline) {
            Write-Warning "[ci] TIMEOUT after ${TimeoutMinutes}m; killing '$RunName' pid=$($proc.Id)."
            try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
            Set-Content -LiteralPath $timeoutMarker -Value (Get-Date -Format o)
            return 124
        }
    }
    if (-not $proc.HasExited) { try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { } }
    $code = if ($proc.HasExited) { $proc.ExitCode } else { 0 }
    Write-Host "[ci] Unity '$RunName' exited code=$code"
    return [int]$code
}

# Run a compile-only batchmode pass (-quit). Returns $true on a clean compile.
function Invoke-CompileOnly {
    param(
        [Parameter(Mandatory)][string]$UnityExe,
        [Parameter(Mandatory)][string]$ProjectPath,
        [Parameter(Mandatory)][string]$LogDir,
        [string]$RunName = 'compile',
        [int]$TimeoutMinutes = 60
    )
    $log = Join-Path $LogDir "$RunName.log"
    $unityArgs = @('-batchmode', '-quit', '-nographics', '-projectPath', $ProjectPath, '-logFile', $log)
    $code = Invoke-UnityBounded -UnityExe $UnityExe -UnityArgs $unityArgs -LogFile $log -LogDir $LogDir -RunName $RunName -TimeoutMinutes $TimeoutMinutes
    $clean = Test-CompileLogClean -LogFile $log
    if ($code -ne 0) { Write-Warning "[ci] compile pass '$RunName' exit code $code (see $log)." }
    if (-not $clean) { Write-Warning "[ci] compiler errors detected in $log." }
    return ($code -eq 0 -and $clean)
}
