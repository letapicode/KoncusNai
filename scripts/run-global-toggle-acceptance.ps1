[CmdletBinding()]
param(
  [string]$OutputDirectory = "artifacts/global-toggle-acceptance",
  [string]$ReportPath = "docs/release/global-toggle-acceptance-report.md",
  [string]$ManualEvidencePath = "artifacts/global-toggle-acceptance/manual-global-toggle-results.json",
  [string]$TemplatePath = "docs/release/manual-global-toggle-results.template.json",
  [string]$HotkeyValidationReportPath = "docs/release/global-toggle-hotkey-validation-report.md",
  [string]$HotkeyValidationSummaryPath = "artifacts/global-toggle-acceptance/global-toggle-hotkey-validation.json",
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

function Contains-InsertionMethodNote {
  param([Parameter(Mandatory = $false)][string]$Evidence)

  if ([string]::IsNullOrWhiteSpace($Evidence)) {
    return $false
  }

  return $Evidence -match '(?i)\b(paste|typing|blocked)\b'
}

function Contains-SingleInsertionNote {
  param([Parameter(Mandatory = $false)][string]$Evidence)

  if ([string]::IsNullOrWhiteSpace($Evidence)) {
    return $false
  }

  return $Evidence -match '(?i)(exactly once|single insertion|single attempt|one insertion|one attempt|no duplicate)'
}

function Contains-DeterministicBlockedReason {
  param([Parameter(Mandatory = $false)][string]$Evidence)

  if ([string]::IsNullOrWhiteSpace($Evidence)) {
    return $false
  }

  if (-not ($Evidence -match '(?i)\bblocked\b')) {
    return $true
  }

  return $Evidence -match '(?i)(because|blocked by|due to|secure|password|uipi|admin|privilege|protected|blacklist|reserved|system menu)'
}

function Contains-VisibleResultProof {
  param([Parameter(Mandatory = $false)][string]$Evidence)

  if ([string]::IsNullOrWhiteSpace($Evidence)) {
    return $false
  }

  return $Evidence -match '(?i)(verified|human-confirmed|visible result|text appeared|appeared in|inserted phrase|final value|confirmed visible)'
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

function Resolve-FreeformTextValue {
  param(
    [Parameter(Mandatory = $true)][string]$Label,
    [Parameter(Mandatory = $false)][string]$ExistingValue,
    [Parameter(Mandatory = $true)][bool]$NonInteractiveMode
  )

  if (-not [string]::IsNullOrWhiteSpace($ExistingValue) -and -not (Contains-PlaceholderValue -Value $ExistingValue)) {
    if ($NonInteractiveMode) {
      return $ExistingValue
    }
    if (Contains-InsertionMethodNote -Evidence $ExistingValue) {
      return $ExistingValue
    }
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

function Resolve-InsertionEvidenceValue {
  param(
    [Parameter(Mandatory = $true)][string]$Label,
    [Parameter(Mandatory = $false)][string]$ExistingValue,
    [Parameter(Mandatory = $true)][bool]$NonInteractiveMode
  )

  if (-not [string]::IsNullOrWhiteSpace($ExistingValue) -and -not (Contains-PlaceholderValue -Value $ExistingValue)) {
    if ((Contains-InsertionMethodNote -Evidence $ExistingValue) `
        -and (Contains-SingleInsertionNote -Evidence $ExistingValue) `
        -and (Contains-VisibleResultProof -Evidence $ExistingValue) `
        -and (Contains-DeterministicBlockedReason -Evidence $ExistingValue)) {
      return $ExistingValue
    }
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

    if (-not (Contains-InsertionMethodNote -Evidence $prompt)) {
      Write-Host "$Label must mention paste, typing, or blocked." -ForegroundColor Yellow
      continue
    }

    if (-not (Contains-SingleInsertionNote -Evidence $prompt)) {
      Write-Host "$Label must mention exactly once, single insertion, or equivalent wording." -ForegroundColor Yellow
      continue
    }

    if (-not (Contains-VisibleResultProof -Evidence $prompt)) {
      Write-Host "$Label must explain how the visible result was verified or confirmed." -ForegroundColor Yellow
      continue
    }

    if (-not (Contains-DeterministicBlockedReason -Evidence $prompt)) {
      Write-Host "$Label must include a deterministic blocked reason when blocked is reported." -ForegroundColor Yellow
      continue
    }

    return $prompt
  }
}

$requiredGlobalToggleKeys = @(
  [PSCustomObject]@{
    key = "global-toggle-notepad"
    criterion = "Global toggle: Notepad"
    passHint = "State whether paste, typing, or blocked was observed and whether insertion happened exactly once."
  },
  [PSCustomObject]@{
    key = "global-toggle-sticky-notes"
    criterion = "Global toggle: Sticky Notes"
    passHint = "State whether paste, typing, or blocked was observed and whether insertion happened exactly once."
  },
  [PSCustomObject]@{
    key = "global-toggle-chrome-url-bar"
    criterion = "Global toggle: Chrome URL bar"
    passHint = "State whether paste, typing, or blocked was observed and whether insertion happened exactly once."
  },
  [PSCustomObject]@{
    key = "global-toggle-microsoft-word"
    criterion = "Global toggle: Microsoft Word"
    passHint = "State whether paste, typing, or blocked was observed and whether insertion happened exactly once."
  },
  [PSCustomObject]@{
    key = "global-toggle-vs-code"
    criterion = "Global toggle: VS Code"
    passHint = "State whether paste, typing, or blocked was observed and whether insertion happened exactly once."
  },
  [PSCustomObject]@{
    key = "global-toggle-windsurf-ide"
    criterion = "Global toggle: Windsurf IDE"
    passHint = "State whether paste, typing, or blocked was observed and whether insertion happened exactly once."
  },
  [PSCustomObject]@{
    key = "global-toggle-antigravity-ide"
    criterion = "Global toggle: Antigravity IDE"
    passHint = "State whether paste, typing, or blocked was observed and whether insertion happened exactly once."
  },
  [PSCustomObject]@{
    key = "global-toggle-windows-terminal"
    criterion = "Global toggle: Windows Terminal"
    passHint = "State whether paste, typing, or blocked was observed and whether insertion happened exactly once."
  },
  [PSCustomObject]@{
    key = "global-toggle-slack"
    criterion = "Global toggle: Slack"
    passHint = "State whether paste, typing, or blocked was observed and whether insertion happened exactly once."
  }
)

$repoRoot = Resolve-RepoRoot
Set-Location -Path $repoRoot

$resolvedOutputDirectory = Resolve-RepoPath -PathValue $OutputDirectory -RepoRoot $repoRoot
$resolvedReportPath = Resolve-RepoPath -PathValue $ReportPath -RepoRoot $repoRoot
$resolvedManualEvidencePath = Resolve-RepoPath -PathValue $ManualEvidencePath -RepoRoot $repoRoot
$resolvedTemplatePath = Resolve-RepoPath -PathValue $TemplatePath -RepoRoot $repoRoot
$resolvedHotkeyValidationReportPath = Resolve-RepoPath -PathValue $HotkeyValidationReportPath -RepoRoot $repoRoot
$resolvedHotkeyValidationSummaryPath = Resolve-RepoPath -PathValue $HotkeyValidationSummaryPath -RepoRoot $repoRoot
$resolvedHotkeyValidationScriptPath = Resolve-RepoPath -PathValue "scripts/run-global-toggle-hotkey-validation.ps1" -RepoRoot $repoRoot

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedManualEvidencePath) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedHotkeyValidationReportPath) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedHotkeyValidationSummaryPath) -Force | Out-Null

$rows = New-Object System.Collections.Generic.List[object]
$manualEvidenceFileCopied = $false

Write-Step "Validating global toggle hotkey strategy"
& $resolvedHotkeyValidationScriptPath `
  -OutputDirectory $resolvedOutputDirectory `
  -ReportPath $resolvedHotkeyValidationReportPath `
  -SummaryPath $resolvedHotkeyValidationSummaryPath

$hotkeyValidation = Read-JsonFile -Path $resolvedHotkeyValidationSummaryPath
$hotkeyDecisionStatus = [string]$hotkeyValidation.decision.status
$hotkeyDecisionEvidence = [string]$hotkeyValidation.decision.evidence
$hotkeyActiveBinding = [string]$hotkeyValidation.decision.activeBindingDisplay

Add-ResultRow -Rows $rows -Criterion "Global toggle hotkey strategy decision" -Status $(if ($hotkeyDecisionStatus -eq "NO_GO") { "FAIL" } else { "PASS" }) -Evidence $hotkeyDecisionEvidence
Add-ResultRow -Rows $rows -Criterion "Global toggle active binding" -Status "PASS" -Evidence "Validated active binding for this machine: $hotkeyActiveBinding."

Write-Step "Preparing global toggle acceptance evidence"
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
  Resolve-FreeformTextValue -Label "Evidence executor" -ExistingValue "" -NonInteractiveMode:$false
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
  Resolve-FreeformTextValue -Label "Evidence machine" -ExistingValue "" -NonInteractiveMode:$false
}

$manualEvidence.executor = $resolvedExecutor
$manualEvidence.machine = $resolvedMachine
$manualEvidence.generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")

foreach ($required in $requiredGlobalToggleKeys) {
  $entry = Ensure-ManualResultEntry -ManualEvidence $manualEvidence -Key ([string]$required.key)
  $entry.status = Resolve-StatusValue -Label "$([string]$required.criterion) status" -ExistingValue ([string]$entry.status) -NonInteractiveMode:$NonInteractive
  $entry.evidence = Resolve-InsertionEvidenceValue -Label "$([string]$required.criterion) evidence" -ExistingValue ([string]$entry.evidence) -NonInteractiveMode:$NonInteractive
}

$manualEvidence | ConvertTo-Json -Depth 8 | Set-Content -Path $resolvedManualEvidencePath -Encoding UTF8

Add-ResultRow -Rows $rows -Criterion "Manual evidence metadata" -Status "PASS" -Evidence "Executor '$resolvedExecutor' on machine '$resolvedMachine'."

foreach ($required in $requiredGlobalToggleKeys) {
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

  if (-not (Contains-InsertionMethodNote -Evidence $entryEvidence)) {
    $criterionRowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
    Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $criterionRowStatus -Evidence "Evidence must mention paste, typing, or blocked. $entryEvidence"
    continue
  }

  if (-not (Contains-SingleInsertionNote -Evidence $entryEvidence)) {
    $criterionRowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
    Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $criterionRowStatus -Evidence "Evidence must state single insertion behavior (for example 'exactly once'). $entryEvidence"
    continue
  }

  if (-not (Contains-VisibleResultProof -Evidence $entryEvidence)) {
    $criterionRowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
    Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $criterionRowStatus -Evidence "Evidence must explain how the visible result was verified or confirmed. $entryEvidence"
    continue
  }

  if (-not (Contains-DeterministicBlockedReason -Evidence $entryEvidence)) {
    $criterionRowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
    Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $criterionRowStatus -Evidence "Blocked outcomes must include a deterministic reason. $entryEvidence"
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
  Add-ResultRow -Rows $rows -Criterion "Manual global toggle evidence file" -Status "PASS" -Evidence "Template created and populated at $resolvedManualEvidencePath."
}
else {
  Add-ResultRow -Rows $rows -Criterion "Manual global toggle evidence file" -Status "PASS" -Evidence "Found manual evidence file at $resolvedManualEvidencePath."
}

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "global-toggle-acceptance-results.json"
$summary = [PSCustomObject]@{
  schemaVersion = 1
  generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
  nonInteractive = [bool]$NonInteractive
  enforceEvidence = [bool]$EnforceEvidence
  hotkeyValidationReportPath = $resolvedHotkeyValidationReportPath
  hotkeyValidationSummaryPath = $resolvedHotkeyValidationSummaryPath
  manualEvidencePath = $resolvedManualEvidencePath
  criteria = $rows
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Global Toggle Acceptance Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Non-interactive: $([bool]$NonInteractive)")
$reportLines.Add("- Enforce evidence: $([bool]$EnforceEvidence)")
$reportLines.Add("- Hotkey validation report: $resolvedHotkeyValidationReportPath")
$reportLines.Add("- Hotkey validation JSON: $resolvedHotkeyValidationSummaryPath")
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

Write-Step "Global toggle acceptance evaluation complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

$blockingRows = @($rows | Where-Object { [string]::Equals([string]$_.status, "FAIL", [StringComparison]::OrdinalIgnoreCase) })
if ($EnforceEvidence) {
  $blockingRows += @($rows | Where-Object {
      @("MANUAL_REQUIRED", "NOT_RUN", "BLOCKED") -contains ([string]$_.status)
    })
}

if ($blockingRows.Count -gt 0) {
  throw "Global toggle acceptance gate failed with $($blockingRows.Count) blocking row(s)."
}
