param(
  [string]$OutputDirectory = "artifacts/fault-injection",
  [string]$ReportPath = "docs/release/fault-injection-report.md"
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
  return [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
}

function Resolve-RepoPath {
  param(
    [Parameter(Mandatory = $true)][string]$PathValue,
    [Parameter(Mandatory = $true)][string]$RepoRoot
  )

  if ([IO.Path]::IsPathRooted($PathValue)) {
    return [IO.Path]::GetFullPath($PathValue)
  }

  return [IO.Path]::GetFullPath((Join-Path -Path $RepoRoot -ChildPath $PathValue))
}

function Invoke-FaultScenario {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$Filter,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory
  )

  $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
  $safeName = $Name -replace "[^a-zA-Z0-9\-]", "-"
  $trxName = "$safeName-$timestamp.trx"
  $start = Get-Date

  $discoveryOutput = @(& dotnet test $ProjectPath `
    -c Release `
    --nologo `
    --list-tests `
    --filter $Filter 2>&1)
  $discoveryExitCode = $LASTEXITCODE
  $discovered = @($discoveryOutput | Where-Object { $_ -match "^\s{4}\S" }).Count
  if ($discoveryExitCode -ne 0 -or $discovered -eq 0) {
    $discoveryOutput | Out-Host
    $duration = (Get-Date) - $start
    return [PSCustomObject]@{
      scenario = $Name
      project = $ProjectPath
      filter = $Filter
      success = $false
      exitCode = if ($discoveryExitCode -ne 0) { $discoveryExitCode } else { 1 }
      durationSeconds = [Math]::Round($duration.TotalSeconds, 2)
      trx = ""
    }
  }

  Write-Host "Discovered $discovered matching test(s)."

  & dotnet test $ProjectPath `
    -c Release `
    --no-restore `
    --no-build `
    --nologo `
    --results-directory $ResultsDirectory `
    --logger "trx;LogFileName=$trxName" `
    --filter $Filter | Out-Host

  $duration = (Get-Date) - $start
  return [PSCustomObject]@{
    scenario = $Name
    project = $ProjectPath
    filter = $Filter
    success = ($LASTEXITCODE -eq 0)
    exitCode = $LASTEXITCODE
    durationSeconds = [Math]::Round($duration.TotalSeconds, 2)
    trx = $trxName
  }
}

$repoRoot = Resolve-RepoRoot
Set-Location -Path $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required for fault-injection scenarios."
}

$resolvedOutputDirectory = Resolve-RepoPath -PathValue $OutputDirectory -RepoRoot $repoRoot
$resolvedReportPath = Resolve-RepoPath -PathValue $ReportPath -RepoRoot $repoRoot
$resultsDirectory = Join-Path -Path $resolvedOutputDirectory -ChildPath "test-results"

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null

$scenarios = @(
  @{
    Name = "Audio device removal handling"
    Project = "tests/DictateAnywhere.Audio.Tests/DictateAnywhere.Audio.Tests.csproj"
    Filter = "FullyQualifiedName~StopAsync_ThrowsWhenDeviceErrorOccurs"
  },
  @{
    Name = "Model corrupt snapshot recovery"
    Project = "tests/DictateAnywhere.Models.Tests/DictateAnywhere.Models.Tests.csproj"
    Filter = "FullyQualifiedName~GetModelsAsync_WhenConfigJsonIsZeroBytes_TreatsModelAsNotInstalled"
  },
  @{
    Name = "Model snapshot promotion rollback"
    Project = "tests/DictateAnywhere.Models.Tests/DictateAnywhere.Models.Tests.csproj"
    Filter = "FullyQualifiedName~PromoteSnapshotDirectory_WhenPromotionFails_RestoresExistingSnapshot"
  },
  @{
    Name = "Hotkey conflict detection"
    Project = "tests/DictateAnywhere.Hotkeys.Tests/DictateAnywhere.Hotkeys.Tests.csproj"
    Filter = "FullyQualifiedName~ValidateAsync_ReturnsFailure_WithoutUnregister"
  },
  @{
    Name = "Privilege boundary routing"
    Project = "tests/DictateAnywhere.Insertion.Tests/DictateAnywhere.Insertion.Tests.csproj"
    Filter = "FullyQualifiedName~InsertAsync_BlockedByPrivilegeBoundary_WithElevatedInsertionEnabled_RoutesToHelper"
  }
)

$results = New-Object System.Collections.Generic.List[object]
Write-Step "Running fault-injection scenario matrix"
foreach ($scenario in $scenarios) {
  $projectPath = Resolve-RepoPath -PathValue $scenario.Project -RepoRoot $repoRoot
  $results.Add((Invoke-FaultScenario `
      -Name $scenario.Name `
      -ProjectPath $projectPath `
      -Filter $scenario.Filter `
      -ResultsDirectory $resultsDirectory))
}

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "fault-injection-results.json"
$results | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Fault Injection Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Summary JSON: $summaryPath")
$reportLines.Add("")
$reportLines.Add("| Scenario | Status | Duration (s) | Filter | TRX |")
$reportLines.Add("|---|---|---:|---|---|")
foreach ($result in $results) {
  $status = if ([bool]$result.success) { "PASS" } else { "FAIL" }
  $reportLines.Add("| $([string]$result.scenario) | $status | $([double]$result.durationSeconds) | $([string]$result.filter) | $([string]$result.trx) |")
}
$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Step "Fault-injection run complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

$failed = @($results | Where-Object { -not [bool]$_.success })
if ($failed.Count -gt 0) {
  throw "Fault-injection scenarios detected failures."
}
