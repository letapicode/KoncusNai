[CmdletBinding()]
param(
  [switch]$RunHotkey,
  [switch]$RunAudio,
  [switch]$RunInsertionMatrix,
  [switch]$ContinueOnError,
  [int]$PerSpikeTimeoutSeconds = 240,
  [string]$OutputDirectory = "artifacts/spikes",
  [string]$ReportPath = "docs/release/milestone-0-spike-report.md",
  [int]$AudioDurationSeconds = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
  param([Parameter(Mandatory = $true)][string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-RepoRoot {
  $scriptsRoot = $script:PSScriptRoot
  if ([string]::IsNullOrWhiteSpace($scriptsRoot)) {
    throw "Failed to resolve scripts directory."
  }

  return [IO.Path]::GetFullPath((Join-Path -Path $scriptsRoot -ChildPath ".."))
}

function Ensure-Dotnet {
  if ($null -eq (Get-Command -Name "dotnet" -ErrorAction SilentlyContinue)) {
    throw "dotnet is required to run spikes."
  }
}

$repoRoot = Resolve-RepoRoot
Set-Location $repoRoot
Ensure-Dotnet

function Invoke-SpikeRunner {
  param(
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][int]$TimeoutSeconds
  )

  $runArgs = @(
    "run",
    "--project", "tools/DictateAnywhere.Spikes/DictateAnywhere.Spikes.csproj",
    "-c", "Release",
    "--no-build",
    "--"
  ) + $Arguments

  $stdoutPath = [System.IO.Path]::GetTempFileName()
  $stderrPath = [System.IO.Path]::GetTempFileName()

  try {
    $process = Start-Process -FilePath "dotnet" -ArgumentList $runArgs -WorkingDirectory $RepoRoot -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
    $completed = $process.WaitForExit([Math]::Max(1, $TimeoutSeconds) * 1000)
    if (-not $completed) {
      try {
        $process.Kill($true)
      }
      catch {
      }

      $partialStdout = if (Test-Path -Path $stdoutPath) { Get-Content -Path $stdoutPath } else { @() }
      $partialStderr = if (Test-Path -Path $stderrPath) { Get-Content -Path $stderrPath } else { @() }
      $partialOutput = @($partialStdout + $partialStderr)
      $details = if ($partialOutput.Count -eq 0) { "No output captured before timeout." } else { $partialOutput -join [Environment]::NewLine }

      return [pscustomobject]@{
        spikeName = $Arguments[0]
        success = $false
        summary = "Spike runner timed out."
        duration = $null
        artifactPath = $null
        details = "Timed out after $TimeoutSeconds seconds. $details"
        exitCode = -2
      }
    }

    $exitCode = $process.ExitCode
    $stdout = if (Test-Path -Path $stdoutPath) { Get-Content -Path $stdoutPath } else { @() }
    $stderr = if (Test-Path -Path $stderrPath) { Get-Content -Path $stderrPath } else { @() }
    $output = @($stdout + $stderr)
  }
  finally {
    Remove-Item -Path $stdoutPath -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $stderrPath -Force -ErrorAction SilentlyContinue
  }

  $jsonLine = ($output | Where-Object { $_ -is [string] -and $_.TrimStart().StartsWith("{") } | Select-Object -Last 1)
  if ([string]::IsNullOrWhiteSpace($jsonLine)) {
    return [pscustomobject]@{
      spikeName = $Arguments[0]
      success = $false
      summary = "Spike runner did not emit JSON output."
      duration = $null
      artifactPath = $null
      details = ($output -join [Environment]::NewLine)
      exitCode = $exitCode
    }
  }

  $result = $jsonLine | ConvertFrom-Json
  $result | Add-Member -NotePropertyName exitCode -NotePropertyValue $exitCode -Force
  return $result
}

function New-NotRunResult {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$Reason
  )

  return [pscustomobject]@{
    spikeName = $Name
    success = $false
    summary = "Not run"
    duration = $null
    artifactPath = $null
    details = $Reason
    exitCode = -1
  }
}

function Start-TargetApp {
  param([Parameter(Mandatory = $true)][string]$Target)

  if ($Target -eq "notepad") {
    return Start-Process -FilePath "notepad.exe" -PassThru
  }

  if ($Target -eq "vscode") {
    $codeCmd = Get-Command -Name "code" -ErrorAction SilentlyContinue
    if ($null -eq $codeCmd) {
      throw "VS Code command 'code' not found."
    }

    return Start-Process -FilePath $codeCmd.Source -ArgumentList "-n" -PassThru
  }

  if ($Target -eq "chrome") {
    $chromeCmd = Get-Command -Name "chrome" -ErrorAction SilentlyContinue
    if ($null -ne $chromeCmd) {
      return Start-Process -FilePath $chromeCmd.Source -ArgumentList "--new-window", "about:blank" -PassThru
    }

    $candidatePaths = @(
      (Join-Path -Path $env:ProgramFiles -ChildPath "Google\\Chrome\\Application\\chrome.exe"),
      (Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath "Google\\Chrome\\Application\\chrome.exe"),
      (Join-Path -Path $env:LOCALAPPDATA -ChildPath "Google\\Chrome\\Application\\chrome.exe")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidatePaths) {
      if (Test-Path -Path $candidate) {
        return Start-Process -FilePath $candidate -ArgumentList "--new-window", "about:blank" -PassThru
      }
    }

    throw "Chrome executable not found in PATH or standard install locations."
  }

  throw "Unsupported target app '$Target'."
}

function Stop-TargetApp {
  param([Parameter(Mandatory = $false)][System.Diagnostics.Process]$Process)

  if ($null -eq $Process) {
    return
  }

  try {
    if (-not $Process.HasExited) {
      $Process.CloseMainWindow() | Out-Null
      Start-Sleep -Milliseconds 400
      if (-not $Process.HasExited) {
        $Process.Kill($true)
      }
    }
  }
  catch {
  }
}

function Format-DurationMs {
  param([Parameter(Mandatory = $false)]$DurationValue)

  if ($null -eq $DurationValue) {
    return "n/a"
  }

  try {
    $duration = [TimeSpan]::Parse([string]$DurationValue, [System.Globalization.CultureInfo]::InvariantCulture)
    return [math]::Round($duration.TotalMilliseconds, 0).ToString([System.Globalization.CultureInfo]::InvariantCulture)
  }
  catch {
    return "n/a"
  }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

Write-Step "Building spike runner"
& dotnet build tools/DictateAnywhere.Spikes/DictateAnywhere.Spikes.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) {
  throw "Spike runner build failed."
}

$results = New-Object System.Collections.Generic.List[object]

if ($RunHotkey) {
  Write-Step "Running hotkey spike (interactive)"
  $hotkeyResult = Invoke-SpikeRunner -RepoRoot $repoRoot -TimeoutSeconds $PerSpikeTimeoutSeconds -Arguments @(
    "hotkey",
    "--modifiers", "Windows,Alt",
    "--virtual-key", "32",
    "--timeout-seconds", "20"
  )
  $results.Add($hotkeyResult)
  if (-not $ContinueOnError -and -not [bool]$hotkeyResult.success) {
    throw "Hotkey spike failed."
  }
}
else {
  $results.Add((New-NotRunResult -Name "hotkey" -Reason "Skipped. Pass -RunHotkey to execute the interactive hotkey spike."))
}

$audioWavPath = Join-Path -Path $OutputDirectory -ChildPath "audio-capture.wav"
if ($RunAudio) {
  Write-Step "Running WASAPI audio spike"
  $audioResult = Invoke-SpikeRunner -RepoRoot $repoRoot -TimeoutSeconds $PerSpikeTimeoutSeconds -Arguments @(
    "audio",
    "--duration-seconds", $AudioDurationSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    "--output", $audioWavPath
  )
  $results.Add($audioResult)
  if (-not $ContinueOnError -and -not [bool]$audioResult.success) {
    throw "Audio spike failed."
  }
}
else {
  $results.Add((New-NotRunResult -Name "audio" -Reason "Skipped. Pass -RunAudio to execute WASAPI capture spike."))
}

if ($RunInsertionMatrix) {
  Write-Step "Running insertion spike matrix (Notepad/VS Code/Chrome clipboard + fallback typing)"

  foreach ($target in @("notepad", "vscode", "chrome")) {
    $process = $null
    try {
      $process = Start-TargetApp -Target $target
      Start-Sleep -Seconds 3
      $targetResult = Invoke-SpikeRunner -RepoRoot $repoRoot -TimeoutSeconds $PerSpikeTimeoutSeconds -Arguments @(
        "insertion",
        "--method", "clipboard",
        "--require-method", "clipboard",
        "--restore-clipboard", "false",
        "--text", "Dictate Anywhere clipboard spike ($target)"
      )
      $targetResult.spikeName = "insertion-clipboard-$target"
      $results.Add($targetResult)
      if (-not $ContinueOnError -and -not [bool]$targetResult.success) {
        throw "Insertion clipboard spike failed for target '$target'."
      }
    }
    catch {
      $results.Add((New-NotRunResult -Name "insertion-clipboard-$target" -Reason $_.Exception.Message))
      if (-not $ContinueOnError) {
        throw
      }
    }
    finally {
      Stop-TargetApp -Process $process
    }
  }

  $typingProcess = $null
  try {
    $typingProcess = Start-TargetApp -Target "notepad"
    Start-Sleep -Seconds 3
    $typingResult = Invoke-SpikeRunner -RepoRoot $repoRoot -TimeoutSeconds $PerSpikeTimeoutSeconds -Arguments @(
      "insertion",
      "--method", "typing",
      "--require-method", "typing",
      "--restore-clipboard", "false",
      "--text", "Dictate Anywhere typing fallback spike"
    )
    $typingResult.spikeName = "insertion-typing-fallback"
    $results.Add($typingResult)
    if (-not $ContinueOnError -and -not [bool]$typingResult.success) {
      throw "Typing fallback spike failed."
    }
  }
  catch {
    $results.Add((New-NotRunResult -Name "insertion-typing-fallback" -Reason $_.Exception.Message))
    if (-not $ContinueOnError) {
      throw
    }
  }
  finally {
    Stop-TargetApp -Process $typingProcess
  }
}
else {
  $results.Add((New-NotRunResult -Name "insertion-clipboard-notepad" -Reason "Skipped. Pass -RunInsertionMatrix to run app insertion spikes."))
  $results.Add((New-NotRunResult -Name "insertion-clipboard-vscode" -Reason "Skipped. Pass -RunInsertionMatrix to run app insertion spikes."))
  $results.Add((New-NotRunResult -Name "insertion-clipboard-chrome" -Reason "Skipped. Pass -RunInsertionMatrix to run app insertion spikes."))
  $results.Add((New-NotRunResult -Name "insertion-typing-fallback" -Reason "Skipped. Pass -RunInsertionMatrix to run app insertion spikes."))
}

$jsonPath = Join-Path -Path $OutputDirectory -ChildPath "milestone-0-spike-results.json"
$results | ConvertTo-Json -Depth 5 | Set-Content -NoNewline $jsonPath

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Milestone 0 Spike Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ssK')")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Result artifact: $jsonPath")
$reportLines.Add("")
$reportLines.Add("## Results")
$reportLines.Add("")
$reportLines.Add("| Spike | Status | Duration (ms) | Artifact | Notes |")
$reportLines.Add("|---|---|---:|---|---|")

foreach ($result in $results) {
  $status = if ([bool]$result.success) { "PASS" } else { "FAIL" }
  $durationMs = Format-DurationMs -DurationValue $result.duration
  $artifact = if ([string]::IsNullOrWhiteSpace([string]$result.artifactPath)) { "n/a" } else { [string]$result.artifactPath }
  $notes = if ([string]::IsNullOrWhiteSpace([string]$result.details)) { [string]$result.summary } else { [string]$result.details }
  $safeNotes = $notes.Replace("|", "/")
  $reportLines.Add("| $([string]$result.spikeName) | $status | $durationMs | $artifact | $safeNotes |")
}

$blockers = @($results | Where-Object { -not [bool]$_.success })
$reportLines.Add("")
$reportLines.Add("## Known Blockers")
$reportLines.Add("")
if ($blockers.Count -eq 0) {
  $reportLines.Add("- None.")
}
else {
  foreach ($blocker in $blockers) {
    $reportLines.Add("- $([string]$blocker.spikeName): $([string]$blocker.details)")
  }
}

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
  New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}
$reportLines | Set-Content -NoNewline:$false $ReportPath

Write-Step "Milestone 0 spike run complete"
Write-Host "Report: $ReportPath" -ForegroundColor Green
Write-Host "JSON:   $jsonPath" -ForegroundColor Green

$failedResults = @($results | Where-Object { -not [bool]$_.success })
if ($failedResults.Count -gt 0 -and -not $ContinueOnError) {
  exit 1
}

exit 0
