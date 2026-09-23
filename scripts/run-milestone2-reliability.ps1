param(
  [string]$OutputDirectory = "artifacts/milestone2",
  [string]$ReportPath = "docs/release/milestone-2-reliability-report.md",
  [int]$SoakIterations = 10
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Step {
  param([string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Condition {
  param(
    [bool]$Condition,
    [string]$Message
  )

  if (-not $Condition) {
    throw $Message
  }
}

function Invoke-DotnetTest {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [string]$Filter,
    [int]$Iteration = 0,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory
  )

  $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
  $safeName = $Name -replace "[^a-zA-Z0-9\-]", "-"
  $trxName = if ($Iteration -gt 0) { "{0}-{1:D3}-{2}.trx" -f $safeName, $Iteration, $timestamp } else { "{0}-{1}.trx" -f $safeName, $timestamp }

  $arguments = @(
    "test",
    $ProjectPath,
    "-c", "Release",
    "--nologo",
    "--results-directory", $ResultsDirectory,
    "--logger", "trx;LogFileName=$trxName"
  )

  if (-not [string]::IsNullOrWhiteSpace($Filter)) {
    $arguments += @("--filter", $Filter)

    $discoveryArguments = @(
      "test",
      $ProjectPath,
      "-c", "Release",
      "--nologo",
      "--list-tests",
      "--filter", $Filter
    )
    $discoveryOutput = @(& dotnet @discoveryArguments 2>&1)
    $discoveryExitCode = $LASTEXITCODE
    $discovered = @($discoveryOutput | Where-Object { $_ -match "^\s{4}\S" }).Count
    if ($discoveryExitCode -ne 0 -or $discovered -eq 0) {
      $discoveryOutput | Out-Host
      return [PSCustomObject]@{
        name = $Name
        project = $ProjectPath
        filter = $Filter
        iteration = $Iteration
        success = $false
        exitCode = if ($discoveryExitCode -ne 0) { $discoveryExitCode } else { 1 }
        durationSeconds = 0
        trx = ""
      }
    }

    Write-Host "Discovered $discovered matching test(s) for $Name."
  }

  $start = Get-Date
  & dotnet @arguments | Out-Host
  $exitCode = $LASTEXITCODE
  $duration = (Get-Date) - $start

  return [PSCustomObject]@{
    name = $Name
    project = $ProjectPath
    filter = $Filter
    iteration = $Iteration
    success = ($exitCode -eq 0)
    exitCode = $exitCode
    durationSeconds = [Math]::Round($duration.TotalSeconds, 2)
    trx = $trxName
  }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -Path $repoRoot

Assert-Condition -Condition ($SoakIterations -gt 0) -Message "SoakIterations must be greater than zero."

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required for reliability regression."
}

$outputRoot = Join-Path -Path $repoRoot -ChildPath $OutputDirectory
$testResultsDirectory = Join-Path -Path $outputRoot -ChildPath "test-results"
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $testResultsDirectory -Force | Out-Null

$results = New-Object System.Collections.Generic.List[object]

Write-Step "Running Milestone 2 reliability baseline tests"
$results.Add((Invoke-DotnetTest -Name "audio-tests" -ProjectPath "tests/DictateAnywhere.Audio.Tests/DictateAnywhere.Audio.Tests.csproj" -ResultsDirectory $testResultsDirectory))
$results.Add((Invoke-DotnetTest -Name "insertion-tests" -ProjectPath "tests/DictateAnywhere.Insertion.Tests/DictateAnywhere.Insertion.Tests.csproj" -ResultsDirectory $testResultsDirectory))
$results.Add((Invoke-DotnetTest -Name "app-tests" -ProjectPath "tests/DictateAnywhere.App.Tests/DictateAnywhere.App.Tests.csproj" -ResultsDirectory $testResultsDirectory))
$results.Add((Invoke-DotnetTest -Name "core-tests" -ProjectPath "tests/DictateAnywhere.Core.Tests/DictateAnywhere.Core.Tests.csproj" -ResultsDirectory $testResultsDirectory))

Write-Step "Running long-run stability soak for coordinator pipeline ($SoakIterations iterations)"
for ($iteration = 1; $iteration -le $SoakIterations; $iteration++) {
  $results.Add((Invoke-DotnetTest `
      -Name "core-soak" `
      -ProjectPath "tests/DictateAnywhere.Core.Tests/DictateAnywhere.Core.Tests.csproj" `
      -Filter "FullyQualifiedName~HoldToTalk_LongRunCycles_RemainsStable" `
      -Iteration $iteration `
      -ResultsDirectory $testResultsDirectory))
}

$summaryPath = Join-Path -Path $outputRoot -ChildPath "milestone-2-reliability-results.json"
$results | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath -Encoding UTF8

$allPassed = $true
foreach ($result in $results) {
  if (-not [bool]$result.success) {
    $allPassed = $false
    break
  }
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Milestone 2 Reliability Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Soak iterations: $SoakIterations")
$reportLines.Add("- Results JSON: $summaryPath")
$reportLines.Add("")
$reportLines.Add("## Execution Matrix")
$reportLines.Add("")
$reportLines.Add("| Scenario | Iteration | Status | Duration (s) | Filter | TRX |")
$reportLines.Add("|---|---:|---|---:|---|---|")

foreach ($result in $results) {
  $status = if ([bool]$result.success) { "PASS" } else { "FAIL" }
  $filterText = if ([string]::IsNullOrWhiteSpace([string]$result.filter)) { "-" } else { [string]$result.filter }
  $reportLines.Add("| $([string]$result.name) | $([int]$result.iteration) | $status | $([double]$result.durationSeconds) | $filterText | $([string]$result.trx) |")
}

$reportLines.Add("")
$reportLines.Add("## Summary")
$reportLines.Add("")
$reportLines.Add("- Overall status: $(if ($allPassed) { "PASS" } else { "FAIL" })")

if (-not $allPassed) {
  $reportLines.Add("- Failure details:")
  foreach ($result in $results) {
    if (-not [bool]$result.success) {
      $reportLines.Add("  - $([string]$result.name) iteration $([int]$result.iteration) failed with exit code $([int]$result.exitCode).")
    }
  }
}

$resolvedReportPath = Join-Path -Path $repoRoot -ChildPath $ReportPath
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null
$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Step "Milestone 2 reliability run complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

if (-not $allPassed) {
  throw "Milestone 2 reliability regression detected failures."
}
