[CmdletBinding()]
param(
  [string]$OutputDirectory = "artifacts/browser-surface-acceptance",
  [string]$ReportPath = "docs/release/browser-surface-acceptance-report.md",
  [string]$ManualEvidencePath = "artifacts/browser-surface-acceptance/manual-browser-surface-results.json",
  [string]$TemplatePath = "docs/release/manual-browser-surface-results.template.json",
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

function Contains-VisibleResultProof {
  param([Parameter(Mandatory = $false)][string]$Evidence)

  if ([string]::IsNullOrWhiteSpace($Evidence)) {
    return $false
  }

  return $Evidence -match '(?i)(verified|human-confirmed|visible result|text appeared|appeared in|inserted phrase|final value|confirmed visible)'
}

function Contains-DeterministicReason {
  param([Parameter(Mandatory = $false)][string]$Evidence)

  if ([string]::IsNullOrWhiteSpace($Evidence)) {
    return $false
  }

  return $Evidence -match '(?i)(because|blocked by|due to|reserved|system menu|browser menu|focus drift|focus changed|leaked|uipi|admin|privilege|protected|non-target)'
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
    hotkeyOutcome = ""
    insertionOutcome = ""
    overlayOutcome = ""
    latencyNotes = ""
    evidence = ""
  }
  $ManualEvidence.results += $entry
  return $entry
}

function Resolve-AllowedValue {
  param(
    [Parameter(Mandatory = $true)][string]$Label,
    [Parameter(Mandatory = $false)][string]$ExistingValue,
    [Parameter(Mandatory = $true)][string[]]$AllowedValues,
    [Parameter(Mandatory = $true)][bool]$NonInteractiveMode
  )

  if (-not [string]::IsNullOrWhiteSpace($ExistingValue)) {
    $normalizedExisting = $ExistingValue.Trim().ToLowerInvariant()
    if ($AllowedValues -contains $normalizedExisting) {
      return $normalizedExisting
    }
  }

  if ($NonInteractiveMode) {
    throw "Missing required $Label in non-interactive mode."
  }

  while ($true) {
    $prompt = Read-Host "$Label [$($AllowedValues -join '/')]"
    if ([string]::IsNullOrWhiteSpace($prompt)) {
      continue
    }

    $normalizedPrompt = $prompt.Trim().ToLowerInvariant()
    if ($AllowedValues -contains $normalizedPrompt) {
      return $normalizedPrompt
    }

    Write-Host "Invalid value. Allowed: $($AllowedValues -join ', ')." -ForegroundColor Yellow
  }
}

function Resolve-FreeformTextValue {
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

function Build-EvidenceSummary {
  param([Parameter(Mandatory = $true)]$Entry)

  return "hotkey=$([string]$Entry.hotkeyOutcome); insertion=$([string]$Entry.insertionOutcome); overlay=$([string]$Entry.overlayOutcome); latency=$([string]$Entry.latencyNotes); $([string]$Entry.evidence)"
}

$allowedStatuses = @("pass", "fail", "manual_required")
$allowedHotkeyOutcomes = @("started", "reserved", "leaked", "focus-drifted")
$allowedInsertionOutcomes = @("paste", "typing", "blocked", "not-applicable")
$allowedOverlayOutcomes = @("visible", "hidden", "wrong-surface", "not-applicable")

$requiredBrowserSurfaceKeys = @(
  [PSCustomObject]@{ key = "global-toggle-chrome-url-bar"; criterion = "Browser surface: Chrome URL bar" },
  [PSCustomObject]@{ key = "global-toggle-google-search-box"; criterion = "Browser surface: Google search box" },
  [PSCustomObject]@{ key = "global-toggle-chatgpt-search-mode"; criterion = "Browser surface: ChatGPT Search mode" },
  [PSCustomObject]@{ key = "global-toggle-chatgpt-prompt"; criterion = "Browser surface: ChatGPT prompt" },
  [PSCustomObject]@{ key = "global-toggle-gemini-prompt"; criterion = "Browser surface: Gemini prompt" },
  [PSCustomObject]@{ key = "global-toggle-claude-prompt"; criterion = "Browser surface: Claude prompt" },
  [PSCustomObject]@{ key = "global-toggle-chrome-browser-chrome"; criterion = "Browser surface: Chrome browser chrome" },
  [PSCustomObject]@{ key = "global-toggle-edge-url-bar"; criterion = "Browser surface: Edge URL bar" },
  [PSCustomObject]@{ key = "global-toggle-edge-search-field"; criterion = "Browser surface: Edge search field" },
  [PSCustomObject]@{ key = "global-toggle-edge-browser-chrome"; criterion = "Browser surface: Edge browser chrome" },
  [PSCustomObject]@{ key = "global-toggle-windsurf-ide"; criterion = "Browser surface: Windsurf editor buffer" },
  [PSCustomObject]@{ key = "global-toggle-windsurf-terminal"; criterion = "Browser surface: Windsurf integrated terminal" },
  [PSCustomObject]@{ key = "global-toggle-windsurf-command-palette"; criterion = "Browser surface: Windsurf command palette or search" },
  [PSCustomObject]@{ key = "global-toggle-sticky-notes"; criterion = "Browser surface: Sticky Notes" },
  [PSCustomObject]@{ key = "global-toggle-antigravity-ide"; criterion = "Browser surface: Antigravity editor surface" }
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

Write-Step "Preparing browser surface evidence"
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

foreach ($required in $requiredBrowserSurfaceKeys) {
  $entry = Ensure-ManualResultEntry -ManualEvidence $manualEvidence -Key ([string]$required.key)
  $entry.status = Resolve-AllowedValue -Label "$([string]$required.criterion) status" -ExistingValue ([string]$entry.status) -AllowedValues $allowedStatuses -NonInteractiveMode:$NonInteractive
  $entry.hotkeyOutcome = Resolve-AllowedValue -Label "$([string]$required.criterion) hotkeyOutcome" -ExistingValue ([string]$entry.hotkeyOutcome) -AllowedValues $allowedHotkeyOutcomes -NonInteractiveMode:$NonInteractive
  $entry.insertionOutcome = Resolve-AllowedValue -Label "$([string]$required.criterion) insertionOutcome" -ExistingValue ([string]$entry.insertionOutcome) -AllowedValues $allowedInsertionOutcomes -NonInteractiveMode:$NonInteractive
  $entry.overlayOutcome = Resolve-AllowedValue -Label "$([string]$required.criterion) overlayOutcome" -ExistingValue ([string]$entry.overlayOutcome) -AllowedValues $allowedOverlayOutcomes -NonInteractiveMode:$NonInteractive
  $entry.latencyNotes = Resolve-FreeformTextValue -Label "$([string]$required.criterion) latencyNotes" -ExistingValue ([string]$entry.latencyNotes) -NonInteractiveMode:$NonInteractive
  $entry.evidence = Resolve-FreeformTextValue -Label "$([string]$required.criterion) evidence" -ExistingValue ([string]$entry.evidence) -NonInteractiveMode:$NonInteractive
}

$manualEvidence | ConvertTo-Json -Depth 8 | Set-Content -Path $resolvedManualEvidencePath -Encoding UTF8

Add-ResultRow -Rows $rows -Criterion "Browser surface evidence metadata" -Status "PASS" -Evidence "Executor '$resolvedExecutor' on machine '$resolvedMachine'."

foreach ($required in $requiredBrowserSurfaceKeys) {
  $entry = @($manualEvidence.results | Where-Object {
      [string]::Equals([string]$_.key, [string]$required.key, [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1)

  if ($entry.Count -eq 0) {
    $rowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
    Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $rowStatus -Evidence "Missing key '$([string]$required.key)' in manual evidence."
    continue
  }

  $result = $entry[0]
  $status = [string]$result.status
  $hotkeyOutcome = [string]$result.hotkeyOutcome
  $insertionOutcome = [string]$result.insertionOutcome
  $overlayOutcome = [string]$result.overlayOutcome
  $latencyNotes = [string]$result.latencyNotes
  $evidence = [string]$result.evidence
  $summary = Build-EvidenceSummary -Entry $result

  if ((Contains-PlaceholderValue -Value $hotkeyOutcome) `
      -or (Contains-PlaceholderValue -Value $insertionOutcome) `
      -or (Contains-PlaceholderValue -Value $overlayOutcome) `
      -or (Contains-PlaceholderValue -Value $latencyNotes) `
      -or (Contains-PlaceholderValue -Value $evidence)) {
    $rowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
    Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $rowStatus -Evidence "Evidence contains placeholder text. $summary"
    continue
  }

  if (($hotkeyOutcome -eq "started") -and (($insertionOutcome -eq "paste") -or ($insertionOutcome -eq "typing"))) {
    if (-not (Contains-VisibleResultProof -Evidence $evidence)) {
      $rowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
      Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $rowStatus -Evidence "Successful insertion rows must include visible-result proof. $summary"
      continue
    }
  }
  else {
    if (-not (Contains-DeterministicReason -Evidence $evidence)) {
      $rowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
      Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $rowStatus -Evidence "Non-start or blocked rows must include a deterministic reason. $summary"
      continue
    }
  }

  if ([string]::Equals($status, "pass", [StringComparison]::OrdinalIgnoreCase)) {
    Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status "PASS" -Evidence $summary
  }
  else {
    $rowStatus = if ($EnforceEvidence) { "FAIL" } else { "MANUAL_REQUIRED" }
    Add-ResultRow -Rows $rows -Criterion ([string]$required.criterion) -Status $rowStatus -Evidence "Manual status '$status'. $summary"
  }
}

if ($manualEvidenceFileCopied) {
  Add-ResultRow -Rows $rows -Criterion "Browser surface manual evidence file" -Status "PASS" -Evidence "Template created and populated at $resolvedManualEvidencePath."
}
else {
  Add-ResultRow -Rows $rows -Criterion "Browser surface manual evidence file" -Status "PASS" -Evidence "Found manual evidence file at $resolvedManualEvidencePath."
}

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "browser-surface-acceptance-results.json"
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
$reportLines.Add("# Browser Surface Acceptance Report")
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

Write-Step "Browser surface acceptance evaluation complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"

$blockingRows = @($rows | Where-Object { [string]::Equals([string]$_.status, "FAIL", [StringComparison]::OrdinalIgnoreCase) })
if ($EnforceEvidence) {
  $blockingRows += @($rows | Where-Object {
      @("MANUAL_REQUIRED", "NOT_RUN", "BLOCKED") -contains ([string]$_.status)
    })
}

if ($blockingRows.Count -gt 0) {
  throw "Browser surface acceptance gate failed with $($blockingRows.Count) blocking row(s)."
}
