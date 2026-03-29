$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")
$projectPath = Join-Path $repoRoot "src\FlowShellBar.App\FlowShellBar.App.csproj"
$targetDirectory = Join-Path $repoRoot "src\FlowShellBar.App\bin\Debug\net8.0-windows10.0.19041.0"
$exePath = Join-Path $targetDirectory "FlowShellBar.App.exe"
$logPath = Join-Path $env:LOCALAPPDATA "FlowShell\FlowShellBar\logs\latest.log"

function Invoke-SmokeRun {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [hashtable]$EnvironmentOverrides = @{}
    )

    $processInfo = [System.Diagnostics.ProcessStartInfo]::new($exePath)
    $processInfo.WorkingDirectory = $targetDirectory
    $processInfo.UseShellExecute = $false

    foreach ($entry in $EnvironmentOverrides.GetEnumerator()) {
        $processInfo.Environment[$entry.Key] = [string]$entry.Value
    }

    $process = [System.Diagnostics.Process]::Start($processInfo)
    if ($null -eq $process) {
        throw "Failed to start smoke run '$Name'."
    }

    Start-Sleep -Seconds 4

    if ($process.HasExited) {
        throw "Smoke run '$Name' exited early with code $($process.ExitCode)."
    }

    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
    Write-Host "Smoke run '$Name' completed."
}

dotnet build $projectPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed."
}

if (-not (Test-Path $exePath)) {
    throw "Expected executable not found at $exePath."
}

Invoke-SmokeRun -Name "offline" -EnvironmentOverrides @{
    FLOWSHELL_BAR_DISABLE_FLOWTILEWM_IPC = "1"
    FLOWSHELL_BAR_DISABLE_FLOWSHELLCORE_IPC = "1"
}

Invoke-SmokeRun -Name "live-attempt"

if (Test-Path $logPath) {
    Write-Host ""
    Write-Host "Latest log tail:"
    Get-Content $logPath -Tail 20
}
