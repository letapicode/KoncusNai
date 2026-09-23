[CmdletBinding()]
param(
  [string]$OutputDirectory = "artifacts/workbench-acceptance",
  [string]$ReportPath = "docs/release/workbench-acceptance-report.md",
  [string]$ManualEvidencePath = "artifacts/workbench-acceptance/manual-workbench-acceptance-results.json",
  [string]$TemplatePath = "docs/release/manual-workbench-acceptance-results.template.json",
  [string]$Executor,
  [string]$Machine,
  [ValidateSet("PASS", "FAIL", "MANUAL_REQUIRED")]
  [string]$TextboxFlowStatus,
  [string]$TextboxFlowEvidence,
  [ValidateSet("PASS", "FAIL", "MANUAL_REQUIRED")]
  [string]$NoGlobalInsertionStatus,
  [string]$NoGlobalInsertionEvidence,
  [ValidateSet("PASS", "FAIL", "MANUAL_REQUIRED")]
  [string]$OverlayHiddenStatus,
  [string]$OverlayHiddenEvidence,
  [switch]$EnforceEvidence
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

function Read-JsonFile {
  param([Parameter(Mandatory = $true)][string]$Path)
  return Get-Content -Path $Path -Raw | ConvertFrom-Json
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

function Contains-PlaceholderValue {
  param([Parameter(Mandatory = $false)][string]$Value)

  if ([string]::IsNullOrWhiteSpace($Value)) {
    return $true
  }

  $normalized = $Value.Trim().ToUpperInvariant()
  return $normalized.Contains("REPLACE_WITH_") -or
    $normalized.Contains("<REPLACE") -or
    $normalized.Contains("<TODO") -or
    $normalized.Contains("TBD")
}

function Ensure-ManualResultEntry {
  param(
    [Parameter(Mandatory = $true)]$ManualEvidence,
    [Parameter(Mandatory = $true)][string]$Key
  )

  $existing = @($ManualEvidence.results | Where-Object {
      [string]::Equals([string]$_.key, $Key, [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1)
  if ($existing.Count -gt 0) {
    return $existing[0]
  }

  $entry = [PSCustomObject]@{
    key = $Key
    status = "MANUAL_REQUIRED"
    evidence = ""
  }
  $ManualEvidence.results += $entry
  return $entry
}

$requiredWorkbenchKeys = @(
  [PSCustomObject]@{
    key = "workbench-record-stop-transcribe-textbox"
    criterion = "Workbench textbox flow (record -> stop -> transcribe)"
    passHint = "Transcribed text appears in textbox after Record/Stop."
  },
  [PSCustomObject]@{
    key = "workbench-no-global-insertion-side-effects"
    criterion = "Workbench no global insertion side effect"
    passHint = "No text is inserted into external apps during Workbench recording/transcribing."
  },
  [PSCustomObject]@{
    key = "workbench-overlay-suppressed"
    criterion = "Workbench overlay suppressed"
    passHint = "Overlay remains hidden for the full Workbench session."
  }
)

$repoRoot = Resolve-RepoRoot
Set-Location -Path $repoRoot

$resolvedOutputDirectory = Resolve-RepoPath -PathValue $OutputDirectory -RepoRoot $repoRoot
$resolvedReportPath = Resolve-RepoPath -PathValue $ReportPath -RepoRoot $repoRoot
$resolvedManualEvidencePath = Resolve-RepoPath -PathValue $ManualEvidencePath -RepoRoot $repoRoot
$resolvedTemplatePath = Resolve-RepoPath -PathValue $TemplatePath -RepoRoot $repoRoot

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedManualEvidencePath) -Force | Out-Null

$rows = New-Object System.Collections.Generic.List[object]
$manualEvidenceFileCopied = $false

Write-Step "Preparing workbench acceptance evidence"
if (-not (Test-Path -Path $resolvedManualEvidencePath)) {
  if (-not (Test-Path -Path $resolvedTemplatePath)) {
    throw "Manual evidence file not found and template is missing: $resolvedTemplatePath"
  }

  Copy-Item -Path $resolvedTemplatePath -Destination $resolvedManualEvidencePath -Force
  $manualEvidenceFileCopied = $true
}

$manualEvidence = $null
if (Test-Path -Path $resolvedManualEvidencePath) {
  $manualEvidence = Read-JsonFile -Path $resolvedManualEvidencePath

  $shouldPatchManualEvidence = $PSBoundParameters.ContainsKey("Executor") -or
    $PSBoundParameters.ContainsKey("Machine") -or
    $PSBoundParameters.ContainsKey("TextboxFlowStatus") -or
    $PSBoundParameters.ContainsKey("TextboxFlowEvidence") -or
    $PSBoundParameters.ContainsKey("NoGlobalInsertionStatus") -or
    $PSBoundParameters.ContainsKey("NoGlobalInsertionEvidence") -or
    $PSBoundParameters.ContainsKey("OverlayHiddenStatus") -or
    $PSBoundParameters.ContainsKey("OverlayHiddenEvidence")

  if ($shouldPatchManualEvidence) {
    if ($null -eq $manualEvidence.results) {
      $manualEvidence.results = @()
    }

    $textboxEntry = Ensure-ManualResultEntry -ManualEvidence $manualEvidence -Key "workbench-record-stop-transcribe-textbox"
    $noGlobalInsertionEntry = Ensure-ManualResultEntry -ManualEvidence $manualEvidence -Key "workbench-no-global-insertion-side-effects"
    $overlayEntry = Ensure-ManualResultEntry -ManualEvidence $manualEvidence -Key "workbench-overlay-suppressed"

    if ($PSBoundParameters.ContainsKey("Executor")) {
      $manualEvidence.executor = $Executor
    }
    if ($PSBoundParameters.ContainsKey("Machine")) {
      $manualEvidence.machine = $Machine
    }
    if ($PSBoundParameters.ContainsKey("TextboxFlowStatus")) {
      $textboxEntry.status = $TextboxFlowStatus
    }
    if ($PSBoundParameters.ContainsKey("TextboxFlowEvidence")) {
      $textboxEntry.evidence = $TextboxFlowEvidence
    }
    if ($PSBoundParameters.ContainsKey("NoGlobalInsertionStatus")) {
      $noGlobalInsertionEntry.status = $NoGlobalInsertionStatus
    }
    if ($PSBoundParameters.ContainsKey("NoGlobalInsertionEvidence")) {
      $noGlobalInsertionEntry.evidence = $NoGlobalInsertionEvidence
    }
    if ($PSBoundParameters.ContainsKey("OverlayHiddenStatus")) {
      $overlayEntry.status = $OverlayHiddenStatus
    }
    if ($PSBoundParameters.ContainsKey("OverlayHiddenEvidence")) {
      $overlayEntry.evidence = $OverlayHiddenEvidence
    }

    $manualEvidence.generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    $manualEvidence | ConvertTo-Json -Depth 8 | Set-Content -Path $resolvedManualEvidencePath -Encoding UTF8
  }

  $executor = [string]$manualEvidence.executor
  $machine = [string]$manualEvidence.machine
  if ((Contains-PlaceholderValue -Value $executor) -or (Contains-PlaceholderValue -Value $machine)) {
    $metadataRowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
    Add-ResultRow -Rows $rows -Criterion "Manual evidence metadata" -Status $metadataRowStatus -Evidence "executor/machine values still contain placeholders."
  }
  else {
    Add-ResultRow -Rows $rows -Criterion "Manual evidence metadata" -Status "PASS" -Evidence "Executor '$executor' on machine '$machine'."
  }

  foreach ($required in $requiredWorkbenchKeys) {
    $entry = @($manualEvidence.results | Where-Object {
        [string]::Equals([string]$_.key, [string]$required.key, [StringComparison]::OrdinalIgnoreCase)
      } | Select-Object -First 1)

    if ($entry.Count -eq 0) {
      $criterionRowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
      Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $criterionRowStatus -Evidence "Missing key '$([string]$required.key)' in manual evidence."
      continue
    }

    $result = $entry[0]
    $entryStatus = [string]$result.status
    $entryEvidence = [string]$result.evidence

    if (Contains-PlaceholderValue -Value $entryEvidence) {
      $criterionRowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
      Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $criterionRowStatus -Evidence "Evidence contains placeholder text. $([string]$required.passHint)"
      continue
    }

    if ([string]::Equals($entryStatus, "PASS", [StringComparison]::OrdinalIgnoreCase)) {
      Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status "PASS" -Evidence $entryEvidence
    }
    else {
      $criterionRowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
      Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $criterionRowStatus -Evidence "Manual status '$entryStatus'. $entryEvidence"
    }
  }

  if ($manualEvidenceFileCopied) {
    Add-ResultRow -Rows $rows -Criterion "Manual workbench evidence file" -Status "PASS" -Evidence "Template created and populated at $resolvedManualEvidencePath."
  }
  else {
    Add-ResultRow -Rows $rows -Criterion "Manual workbench evidence file" -Status "PASS" -Evidence "Found manual evidence file at $resolvedManualEvidencePath."
  }
}

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "workbench-acceptance-results.json"
$summary = [PSCustomObject]@{
  schemaVersion = 1
  generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
  enforceEvidence = [bool]$EnforceEvidence
  manualEvidencePath = $resolvedManualEvidencePath
  criteria = $rows
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Workbench Acceptance Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Enforce evidence: $([bool]$EnforceEvidence)")
$reportLines.Add("- Manual evidence: $resolvedManualEvidencePath")
$reportLines.Add("- Summary JSON: $summaryPath")
$reportLines.Add("")
$reportLines.Add("| Criterion | Status | Evidence |")
$reportLines.Add("|---|---|---|")
foreach ($row in $rows) {
  $safeEvidence = ([string]$row.evidence).Replace("|", "/")
  $reportLines.Add("| $([string]$row.criterion) | $([string]$row.status) | $safeEvidence |")
}
$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Step "Workbench acceptance evaluation complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

$blockingRows = @($rows | Where-Object { [string]::Equals([string]$_.status, "FAIL", [StringComparison]::OrdinalIgnoreCase) })
if ($EnforceEvidence) {
  $blockingRows += @($rows | Where-Object {
      @("MANUAL_REQUIRED", "NOT_RUN", "BLOCKED") -contains ([string]$_.status)
    })
}

if ($blockingRows.Count -gt 0) {
  throw "Workbench acceptance gate failed with $($blockingRows.Count) blocking row(s)."
}
