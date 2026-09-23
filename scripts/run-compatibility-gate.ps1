param(
  [string]$OutputDirectory = "artifacts/compatibility",
  [string]$ReportPath = "docs/release/compatibility-gate-report.md",
  [string]$CompatibilityMatrixPath = "docs/release/compatibility-matrix.json",
  [string]$ReleaseConstraintsPath = "docs/release/release-constraints.json",
  [string]$AcceptanceResultsPath = "artifacts/milestone1/milestone-1-acceptance-results.json",
  [string]$ReliabilityResultsPath = "artifacts/milestone2/milestone-2-reliability-results.json",
  [string]$ElevatedResultsPath = "artifacts/milestone3/milestone-3-elevated-acceptance-results.json",
  [string]$InstallerValidationSummaryPath = "",
  [string]$WorkbenchAcceptanceResultsPath = "artifacts/workbench-acceptance/workbench-acceptance-results.json",
  [string]$GlobalToggleAcceptanceResultsPath = "artifacts/global-toggle-acceptance/global-toggle-acceptance-results.json",
  [string]$EnglishBaselineRegressionResultsPath = "artifacts/english-baseline-regression/english-baseline-regression-results.json",
  [string]$MultilingualAcceptanceResultsPath = "artifacts/multilingual-acceptance/multilingual-acceptance-results.json",
  [string]$ManualEvidencePath = "artifacts/compatibility/manual-app-matrix-results.json",
  [string]$ReleaseLine = "1.x",
  [switch]$EnforceReleaseEvidence,
  [switch]$RequireElevatedEvidence
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

function Add-ResultRow {
  param(
    [Parameter(Mandatory = $true)]$Rows,
    [Parameter(Mandatory = $true)][string]$Criterion,
    [Parameter(Mandatory = $true)][string]$Status,
    [Parameter(Mandatory = $true)][string]$Evidence
  )

  $Rows.Add([PSCustomObject]@{
    criterion = $Criterion
    status = $Status
    evidence = $Evidence
  })
}

function Read-JsonFile {
  param([Parameter(Mandatory = $true)][string]$Path)
  return Get-Content -Path $Path -Raw | ConvertFrom-Json
}

function Get-CriterionStatus {
  param(
    [Parameter(Mandatory = $true)]$AcceptanceJson,
    [Parameter(Mandatory = $true)][string]$CriterionName
  )

  foreach ($criterion in @($AcceptanceJson.criteria)) {
    if ([string]::Equals([string]$criterion.criterion, $CriterionName, [StringComparison]::OrdinalIgnoreCase)) {
      return [PSCustomObject]@{
        found = $true
        status = [string]$criterion.status
        evidence = [string]$criterion.evidence
      }
    }
  }

  return [PSCustomObject]@{
    found = $false
    status = ""
    evidence = ""
  }
}

function Test-VerifiedInsertionEvidence {
  param([Parameter(Mandatory = $false)][string]$Evidence)

  if ([string]::IsNullOrWhiteSpace($Evidence)) {
    return $false
  }

  return $Evidence -match '(?i)(verified|automation-captured|final value|human-confirmed|visible result|text appeared|appeared in|inserted phrase|confirmed visible)'
}

function Resolve-LatestInstallerValidationSummary {
  param([Parameter(Mandatory = $true)][string]$RepoRoot)

  $validationRoot = Join-Path -Path $RepoRoot -ChildPath "artifacts/installer-validation"
  if (-not (Test-Path -Path $validationRoot)) {
    return $null
  }

  $candidates = @(Get-ChildItem -Path $validationRoot -Directory -ErrorAction SilentlyContinue | Sort-Object -Property Name -Descending)
  foreach ($candidate in $candidates) {
    $summaryPath = Join-Path -Path $candidate.FullName -ChildPath "installer-scenario-summary.json"
    if (Test-Path -Path $summaryPath) {
      return [IO.Path]::GetFullPath($summaryPath)
    }
  }

  return $null
}

$repoRoot = Resolve-RepoRoot
Set-Location -Path $repoRoot

$resolvedOutputDirectory = Resolve-RepoPath -PathValue $OutputDirectory -RepoRoot $repoRoot
$resolvedReportPath = Resolve-RepoPath -PathValue $ReportPath -RepoRoot $repoRoot
$resolvedMatrixPath = Resolve-RepoPath -PathValue $CompatibilityMatrixPath -RepoRoot $repoRoot
$resolvedConstraintsPath = Resolve-RepoPath -PathValue $ReleaseConstraintsPath -RepoRoot $repoRoot
$resolvedAcceptancePath = Resolve-RepoPath -PathValue $AcceptanceResultsPath -RepoRoot $repoRoot
$resolvedReliabilityPath = Resolve-RepoPath -PathValue $ReliabilityResultsPath -RepoRoot $repoRoot
$resolvedElevatedPath = Resolve-RepoPath -PathValue $ElevatedResultsPath -RepoRoot $repoRoot
$resolvedWorkbenchAcceptancePath = Resolve-RepoPath -PathValue $WorkbenchAcceptanceResultsPath -RepoRoot $repoRoot
$resolvedGlobalToggleAcceptancePath = Resolve-RepoPath -PathValue $GlobalToggleAcceptanceResultsPath -RepoRoot $repoRoot
$resolvedEnglishBaselineRegressionPath = Resolve-RepoPath -PathValue $EnglishBaselineRegressionResultsPath -RepoRoot $repoRoot
$resolvedMultilingualAcceptancePath = Resolve-RepoPath -PathValue $MultilingualAcceptanceResultsPath -RepoRoot $repoRoot
$resolvedManualEvidencePath = Resolve-RepoPath -PathValue $ManualEvidencePath -RepoRoot $repoRoot
$resolvedInstallerValidationSummaryPath = $null
if (-not [string]::IsNullOrWhiteSpace($InstallerValidationSummaryPath)) {
  $resolvedInstallerValidationSummaryPath = Resolve-RepoPath -PathValue $InstallerValidationSummaryPath -RepoRoot $repoRoot
}
else {
  $resolvedInstallerValidationSummaryPath = Resolve-LatestInstallerValidationSummary -RepoRoot $repoRoot
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null

$rows = New-Object System.Collections.Generic.List[object]

Write-Step "Loading compatibility contract"
if (-not (Test-Path -Path $resolvedMatrixPath)) {
  throw "Compatibility matrix file not found: $resolvedMatrixPath"
}
if (-not (Test-Path -Path $resolvedConstraintsPath)) {
  throw "Release constraints file not found: $resolvedConstraintsPath"
}

$matrix = Read-JsonFile -Path $resolvedMatrixPath
$constraints = Read-JsonFile -Path $resolvedConstraintsPath

if ($null -eq $matrix.supportedWindows -or @($matrix.supportedWindows).Count -eq 0) {
  Add-ResultRow -Rows $rows -Criterion "Supported Windows contract" -Status "FAIL" -Evidence "No supportedWindows entries defined."
}
else {
  $supportedWindows11 = @($matrix.supportedWindows | Where-Object {
      [string]::Equals([string]$_.id, "windows-11", [StringComparison]::OrdinalIgnoreCase) -and
      [string]::Equals([string]$_.status, "supported", [StringComparison]::OrdinalIgnoreCase)
    })
  if ($supportedWindows11.Count -gt 0) {
    $hasX64 = $false
    foreach ($entry in $supportedWindows11) {
      foreach ($arch in @($entry.architectures)) {
        if ([string]::Equals([string]$arch, "x64", [StringComparison]::OrdinalIgnoreCase)) {
          $hasX64 = $true
          break
        }
      }

      if ($hasX64) {
        break
      }
    }

    if ($hasX64) {
      Add-ResultRow -Rows $rows -Criterion "Supported Windows contract" -Status "PASS" -Evidence "Windows 11 supported contract includes x64 architecture."
    }
    else {
      Add-ResultRow -Rows $rows -Criterion "Supported Windows contract" -Status "FAIL" -Evidence "Windows 11 supported contract does not include x64."
    }
  }
  else {
    Add-ResultRow -Rows $rows -Criterion "Supported Windows contract" -Status "FAIL" -Evidence "Missing supported windows-11 entry."
  }
}

$requiredHardware = @("older-i5", "modern-i5")
$hardwareClassIds = @($matrix.hardwareClasses | ForEach-Object { [string]$_.classId })
$missingHardware = @()
foreach ($required in $requiredHardware) {
  if (-not ($hardwareClassIds | Where-Object { [string]::Equals($_, $required, [StringComparison]::OrdinalIgnoreCase) })) {
    $missingHardware += $required
  }
}

if ($missingHardware.Count -eq 0) {
  Add-ResultRow -Rows $rows -Criterion "Hardware class contract" -Status "PASS" -Evidence "older-i5 and modern-i5 classes are defined."
}
else {
  Add-ResultRow -Rows $rows -Criterion "Hardware class contract" -Status "FAIL" -Evidence ("Missing hardware classes: " + ($missingHardware -join ", "))
}

$requiredCategories = @("ide", "browser", "docs", "terminal", "chat")
$matrixCategories = @($matrix.appTargets | ForEach-Object { [string]$_.category.ToLowerInvariant() } | Sort-Object -Unique)
$missingCategories = @()
foreach ($category in $requiredCategories) {
  if (-not ($matrixCategories | Where-Object { $_ -eq $category })) {
    $missingCategories += $category
  }
}

if ($missingCategories.Count -eq 0) {
  Add-ResultRow -Rows $rows -Criterion "App target matrix contract" -Status "PASS" -Evidence "All required categories (IDE/browser/docs/terminal/chat) are defined."
}
else {
  Add-ResultRow -Rows $rows -Criterion "App target matrix contract" -Status "FAIL" -Evidence ("Missing categories: " + ($missingCategories -join ", "))
}

$selectedRelease = @($constraints.releaseConstraints | Where-Object {
    [string]::Equals([string]$_.releaseLine, $ReleaseLine, [StringComparison]::OrdinalIgnoreCase)
  } | Select-Object -First 1)

if ($selectedRelease.Count -eq 0) {
  Add-ResultRow -Rows $rows -Criterion "Release non-goals/limitations register" -Status "FAIL" -Evidence "Release line '$ReleaseLine' is not defined in release constraints."
}
else {
  $entry = $selectedRelease[0]
  if (@($entry.nonGoals).Count -gt 0 -and @($entry.knownLimitations).Count -gt 0) {
    Add-ResultRow -Rows $rows -Criterion "Release non-goals/limitations register" -Status "PASS" -Evidence "Release line '$ReleaseLine' contains non-goals and known limitations."
  }
  else {
    Add-ResultRow -Rows $rows -Criterion "Release non-goals/limitations register" -Status "FAIL" -Evidence "Release line '$ReleaseLine' must define at least one non-goal and one known limitation."
  }
}

Write-Step "Validating evidence artifacts"
$acceptanceJson = $null
if (Test-Path -Path $resolvedAcceptancePath) {
  $acceptanceJson = Read-JsonFile -Path $resolvedAcceptancePath

  foreach ($criterionName in @($matrix.requiredMilestone1Criteria)) {
    $criterion = Get-CriterionStatus -AcceptanceJson $acceptanceJson -CriterionName ([string]$criterionName)
    if (-not [bool]$criterion.found) {
      Add-ResultRow -Rows $rows -Criterion "Milestone 1 criterion: $criterionName" -Status "FAIL" -Evidence "Criterion not found in acceptance artifact."
      continue
    }

    $status = [string]$criterion.status
    if ([string]::Equals($status, "PASS", [StringComparison]::OrdinalIgnoreCase)) {
      if ($criterionName -match '(?i)insertion|url bar' -and -not (Test-VerifiedInsertionEvidence -Evidence ([string]$criterion.evidence))) {
        Add-ResultRow -Rows $rows -Criterion "Milestone 1 criterion: $criterionName" -Status "FAIL" -Evidence "PASS evidence is dispatch-only or missing visible-result verification. $([string]$criterion.evidence)"
        continue
      }

      Add-ResultRow -Rows $rows -Criterion "Milestone 1 criterion: $criterionName" -Status "PASS" -Evidence ([string]$criterion.evidence)
      continue
    }

    if ([string]::Equals($status, "BLOCKED", [StringComparison]::OrdinalIgnoreCase)) {
      if ($EnforceReleaseEvidence) {
        Add-ResultRow -Rows $rows -Criterion "Milestone 1 criterion: $criterionName" -Status "FAIL" -Evidence "Blocked in strict mode. $([string]$criterion.evidence)"
      }
      else {
        Add-ResultRow -Rows $rows -Criterion "Milestone 1 criterion: $criterionName" -Status "BLOCKED" -Evidence ([string]$criterion.evidence)
      }
      continue
    }

    Add-ResultRow -Rows $rows -Criterion "Milestone 1 criterion: $criterionName" -Status "FAIL" -Evidence "Unexpected status '$status'. $([string]$criterion.evidence)"
  }
}
else {
  $status = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
  Add-ResultRow -Rows $rows -Criterion "Milestone 1 acceptance artifact" -Status $status -Evidence "Missing artifact: $resolvedAcceptancePath"
}

if (Test-Path -Path $resolvedReliabilityPath) {
  $reliabilityJson = Read-JsonFile -Path $resolvedReliabilityPath
  $failedRows = @($reliabilityJson | Where-Object { -not [bool]$_.success })
  if ($failedRows.Count -eq 0) {
    Add-ResultRow -Rows $rows -Criterion "Reliability evidence artifact" -Status "PASS" -Evidence "Milestone 2 reliability results are all successful."
  }
  else {
    Add-ResultRow -Rows $rows -Criterion "Reliability evidence artifact" -Status "FAIL" -Evidence "$($failedRows.Count) reliability rows reported failure."
  }
}
else {
  $status = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
  Add-ResultRow -Rows $rows -Criterion "Reliability evidence artifact" -Status $status -Evidence "Missing artifact: $resolvedReliabilityPath"
}

$requireInstallerEvidence = [bool]$matrix.releaseGatePolicy.requiresInstallerEvidence
if ($requireInstallerEvidence) {
  if (-not [string]::IsNullOrWhiteSpace($resolvedInstallerValidationSummaryPath) -and (Test-Path -Path $resolvedInstallerValidationSummaryPath)) {
    $installerSummary = Read-JsonFile -Path $resolvedInstallerValidationSummaryPath
    $requiredScenarioIds = @($matrix.releaseGatePolicy.requiredInstallerScenarioIds)
    if ($requiredScenarioIds.Count -eq 0) {
      $requiredScenarioIds = @("1", "2", "3", "4")
    }

    foreach ($scenarioId in $requiredScenarioIds) {
      $scenario = @($installerSummary.scenarioResults | Where-Object {
          [string]::Equals([string]$_.scenarioId, [string]$scenarioId, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)

      if ($scenario.Count -eq 0) {
        $status = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
        Add-ResultRow -Rows $rows -Criterion "Installer scenario: $scenarioId" -Status $status -Evidence "Scenario not present in installer summary."
        continue
      }

      $row = $scenario[0]
      if ([string]::Equals([string]$row.status, "PASS", [StringComparison]::OrdinalIgnoreCase)) {
        Add-ResultRow -Rows $rows -Criterion "Installer scenario: $scenarioId" -Status "PASS" -Evidence "PASS in $resolvedInstallerValidationSummaryPath"
      }
      else {
        $status = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
        Add-ResultRow -Rows $rows -Criterion "Installer scenario: $scenarioId" -Status $status -Evidence "Unexpected status '$([string]$row.status)'."
      }
    }

  }
  else {
    $status = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
    Add-ResultRow -Rows $rows -Criterion "Installer evidence artifact" -Status $status -Evidence "Missing installer scenario summary artifact."
  }
}
else {
  Add-ResultRow -Rows $rows -Criterion "Installer evidence gate" -Status "PASS" -Evidence "Skipped by release gate policy."
}

$requireWorkbenchAcceptanceEvidence = [bool]$matrix.releaseGatePolicy.requiresWorkbenchAcceptanceEvidence
if ($requireWorkbenchAcceptanceEvidence) {
  if (Test-Path -Path $resolvedWorkbenchAcceptancePath) {
    $workbenchJson = Read-JsonFile -Path $resolvedWorkbenchAcceptancePath
    $requiredWorkbenchCriteria = @($matrix.releaseGatePolicy.requiredWorkbenchCriteria)
    if ($requiredWorkbenchCriteria.Count -eq 0) {
      $requiredWorkbenchCriteria = @(
        "Workbench textbox flow (record -> stop -> transcribe)",
        "Workbench no global insertion side effect",
        "Workbench overlay suppressed"
      )
    }

    foreach ($criterionName in $requiredWorkbenchCriteria) {
      $criterion = @($workbenchJson.criteria | Where-Object {
          [string]::Equals([string]$_.criterion, [string]$criterionName, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)

      if ($criterion.Count -eq 0) {
        $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
        Add-ResultRow -Rows $rows -Criterion "Workbench acceptance: $criterionName" -Status $criterionStatus -Evidence "Criterion not found in workbench acceptance artifact."
        continue
      }

      $row = $criterion[0]
      if ([string]::Equals([string]$row.status, "PASS", [StringComparison]::OrdinalIgnoreCase)) {
        Add-ResultRow -Rows $rows -Criterion "Workbench acceptance: $criterionName" -Status "PASS" -Evidence ([string]$row.evidence)
      }
      else {
        $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
        Add-ResultRow -Rows $rows -Criterion "Workbench acceptance: $criterionName" -Status $criterionStatus -Evidence "Unexpected status '$([string]$row.status)'. $([string]$row.evidence)"
      }
    }
  }
  else {
    $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
    Add-ResultRow -Rows $rows -Criterion "Workbench acceptance evidence artifact" -Status $criterionStatus -Evidence "Missing artifact: $resolvedWorkbenchAcceptancePath"
  }
}
else {
  Add-ResultRow -Rows $rows -Criterion "Workbench acceptance gate" -Status "PASS" -Evidence "Skipped by release gate policy."
}

$requireGlobalToggleAcceptanceEvidence = [bool]$matrix.releaseGatePolicy.requiresGlobalToggleAcceptanceEvidence
if ($requireGlobalToggleAcceptanceEvidence) {
  if (Test-Path -Path $resolvedGlobalToggleAcceptancePath) {
    $globalToggleJson = Read-JsonFile -Path $resolvedGlobalToggleAcceptancePath
    $requiredGlobalToggleCriteria = @($matrix.releaseGatePolicy.requiredGlobalToggleCriteria)
    if ($requiredGlobalToggleCriteria.Count -eq 0) {
      $requiredGlobalToggleCriteria = @(
        "Global toggle: Notepad",
        "Global toggle: Sticky Notes",
        "Global toggle: Chrome URL bar",
        "Global toggle: Microsoft Word",
        "Global toggle: VS Code",
        "Global toggle: Windsurf IDE",
        "Global toggle: Antigravity IDE",
        "Global toggle: Windows Terminal",
        "Global toggle: Slack"
      )
    }

    foreach ($criterionName in $requiredGlobalToggleCriteria) {
      $criterion = @($globalToggleJson.criteria | Where-Object {
          [string]::Equals([string]$_.criterion, [string]$criterionName, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)

      if ($criterion.Count -eq 0) {
        $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
        Add-ResultRow -Rows $rows -Criterion "Global toggle acceptance: $criterionName" -Status $criterionStatus -Evidence "Criterion not found in global toggle acceptance artifact."
        continue
      }

      $row = $criterion[0]
      if ([string]::Equals([string]$row.status, "PASS", [StringComparison]::OrdinalIgnoreCase)) {
        if (-not (Test-VerifiedInsertionEvidence -Evidence ([string]$row.evidence))) {
          $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
          Add-ResultRow -Rows $rows -Criterion "Global toggle acceptance: $criterionName" -Status $criterionStatus -Evidence "PASS evidence is missing visible-result verification. $([string]$row.evidence)"
          continue
        }

        Add-ResultRow -Rows $rows -Criterion "Global toggle acceptance: $criterionName" -Status "PASS" -Evidence ([string]$row.evidence)
      }
      else {
        $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
        Add-ResultRow -Rows $rows -Criterion "Global toggle acceptance: $criterionName" -Status $criterionStatus -Evidence "Unexpected status '$([string]$row.status)'. $([string]$row.evidence)"
      }
    }
  }
  else {
    $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
    Add-ResultRow -Rows $rows -Criterion "Global toggle acceptance evidence artifact" -Status $criterionStatus -Evidence "Missing artifact: $resolvedGlobalToggleAcceptancePath"
  }
}
else {
  Add-ResultRow -Rows $rows -Criterion "Global toggle acceptance gate" -Status "PASS" -Evidence "Skipped by release gate policy."
}

$requireEnglishBaselineRegressionEvidence = [bool]$matrix.releaseGatePolicy.requiresEnglishBaselineRegressionEvidence
if ($requireEnglishBaselineRegressionEvidence) {
  if (Test-Path -Path $resolvedEnglishBaselineRegressionPath) {
    $englishBaselineJson = Read-JsonFile -Path $resolvedEnglishBaselineRegressionPath
    $requiredEnglishBaselineCriteria = @($matrix.releaseGatePolicy.requiredEnglishBaselineRegressionCriteria)
    if ($requiredEnglishBaselineCriteria.Count -eq 0) {
      $requiredEnglishBaselineCriteria = @(
        "English baseline: default model manifest remains English-only",
        "English baseline: legacy settings migration defaults to English",
        "English baseline: profile resolver defaults to English",
        "English baseline: benchmark English scope stays on English candidate set"
      )
    }

    foreach ($criterionName in $requiredEnglishBaselineCriteria) {
      $criterion = @($englishBaselineJson.criteria | Where-Object {
          [string]::Equals([string]$_.criterion, [string]$criterionName, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)

      if ($criterion.Count -eq 0) {
        $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
        Add-ResultRow -Rows $rows -Criterion "English baseline regression: $criterionName" -Status $criterionStatus -Evidence "Criterion not found in English baseline regression artifact."
        continue
      }

      $row = $criterion[0]
      if ([string]::Equals([string]$row.status, "PASS", [StringComparison]::OrdinalIgnoreCase)) {
        Add-ResultRow -Rows $rows -Criterion "English baseline regression: $criterionName" -Status "PASS" -Evidence ([string]$row.evidence)
      }
      else {
        $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
        Add-ResultRow -Rows $rows -Criterion "English baseline regression: $criterionName" -Status $criterionStatus -Evidence "Unexpected status '$([string]$row.status)'. $([string]$row.evidence)"
      }
    }
  }
  else {
    $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
    Add-ResultRow -Rows $rows -Criterion "English baseline regression artifact" -Status $criterionStatus -Evidence "Missing artifact: $resolvedEnglishBaselineRegressionPath"
  }
}
else {
  Add-ResultRow -Rows $rows -Criterion "English baseline regression gate" -Status "PASS" -Evidence "Skipped by release gate policy."
}

$requireMultilingualAcceptanceEvidence = [bool]$matrix.releaseGatePolicy.requiresMultilingualAcceptanceEvidence
if ($requireMultilingualAcceptanceEvidence) {
  if (Test-Path -Path $resolvedMultilingualAcceptancePath) {
    $multilingualJson = Read-JsonFile -Path $resolvedMultilingualAcceptancePath
    $requiredMultilingualCriteria = @($matrix.releaseGatePolicy.requiredMultilingualAcceptanceCriteria)
    if ($requiredMultilingualCriteria.Count -eq 0) {
      $requiredMultilingualCriteria = @(
        "Multilingual acceptance: global language selection persists",
        "Multilingual acceptance: profile language override persists and resolves correctly",
        "Multilingual acceptance: model/language compatibility handling is deterministic",
        "Multilingual acceptance: benchmark recommendation follows selected language scope",
        "Multilingual acceptance: English baseline regression remains PASS after multilingual flow"
      )
    }

    foreach ($criterionName in $requiredMultilingualCriteria) {
      $criterion = @($multilingualJson.criteria | Where-Object {
          [string]::Equals([string]$_.criterion, [string]$criterionName, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)

      if ($criterion.Count -eq 0) {
        $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
        Add-ResultRow -Rows $rows -Criterion "Multilingual acceptance: $criterionName" -Status $criterionStatus -Evidence "Criterion not found in multilingual acceptance artifact."
        continue
      }

      $row = $criterion[0]
      if ([string]::Equals([string]$row.status, "PASS", [StringComparison]::OrdinalIgnoreCase)) {
        Add-ResultRow -Rows $rows -Criterion "Multilingual acceptance: $criterionName" -Status "PASS" -Evidence ([string]$row.evidence)
      }
      else {
        $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
        Add-ResultRow -Rows $rows -Criterion "Multilingual acceptance: $criterionName" -Status $criterionStatus -Evidence "Unexpected status '$([string]$row.status)'. $([string]$row.evidence)"
      }
    }
  }
  else {
    $criterionStatus = if ($EnforceReleaseEvidence) { "FAIL" } else { "NOT_RUN" }
    Add-ResultRow -Rows $rows -Criterion "Multilingual acceptance evidence artifact" -Status $criterionStatus -Evidence "Missing artifact: $resolvedMultilingualAcceptancePath"
  }
}
else {
  Add-ResultRow -Rows $rows -Criterion "Multilingual acceptance gate" -Status "PASS" -Evidence "Skipped by release gate policy."
}

$rollbackCriteria = @($matrix.releaseGatePolicy.extensibilityRollbackCriteria)
if ($rollbackCriteria.Count -gt 0) {
  Add-ResultRow -Rows $rows -Criterion "Extensibility rollback criteria contract" -Status "PASS" -Evidence "$($rollbackCriteria.Count) rollback criterion/criteria defined in compatibility contract."
}
else {
  Add-ResultRow -Rows $rows -Criterion "Extensibility rollback criteria contract" -Status "FAIL" -Evidence "Compatibility contract must define extensibilityRollbackCriteria."
}

$manualRequirements = @($matrix.manualEvidenceRequirements)
if ($manualRequirements.Count -gt 0) {
  if (Test-Path -Path $resolvedManualEvidencePath) {
    $manualEvidence = Read-JsonFile -Path $resolvedManualEvidencePath
    foreach ($requirement in $manualRequirements) {
      $requiredKey = [string]$requirement.key
      $resultEntry = @($manualEvidence.results | Where-Object {
          [string]::Equals([string]$_.key, $requiredKey, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)

      if ($resultEntry.Count -eq 0) {
        $status = if ($EnforceReleaseEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
        Add-ResultRow -Rows $rows -Criterion "Manual app evidence: $requiredKey" -Status $status -Evidence "Required key is missing from manual evidence file."
        continue
      }

      $entry = $resultEntry[0]
      $entryStatus = [string]$entry.status
      if ([string]::Equals($entryStatus, "PASS", [StringComparison]::OrdinalIgnoreCase)) {
        if (-not (Test-VerifiedInsertionEvidence -Evidence ([string]$entry.evidence))) {
          $status = if ($EnforceReleaseEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
          Add-ResultRow -Rows $rows -Criterion "Manual app evidence: $requiredKey" -Status $status -Evidence "PASS evidence is missing visible-result verification. $([string]$entry.evidence)"
          continue
        }

        Add-ResultRow -Rows $rows -Criterion "Manual app evidence: $requiredKey" -Status "PASS" -Evidence ([string]$entry.evidence)
      }
      else {
        $status = if ($EnforceReleaseEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
        Add-ResultRow -Rows $rows -Criterion "Manual app evidence: $requiredKey" -Status $status -Evidence "Manual status '$entryStatus'. $([string]$entry.evidence)"
      }
    }
  }
  else {
    $status = if ($EnforceReleaseEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
    Add-ResultRow -Rows $rows -Criterion "Manual app matrix evidence" -Status $status -Evidence "Missing manual evidence file: $resolvedManualEvidencePath"
  }
}

if ($RequireElevatedEvidence) {
  if (Test-Path -Path $resolvedElevatedPath) {
    $elevatedJson = Read-JsonFile -Path $resolvedElevatedPath
    $requiredElevatedCriteria = @(
      "Elevated routing tests",
      "Installer/UIAccess packaging contract"
    )

    foreach ($criterionName in $requiredElevatedCriteria) {
      $criterionRow = @($elevatedJson | Where-Object {
          [string]::Equals([string]$_.criterion, $criterionName, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)

      if ($criterionRow.Count -eq 0) {
        Add-ResultRow -Rows $rows -Criterion "Elevated evidence: $criterionName" -Status "FAIL" -Evidence "Criterion missing in elevated evidence artifact."
        continue
      }

      $row = $criterionRow[0]
      if ([string]::Equals([string]$row.status, "PASS", [StringComparison]::OrdinalIgnoreCase)) {
        Add-ResultRow -Rows $rows -Criterion "Elevated evidence: $criterionName" -Status "PASS" -Evidence ([string]$row.evidence)
      }
      else {
        Add-ResultRow -Rows $rows -Criterion "Elevated evidence: $criterionName" -Status "FAIL" -Evidence "Unexpected status '$([string]$row.status)'. $([string]$row.evidence)"
      }
    }
  }
  else {
    Add-ResultRow -Rows $rows -Criterion "Elevated evidence artifact" -Status "FAIL" -Evidence "Missing artifact: $resolvedElevatedPath"
  }
}
else {
  Add-ResultRow -Rows $rows -Criterion "Elevated evidence gate" -Status "PASS" -Evidence "Skipped because elevated insertion is out of current release scope."
}

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "compatibility-gate-results.json"
$rows | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Compatibility Gate Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Release line: $ReleaseLine")
$reportLines.Add("- Strict release mode: $([bool]$EnforceReleaseEvidence)")
$reportLines.Add("- Require elevated evidence: $([bool]$RequireElevatedEvidence)")
$reportLines.Add("- Summary JSON: $summaryPath")
$reportLines.Add("")
$reportLines.Add("| Criterion | Status | Evidence |")
$reportLines.Add("|---|---|---|")

foreach ($row in $rows) {
  $safeEvidence = ([string]$row.evidence).Replace("|", "/")
  $reportLines.Add("| $([string]$row.criterion) | $([string]$row.status) | $safeEvidence |")
}

$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Step "Compatibility gate evaluation complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

$blockingRows = @($rows | Where-Object { [string]::Equals([string]$_.status, "FAIL", [StringComparison]::OrdinalIgnoreCase) })
if ($EnforceReleaseEvidence) {
  $blockingRows += @($rows | Where-Object {
      @("NOT_RUN", "MANUAL_REQUIRED", "BLOCKED") -contains ([string]$_.status)
    })
}

if ($blockingRows.Count -gt 0) {
  throw "Compatibility gate failed with $($blockingRows.Count) blocking row(s)."
}
