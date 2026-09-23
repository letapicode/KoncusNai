param(
  [string]$OutputDirectory = "artifacts/english-baseline-regression",
  [string]$ReportPath = "docs/release/english-baseline-regression-report.md"
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

function Invoke-TestCriterion {
  param(
    [Parameter(Mandatory = $true)]$Rows,
    [Parameter(Mandatory = $true)][string]$Criterion,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$Filter,
    [Parameter(Mandatory = $true)][string]$SuccessEvidence
  )

  Write-Step $Criterion
  $arguments = @(
    "test",
    $ProjectPath,
    "-c", "Release",
    "--nologo",
    "--filter", $Filter)

  $testOutput = @(& dotnet @arguments 2>&1)
  $testExitCode = $LASTEXITCODE
  $testOutput | Out-Host
  $matchedNoTests = @($testOutput | Where-Object {
    [string]$_ -match "No test matches the given testcase filter|No test is available"
  }).Count -gt 0

  if ($testExitCode -eq 0 -and -not $matchedNoTests) {
    $Rows.Add([PSCustomObject]@{
      criterion = $Criterion
      status = "PASS"
      evidence = $SuccessEvidence
    })
    return
  }

  $Rows.Add([PSCustomObject]@{
    criterion = $Criterion
    status = "FAIL"
    evidence = if ($matchedNoTests) {
      "No test matched filter '$Filter'."
    }
    else {
      "dotnet test failed for filter '$Filter'."
    }
  })
}

$repoRoot = Resolve-RepoRoot
Set-Location -Path $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required for English baseline regression checks."
}

$resolvedOutputDirectory = Resolve-RepoPath -PathValue $OutputDirectory -RepoRoot $repoRoot
$resolvedReportPath = Resolve-RepoPath -PathValue $ReportPath -RepoRoot $repoRoot

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null

$rows = New-Object System.Collections.Generic.List[object]

Invoke-TestCriterion -Rows $rows `
  -Criterion "English baseline: current provider models remain registered" `
  -ProjectPath ".\tests\DictateAnywhere.Models.Tests\DictateAnywhere.Models.Tests.csproj" `
  -Filter "FullyQualifiedName~DictateAnywhere.Models.Tests.CompositeModelManagerTests" `
  -SuccessEvidence "Verified current Cohere and CrisperWhisper model managers remain composed."

Invoke-TestCriterion -Rows $rows `
  -Criterion "English baseline: legacy settings migration defaults to English" `
  -ProjectPath ".\tests\DictateAnywhere.Settings.Tests\DictateAnywhere.Settings.Tests.csproj" `
  -Filter "FullyQualifiedName=DictateAnywhere.Settings.Tests.JsonSettingsStoreTests.LoadAsync_MigratesLegacySchemaV1_ToEnglishBaseline_WhenLanguageMissing" `
  -SuccessEvidence "Verified legacy settings files without transcriptionLanguage still load with English baseline."

Invoke-TestCriterion -Rows $rows `
  -Criterion "English baseline: profile resolver defaults to English" `
  -ProjectPath ".\tests\DictateAnywhere.Core.Tests\DictateAnywhere.Core.Tests.csproj" `
  -Filter "FullyQualifiedName=DictateAnywhere.Core.Tests.DictationProfileResolverTests.ResolveEffectiveTranscriptionLanguage_UsesEnglishDefault_WhenGlobalAndOverrideMissing" `
  -SuccessEvidence "Verified effective transcription language resolves to 'en' when global and profile values are absent."

Invoke-TestCriterion -Rows $rows `
  -Criterion "English baseline: benchmark uses current provider candidates" `
  -ProjectPath ".\tests\DictateAnywhere.Benchmark.Tests\DictateAnywhere.Benchmark.Tests.csproj" `
  -Filter "FullyQualifiedName=DictateAnywhere.Benchmark.Tests.SmokeTests.RunAsync_PersistsCurrentProviderCandidates" `
  -SuccessEvidence "Verified benchmark persistence contains only current Cohere and CrisperWhisper candidates."

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "english-baseline-regression-results.json"
$summary = [PSCustomObject]@{
  schemaVersion = 1
  generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
  criteria = $rows
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# English Baseline Regression Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Summary JSON: $summaryPath")
$reportLines.Add("")
$reportLines.Add("| Criterion | Status | Evidence |")
$reportLines.Add("|---|---|---|")
foreach ($row in $rows) {
  $safeEvidence = ([string]$row.evidence).Replace("|", "/")
  $reportLines.Add("| $([string]$row.criterion) | $([string]$row.status) | $safeEvidence |")
}
$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Step "English baseline regression complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

$failedRows = @($rows | Where-Object { [string]::Equals([string]$_.status, "FAIL", [StringComparison]::OrdinalIgnoreCase) })
if ($failedRows.Count -gt 0) {
  throw "English baseline regression failed with $($failedRows.Count) failing criterion/criteria."
}
