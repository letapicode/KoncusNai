param(
  [string]$OutputDirectory = "artifacts/quality",
  [string]$ReportPath = "docs/release/quality-suite-report.md",
  [int]$SoakIterations = 10,
  [switch]$RunEndToEndSmoke,
  [switch]$ContinueOnError
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

function Invoke-ExternalCommand {
  param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string[]]$Arguments = @()
  )

  & $Executable @Arguments | Out-Host
  if ($LASTEXITCODE -ne 0) {
    throw "Command failed ($LASTEXITCODE): $Executable $($Arguments -join ' ')"
  }
}

$repoRoot = Resolve-RepoRoot
Set-Location -Path $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required for the quality suite."
}

$resolvedOutputDirectory = Resolve-RepoPath -PathValue $OutputDirectory -RepoRoot $repoRoot
$resolvedReportPath = Resolve-RepoPath -PathValue $ReportPath -RepoRoot $repoRoot

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null

$results = New-Object System.Collections.Generic.List[object]

function Invoke-QualityStep {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][scriptblock]$Action
  )

  Write-Step $Name
  $start = Get-Date
  $success = $true
  $failureMessage = ""

  try {
    & $Action
  }
  catch {
    $success = $false
    $failureMessage = $_.Exception.Message
  }

  $duration = (Get-Date) - $start
  $results.Add([PSCustomObject]@{
    step = $Name
    success = $success
    durationSeconds = [Math]::Round($duration.TotalSeconds, 2)
    details = if ($success) { "Completed." } else { $failureMessage }
  })

  if (-not $success -and -not $ContinueOnError) {
    throw "Quality step failed: $Name. $failureMessage"
  }
}

Invoke-QualityStep -Name "Documentation claims" -Action {
  Invoke-ExternalCommand -Executable "powershell" -Arguments @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", ".\scripts\validate-documentation-claims.ps1")
}

Invoke-QualityStep -Name "Tracked and bundled size budgets" -Action {
  Invoke-ExternalCommand -Executable "powershell" -Arguments @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", ".\scripts\validate-size-budgets.ps1")
}

Invoke-QualityStep -Name "Solution unit/integration regression" -Action {
  Invoke-ExternalCommand -Executable "dotnet" -Arguments @(
    "test",
    "DictateAnywhere.sln",
    "-c", "Release",
    "--maxcpucount:1",
    "--nologo")
}

Invoke-QualityStep -Name "Public API and compiled dependency contract" -Action {
  Invoke-ExternalCommand -Executable "powershell" -Arguments @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", ".\scripts\validate-public-api.ps1",
    "-NoBuild")
}

Invoke-QualityStep -Name "Project reference guardrails" -Action {
  Invoke-ExternalCommand -Executable "powershell" -Arguments @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", ".\scripts\validate-project-reference-guardrails.ps1",
    "-OutputDirectory", (Join-Path -Path $resolvedOutputDirectory -ChildPath "architecture"),
    "-ReportPath", "docs/release/project-reference-guardrails-report.md")
}

Invoke-QualityStep -Name "English baseline regression checks" -Action {
  Invoke-ExternalCommand -Executable "powershell" -Arguments @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", ".\scripts\run-english-baseline-regression.ps1")
}

Invoke-QualityStep -Name "Performance regression checks" -Action {
  Invoke-ExternalCommand -Executable "powershell" -Arguments @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", ".\scripts\run-performance-regression.ps1",
    "-OutputDirectory", (Join-Path -Path $resolvedOutputDirectory -ChildPath "performance"),
    "-ReportPath", "docs/release/performance-regression-report.md")
}

Invoke-QualityStep -Name "Soak stability checks" -Action {
  Invoke-ExternalCommand -Executable "powershell" -Arguments @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", ".\scripts\run-soak-test.ps1",
    "-Iterations", $SoakIterations.ToString([System.Globalization.CultureInfo]::InvariantCulture),
    "-OutputDirectory", (Join-Path -Path $resolvedOutputDirectory -ChildPath "soak"),
    "-ReportPath", "docs/release/soak-test-report.md")
}

Invoke-QualityStep -Name "Fault injection checks" -Action {
  Invoke-ExternalCommand -Executable "powershell" -Arguments @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", ".\scripts\run-fault-injection.ps1",
    "-OutputDirectory", (Join-Path -Path $resolvedOutputDirectory -ChildPath "fault-injection"),
    "-ReportPath", "docs/release/fault-injection-report.md")
}

if ($RunEndToEndSmoke) {
  Invoke-QualityStep -Name "End-to-end acceptance smoke" -Action {
    Invoke-ExternalCommand -Executable "powershell" -Arguments @(
      "-NoProfile",
      "-ExecutionPolicy", "Bypass",
      "-File", ".\scripts\run-milestone1-acceptance.ps1",
      "-ContinueOnError",
      "-OutputDirectory", (Join-Path -Path $resolvedOutputDirectory -ChildPath "milestone1"),
      "-ReportPath", "docs/release/milestone-1-acceptance-report.md")
  }
}
else {
  $results.Add([PSCustomObject]@{
    step = "End-to-end acceptance smoke"
    success = $true
    durationSeconds = 0
    details = "Skipped. Use -RunEndToEndSmoke to enable interactive app matrix validation."
  })
}

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "quality-suite-results.json"
$results | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Quality Suite Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Soak iterations: $SoakIterations")
$reportLines.Add("- End-to-end smoke: $([bool]$RunEndToEndSmoke)")
$reportLines.Add("- Continue on error: $([bool]$ContinueOnError)")
$reportLines.Add("- Summary JSON: $summaryPath")
$reportLines.Add("")
$reportLines.Add("| Step | Status | Duration (s) | Details |")
$reportLines.Add("|---|---|---:|---|")
foreach ($result in $results) {
  $status = if ([bool]$result.success) { "PASS" } else { "FAIL" }
  $safeDetails = ([string]$result.details).Replace("|", "/")
  $reportLines.Add("| $([string]$result.step) | $status | $([double]$result.durationSeconds) | $safeDetails |")
}
$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Step "Quality suite run complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

$failed = @($results | Where-Object { -not [bool]$_.success })
if ($failed.Count -gt 0) {
  throw "Quality suite detected failures."
}
