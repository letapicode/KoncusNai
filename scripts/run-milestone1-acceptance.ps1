[CmdletBinding()]
param(
  [switch]$ContinueOnError,
  [string]$OutputDirectory = "artifacts/milestone1",
  [string]$ReportPath = "docs/release/milestone-1-acceptance-report.md",
  [int]$PerSpikeTimeoutSeconds = 240
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
  param([Parameter(Mandatory = $true)][string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Condition {
  param(
    [Parameter(Mandatory = $true)][bool]$Condition,
    [Parameter(Mandatory = $true)][string]$Message
  )

  if (-not $Condition) {
    throw $Message
  }
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
    throw "dotnet is required to run Milestone 1 acceptance."
  }
}

function Ensure-WinFormsLoaded {
  if (-not ("System.Windows.Forms.SendKeys" -as [type])) {
    Add-Type -AssemblyName System.Windows.Forms
  }
}

function Resolve-ShortcutTarget {
  param(
    [Parameter(Mandatory = $true)][string]$ShortcutPath
  )

  if (-not (Test-Path -Path $ShortcutPath)) {
    return $null
  }

  try {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $targetPath = [string]$shortcut.TargetPath
    if ([string]::IsNullOrWhiteSpace($targetPath)) {
      return $null
    }

    if (Test-Path -Path $targetPath) {
      return $targetPath
    }

    return $null
  }
  catch {
    return $null
  }
}

function Invoke-SendKeys {
  param(
    [Parameter(Mandatory = $true)][string]$Keys,
    [int]$StabilizeDelayMs = 200
  )

  Ensure-WinFormsLoaded
  [System.Windows.Forms.SendKeys]::SendWait($Keys)
  if ($StabilizeDelayMs -gt 0) {
    Start-Sleep -Milliseconds $StabilizeDelayMs
  }
}

function Resolve-ExecutablePath {
  param(
    [Parameter(Mandatory = $true)][string]$Target
  )

  switch ($Target) {
    "chrome" {
      $cmd = Get-Command -Name "chrome" -ErrorAction SilentlyContinue
      if ($null -ne $cmd) {
        return $cmd.Source
      }

      $paths = @(
        (Join-Path -Path $env:ProgramFiles -ChildPath "Google\\Chrome\\Application\\chrome.exe"),
        (Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath "Google\\Chrome\\Application\\chrome.exe"),
        (Join-Path -Path $env:LOCALAPPDATA -ChildPath "Google\\Chrome\\Application\\chrome.exe")
      ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

      foreach ($path in $paths) {
        if (Test-Path -Path $path) {
          return $path
        }
      }

      return $null
    }

    "vscode" {
      $cmd = Get-Command -Name "code" -ErrorAction SilentlyContinue
      if ($null -ne $cmd) {
        return $cmd.Source
      }

      return $null
    }

    "word" {
      $cmd = Get-Command -Name "WINWORD.EXE" -ErrorAction SilentlyContinue
      if ($null -ne $cmd) {
        return $cmd.Source
      }

      $paths = @(
        (Join-Path -Path $env:ProgramFiles -ChildPath "Microsoft Office\\root\\Office16\\WINWORD.EXE"),
        (Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath "Microsoft Office\\root\\Office16\\WINWORD.EXE"),
        (Join-Path -Path $env:ProgramFiles -ChildPath "Microsoft Office\\root\\Office15\\WINWORD.EXE"),
        (Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath "Microsoft Office\\root\\Office15\\WINWORD.EXE"),
        (Join-Path -Path $env:ProgramFiles -ChildPath "Microsoft Office\\Office15\\WINWORD.EXE"),
        (Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath "Microsoft Office\\Office15\\WINWORD.EXE")
      ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

      foreach ($path in $paths) {
        if (Test-Path -Path $path) {
          return $path
        }
      }

      $startMenuOffice2013 = Join-Path -Path $env:ProgramData -ChildPath "Microsoft\\Windows\\Start Menu\\Programs\\Microsoft Office 2013"
      if (Test-Path -Path $startMenuOffice2013) {
        $wordShortcuts = Get-ChildItem -Path $startMenuOffice2013 -Filter "*.lnk" -ErrorAction SilentlyContinue |
          Where-Object { $_.Name -match "Word|WINWORD" }

        foreach ($shortcut in $wordShortcuts) {
          $targetPath = Resolve-ShortcutTarget -ShortcutPath $shortcut.FullName
          if (-not [string]::IsNullOrWhiteSpace($targetPath)) {
            return $targetPath
          }
        }
      }

      return $null
    }

    "slack" {
      $cmd = Get-Command -Name "slack" -ErrorAction SilentlyContinue
      if ($null -ne $cmd) {
        return $cmd.Source
      }

      $paths = @(
        (Join-Path -Path $env:LOCALAPPDATA -ChildPath "slack\\slack.exe"),
        (Join-Path -Path $env:ProgramFiles -ChildPath "Slack\\slack.exe"),
        (Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath "Slack\\slack.exe")
      ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

      foreach ($path in $paths) {
        if (Test-Path -Path $path) {
          return $path
        }
      }

      return $null
    }

    default {
      throw "Unsupported target '$Target'."
    }
  }
}

function Start-TargetApp {
  param(
    [Parameter(Mandatory = $true)][string]$Target
  )

  $path = Resolve-ExecutablePath -Target $Target
  if ([string]::IsNullOrWhiteSpace($path)) {
    return $null
  }

  $arguments = @()
  switch ($Target) {
    "chrome" { $arguments = @("--new-window", "about:blank") }
    "vscode" { $arguments = @("-n") }
    default { }
  }

  if ($arguments.Count -gt 0) {
    return Start-Process -FilePath $path -ArgumentList $arguments -PassThru
  }

  return Start-Process -FilePath $path -PassThru
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

      return [pscustomobject]@{
        spikeName = $Arguments[0]
        success = $false
        summary = "Spike runner timed out."
        duration = $null
        artifactPath = $null
        details = "Timed out after $TimeoutSeconds seconds."
      }
    }

    $stdout = if (Test-Path -Path $stdoutPath) { Get-Content -Path $stdoutPath } else { @() }
    $stderr = if (Test-Path -Path $stderrPath) { Get-Content -Path $stderrPath } else { @() }
    $output = @($stdout + $stderr)
    $jsonLine = ($output | Where-Object { $_ -is [string] -and $_.TrimStart().StartsWith("{") } | Select-Object -Last 1)

    if ([string]::IsNullOrWhiteSpace($jsonLine)) {
      return [pscustomobject]@{
        spikeName = $Arguments[0]
        success = $false
        summary = "Spike runner did not emit JSON output."
        duration = $null
        artifactPath = $null
        details = ($output -join [Environment]::NewLine)
      }
    }

    return $jsonLine | ConvertFrom-Json
  }
  finally {
    Remove-Item -Path $stdoutPath -Force -ErrorAction SilentlyContinue
    Remove-Item -Path $stderrPath -Force -ErrorAction SilentlyContinue
  }
}

function New-EvaluationRow {
  param(
    [Parameter(Mandatory = $true)][string]$Criterion,
    [Parameter(Mandatory = $true)][string]$Status,
    [Parameter(Mandatory = $true)][string]$Evidence
  )

  return [pscustomobject]@{
    criterion = $Criterion
    status = $Status
    evidence = $Evidence
  }
}

function Evaluate-SpikeCriterion {
  param(
    [Parameter(Mandatory = $true)][string]$Criterion,
    [Parameter(Mandatory = $false)]$SpikeResult
  )

  if ($null -eq $SpikeResult) {
    return New-EvaluationRow -Criterion $Criterion -Status "FAIL" -Evidence "Missing spike result."
  }

  $requiresVisibleProof = $Criterion -match '(?i)insertion|url bar'
  if ([bool]$SpikeResult.success) {
    if ($requiresVisibleProof -and -not (Test-VerifiedInsertionEvidence -Evidence ([string]$SpikeResult.summary))) {
      return New-EvaluationRow -Criterion $Criterion -Status "FAIL" -Evidence "PASS result did not include verified or human-confirmed visible insertion evidence."
    }

    return New-EvaluationRow -Criterion $Criterion -Status "PASS" -Evidence ([string]$SpikeResult.summary)
  }

  return New-EvaluationRow -Criterion $Criterion -Status "FAIL" -Evidence ([string]$SpikeResult.details)
}

function Test-VerifiedInsertionEvidence {
  param([Parameter(Mandatory = $false)][string]$Evidence)

  if ([string]::IsNullOrWhiteSpace($Evidence)) {
    return $false
  }

  return $Evidence -match '(?i)(verified|automation-captured|final value|human-confirmed|visible result|text appeared|appeared in|inserted phrase|confirmed visible)'
}

function Invoke-TargetInsertionScenario {
  param(
    [Parameter(Mandatory = $true)][string]$Target,
    [Parameter(Mandatory = $true)][string]$RepoRoot,
    [Parameter(Mandatory = $true)][int]$TimeoutSeconds
  )

  $executablePath = Resolve-ExecutablePath -Target $Target
  if ([string]::IsNullOrWhiteSpace($executablePath)) {
    return [pscustomobject]@{
      target = $Target
      status = "BLOCKED"
      result = $null
      reason = "Target application is not installed."
    }
  }

  $process = $null
  try {
    $process = Start-TargetApp -Target $Target
    if ($null -eq $process) {
      return [pscustomobject]@{
        target = $Target
        status = "BLOCKED"
        result = $null
        reason = "Failed to start target application."
      }
    }

    switch ($Target) {
      "chrome" { Start-Sleep -Seconds 3; Invoke-SendKeys -Keys "^l" }
      "vscode" { Start-Sleep -Seconds 3; Invoke-SendKeys -Keys "^n" }
      "slack" { Start-Sleep -Seconds 8; Invoke-SendKeys -Keys "^k" }
      "word" { Start-Sleep -Seconds 8 }
      default { Start-Sleep -Seconds 3 }
    }

    $result = Invoke-SpikeRunner -RepoRoot $RepoRoot -TimeoutSeconds $TimeoutSeconds -Arguments @(
      "insertion",
      "--method", "clipboard",
      "--require-method", "clipboard",
      "--restore-clipboard", "false",
      "--text", "Dictate Anywhere MVP acceptance ($Target)"
    )

    return [pscustomobject]@{
      target = $Target
      status = if ([bool]$result.success) { "PASS" } else { "FAIL" }
      result = $result
      reason = if ([bool]$result.success) { [string]$result.summary } else { [string]$result.details }
    }
  }
  finally {
    Stop-TargetApp -Process $process
  }
}

$repoRoot = Resolve-RepoRoot
Set-Location $repoRoot
Ensure-Dotnet

Write-Step "Building spike runner"
& dotnet build tools/DictateAnywhere.Spikes/DictateAnywhere.Spikes.csproj -c Release --nologo
if ($LASTEXITCODE -ne 0) {
  throw "Spike runner build failed."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$baselineOutputDirectory = Join-Path -Path $OutputDirectory -ChildPath "baseline-spikes"
$baselineReportPath = Join-Path -Path $OutputDirectory -ChildPath "baseline-milestone-0-report.md"

Write-Step "Running baseline Milestone 0 spike suite"
& powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File ".\scripts\run-milestone0-spikes.ps1" `
  -RunHotkey `
  -RunAudio `
  -RunInsertionMatrix `
  -ContinueOnError `
  -OutputDirectory $baselineOutputDirectory `
  -ReportPath $baselineReportPath `
  -PerSpikeTimeoutSeconds $PerSpikeTimeoutSeconds

$baselineResultsPath = Join-Path -Path $baselineOutputDirectory -ChildPath "milestone-0-spike-results.json"
Assert-Condition -Condition (Test-Path -Path $baselineResultsPath) -Message "Baseline spike results were not produced at $baselineResultsPath"

$baselineResults = Get-Content -Path $baselineResultsPath -Raw | ConvertFrom-Json

$resultByName = @{}
foreach ($entry in $baselineResults) {
  $resultByName[[string]$entry.spikeName] = $entry
}

Write-Step "Running additional app matrix scenarios (Word and Slack)"
$wordScenario = Invoke-TargetInsertionScenario -Target "word" -RepoRoot $repoRoot -TimeoutSeconds $PerSpikeTimeoutSeconds
$slackScenario = Invoke-TargetInsertionScenario -Target "slack" -RepoRoot $repoRoot -TimeoutSeconds $PerSpikeTimeoutSeconds

Write-Step "Evaluating Milestone 1 acceptance criteria"
$rows = New-Object System.Collections.Generic.List[object]

$rows.Add((Evaluate-SpikeCriterion -Criterion "VS Code insertion" -SpikeResult $resultByName["insertion-clipboard-vscode"]))
$rows.Add((Evaluate-SpikeCriterion -Criterion "Chrome URL bar insertion" -SpikeResult $resultByName["insertion-clipboard-chrome"]))

if ($wordScenario.status -eq "BLOCKED") {
  $rows.Add((New-EvaluationRow -Criterion "Word insertion" -Status "BLOCKED" -Evidence $wordScenario.reason))
}
else {
  $rows.Add((Evaluate-SpikeCriterion -Criterion "Word insertion" -SpikeResult $wordScenario.result))
}

if ($slackScenario.status -eq "BLOCKED") {
  $rows.Add((New-EvaluationRow -Criterion "Slack insertion (non-admin)" -Status "BLOCKED" -Evidence $slackScenario.reason))
}
else {
  $rows.Add((Evaluate-SpikeCriterion -Criterion "Slack insertion (non-admin)" -SpikeResult $slackScenario.result))
}

$summaryPath = Join-Path -Path $OutputDirectory -ChildPath "milestone-1-acceptance-results.json"
$summary = [pscustomobject]@{
  generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
  baselineResultsPath = $baselineResultsPath
  wordScenario = $wordScenario
  slackScenario = $slackScenario
  criteria = $rows
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -NoNewline $summaryPath

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Milestone 1 Acceptance Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ssK')")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Baseline spikes: $baselineResultsPath")
$reportLines.Add("- Summary JSON: $summaryPath")
$reportLines.Add("")
$reportLines.Add("## Acceptance Matrix")
$reportLines.Add("")
$reportLines.Add("| Criterion | Status | Evidence |")
$reportLines.Add("|---|---|---|")

foreach ($row in $rows) {
  $safeEvidence = ([string]$row.evidence).Replace("|", "/")
  $reportLines.Add("| $([string]$row.criterion) | $([string]$row.status) | $safeEvidence |")
}

$reportLines.Add("")
$reportLines.Add("## Additional Scenario Notes")
$reportLines.Add("")
$reportLines.Add("- Word scenario: $([string]$wordScenario.status) - $([string]$wordScenario.reason)")
$reportLines.Add("- Slack scenario: $([string]$slackScenario.status) - $([string]$slackScenario.reason)")

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
  New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}
$reportLines | Set-Content -NoNewline:$false $ReportPath

Write-Step "Milestone 1 acceptance run complete"
Write-Host "Report: $ReportPath" -ForegroundColor Green
Write-Host "JSON:   $summaryPath" -ForegroundColor Green

$blockingRows = @($rows | Where-Object { $_.status -in @("FAIL", "BLOCKED") })
if ($blockingRows.Count -gt 0 -and -not $ContinueOnError) {
  exit 1
}

exit 0
