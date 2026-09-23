[CmdletBinding()]
param(
  [string]$OutputDirectory = "artifacts/multilingual-acceptance",
  [string]$ReportPath = "docs/release/multilingual-acceptance-report.md",
  [string]$ManualEvidencePath = "artifacts/multilingual-acceptance/manual-multilingual-acceptance-results.json",
  [string]$TemplatePath = "docs/release/manual-multilingual-acceptance-results.template.json",
  [string]$Executor,
  [string]$Machine,
  [switch]$NonInteractive,
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

function Resolve-StatusValue {
  param(
    [Parameter(Mandatory = $true)][string]$Label,
    [Parameter(Mandatory = $false)][string]$ExistingValue,
    [Parameter(Mandatory = $true)][bool]$NonInteractiveMode
  )

  $allowed = @("PASS", "FAIL", "MANUAL_REQUIRED")

  if (-not [string]::IsNullOrWhiteSpace($ExistingValue)) {
    $existingNormalized = $ExistingValue.ToUpperInvariant()
    if ($allowed -contains $existingNormalized) {
      if ($NonInteractiveMode) {
        return $existingNormalized
      }
      if (-not [string]::Equals($existingNormalized, "MANUAL_REQUIRED", [StringComparison]::OrdinalIgnoreCase)) {
        return $existingNormalized
      }
    }
  }

  if ($NonInteractiveMode) {
    throw "Missing required $Label in non-interactive mode."
  }

  while ($true) {
    $prompt = Read-Host "$Label [PASS/FAIL/MANUAL_REQUIRED]"
    if ([string]::IsNullOrWhiteSpace($prompt)) {
      continue
    }

    $normalizedPrompt = $prompt.ToUpperInvariant()
    if ($allowed -contains $normalizedPrompt) {
      return $normalizedPrompt
    }

    Write-Host "Invalid value. Allowed: PASS, FAIL, MANUAL_REQUIRED." -ForegroundColor Yellow
  }
}

function Resolve-TextValue {
  param(
    [Parameter(Mandatory = $true)][string]$Label,
    [Parameter(Mandatory = $false)][string]$ExistingValue,
    [Parameter(Mandatory = $true)][bool]$NonInteractiveMode
  )

  if (-not [string]::IsNullOrWhiteSpace($ExistingValue) -and -not (Contains-PlaceholderValue -Value $ExistingValue)) {
    return $ExistingValue
  }

  if ($NonInteractiveMode) {
    throw "Missing required $Label in non-interactive mode."
  }

  while ($true) {
    $prompt = Read-Host "$Label"
    if ([string]::IsNullOrWhiteSpace($prompt)) {
      continue
    }

    if (Contains-PlaceholderValue -Value $prompt) {
      Write-Host "$Label cannot contain placeholder text." -ForegroundColor Yellow
      continue
    }

    return $prompt
  }
}

$requiredMultilingualKeys = @(
  [PSCustomObject]@{
    key = "multilingual-global-language-selection"
    criterion = "Multilingual acceptance: global language selection persists"
    passHint = "Global transcription language is retained after save/restart."
  },
  [PSCustomObject]@{
    key = "multilingual-profile-language-override"
    criterion = "Multilingual acceptance: profile language override persists and resolves correctly"
    passHint = "Profile override survives save/restart and effective language resolution is correct."
  },
  [PSCustomObject]@{
    key = "multilingual-model-language-compatibility"
    criterion = "Multilingual acceptance: model/language compatibility handling is deterministic"
    passHint = "Unsupported model/language pair is blocked or falls back deterministically with explicit status."
  },
  [PSCustomObject]@{
    key = "multilingual-benchmark-language-scope"
    criterion = "Multilingual acceptance: benchmark recommendation follows selected language scope"
    passHint = "Benchmark candidate set and recommendation notes align with requested language scope."
  },
  [PSCustomObject]@{
    key = "multilingual-english-baseline-regression-after-multilingual-flow"
    criterion = "Multilingual acceptance: English baseline regression remains PASS after multilingual flow"
    passHint = "English baseline regression is rerun and remains PASS."
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

Write-Step "Preparing multilingual acceptance evidence"
if (-not (Test-Path -Path $resolvedManualEvidencePath)) {
  if (-not (Test-Path -Path $resolvedTemplatePath)) {
    throw "Manual evidence file not found and template is missing: $resolvedTemplatePath"
  }

  Copy-Item -Path $resolvedTemplatePath -Destination $resolvedManualEvidencePath -Force
  $manualEvidenceFileCopied = $true
}

$manualEvidence = Read-JsonFile -Path $resolvedManualEvidencePath
if ($null -eq $manualEvidence.results) {
  $manualEvidence.results = @()
}

$resolvedExecutor = if ($PSBoundParameters.ContainsKey("Executor")) {
  $Executor
}
elseif (-not (Contains-PlaceholderValue -Value ([string]$manualEvidence.executor))) {
  [string]$manualEvidence.executor
}
elseif ($NonInteractive) {
  throw "Missing required executor in non-interactive mode."
}
else {
  Resolve-TextValue -Label "Evidence executor" -ExistingValue "" -NonInteractiveMode:$false
}

$resolvedMachine = if ($PSBoundParameters.ContainsKey("Machine")) {
  $Machine
}
elseif (-not (Contains-PlaceholderValue -Value ([string]$manualEvidence.machine))) {
  [string]$manualEvidence.machine
}
elseif ($NonInteractive) {
  throw "Missing required machine in non-interactive mode."
}
else {
  Resolve-TextValue -Label "Evidence machine" -ExistingValue "" -NonInteractiveMode:$false
}

$manualEvidence.executor = $resolvedExecutor
$manualEvidence.machine = $resolvedMachine
$manualEvidence.generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")

foreach ($required in $requiredMultilingualKeys) {
  $entry = Ensure-ManualResultEntry -ManualEvidence $manualEvidence -Key ([string]$required.key)
  $entry.status = Resolve-StatusValue -Label "$([string]$required.criterion) status" -ExistingValue ([string]$entry.status) -NonInteractiveMode:$NonInteractive
  $entry.evidence = Resolve-TextValue -Label "$([string]$required.criterion) evidence" -ExistingValue ([string]$entry.evidence) -NonInteractiveMode:$NonInteractive
}

$manualEvidence | ConvertTo-Json -Depth 8 | Set-Content -Path $resolvedManualEvidencePath -Encoding UTF8

Add-ResultRow -Rows $rows -Criterion "Manual evidence metadata" -Status "PASS" -Evidence "Executor '$resolvedExecutor' on machine '$resolvedMachine'."

foreach ($required in $requiredMultilingualKeys) {
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
  Add-ResultRow -Rows $rows -Criterion "Manual multilingual evidence file" -Status "PASS" -Evidence "Template created and populated at $resolvedManualEvidencePath."
}
else {
  Add-ResultRow -Rows $rows -Criterion "Manual multilingual evidence file" -Status "PASS" -Evidence "Found manual evidence file at $resolvedManualEvidencePath."
}

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "multilingual-acceptance-results.json"
$summary = [PSCustomObject]@{
  schemaVersion = 1
  generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
  nonInteractive = [bool]$NonInteractive
  enforceEvidence = [bool]$EnforceEvidence
  manualEvidencePath = $resolvedManualEvidencePath
  criteria = $rows
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Multilingual Acceptance Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Non-interactive: $([bool]$NonInteractive)")
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

Write-Step "Multilingual acceptance evaluation complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

$blockingRows = @($rows | Where-Object { [string]::Equals([string]$_.status, "FAIL", [StringComparison]::OrdinalIgnoreCase) })
if ($EnforceEvidence) {
  $blockingRows += @($rows | Where-Object {
      @("MANUAL_REQUIRED", "NOT_RUN", "BLOCKED") -contains ([string]$_.status)
    })
}

if ($blockingRows.Count -gt 0) {
  throw "Multilingual acceptance gate failed with $($blockingRows.Count) blocking row(s)."
}
