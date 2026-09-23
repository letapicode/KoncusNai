param(
  [int]$Iterations = 25,
  [string]$OutputDirectory = "artifacts/soak",
  [string]$ReportPath = "docs/release/soak-test-report.md",
  [string]$TestProjectPath = "tests/DictateAnywhere.Core.Tests/DictateAnywhere.Core.Tests.csproj",
  [string]$TestFilter = "FullyQualifiedName~HoldToTalk_LongRunCycles_RemainsStable"
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

if ($Iterations -le 0) {
  throw "Iterations must be greater than zero."
}

$repoRoot = Resolve-RepoRoot
Set-Location -Path $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required for soak tests."
}

$resolvedOutputDirectory = Resolve-RepoPath -PathValue $OutputDirectory -RepoRoot $repoRoot
$resolvedReportPath = Resolve-RepoPath -PathValue $ReportPath -RepoRoot $repoRoot
$resolvedTestProjectPath = Resolve-RepoPath -PathValue $TestProjectPath -RepoRoot $repoRoot
$resultsDirectory = Join-Path -Path $resolvedOutputDirectory -ChildPath "test-results"

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null

$results = New-Object System.Collections.Generic.List[object]

$discoveryOutput = @(& dotnet test $resolvedTestProjectPath `
  -c Release `
  --nologo `
  --list-tests `
  --filter $TestFilter 2>&1)
$discoveryExitCode = $LASTEXITCODE
$discovered = @($discoveryOutput | Where-Object { $_ -match "^\s{4}\S" }).Count
if ($discoveryExitCode -ne 0 -or $discovered -eq 0) {
  $discoveryOutput | Out-Host
  throw "Soak filter discovery failed or found no tests."
}

Write-Host "Discovered $discovered matching soak test(s)."

Write-Step "Running soak test loop ($Iterations iterations)"
for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
  $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
  $trxName = "soak-{0:D3}-{1}.trx" -f $iteration, $timestamp
  $testStart = Get-Date

  & dotnet test $resolvedTestProjectPath `
    -c Release `
    --nologo `
    --results-directory $resultsDirectory `
    --logger "trx;LogFileName=$trxName" `
    --filter $TestFilter | Out-Host

  $duration = (Get-Date) - $testStart
  $success = $LASTEXITCODE -eq 0
  $results.Add([PSCustomObject]@{
    iteration = $iteration
    success = $success
    exitCode = $LASTEXITCODE
    durationSeconds = [Math]::Round($duration.TotalSeconds, 2)
    trx = $trxName
  })
}

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "soak-results.json"
$results | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Soak Test Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Iterations: $Iterations")
$reportLines.Add("- Test project: $resolvedTestProjectPath")
$reportLines.Add("- Test filter: $TestFilter")
$reportLines.Add("- Summary JSON: $summaryPath")
$reportLines.Add("")
$reportLines.Add("| Iteration | Status | Duration (s) | TRX |")
$reportLines.Add("|---:|---|---:|---|")
foreach ($result in $results) {
  $status = if ([bool]$result.success) { "PASS" } else { "FAIL" }
  $reportLines.Add("| $([int]$result.iteration) | $status | $([double]$result.durationSeconds) | $([string]$result.trx) |")
}
$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Step "Soak test run complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

$failed = @($results | Where-Object { -not [bool]$_.success })
if ($failed.Count -gt 0) {
  throw "Soak test detected failures."
}
