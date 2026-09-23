[CmdletBinding()]
param(
  [string]$OutputDirectory = "artifacts/global-toggle-acceptance",
  [string]$ReportPath = "docs/release/global-toggle-hotkey-validation-report.md",
  [string]$SummaryPath = "artifacts/global-toggle-acceptance/global-toggle-hotkey-validation.json",
  [string]$SpikeProjectPath = "tools/DictateAnywhere.Spikes/DictateAnywhere.Spikes.csproj",
  [int]$TimeoutSeconds = 6
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

function Get-VerificationMode {
  param([Parameter(Mandatory = $false)][string]$Details)

  if ([string]::IsNullOrWhiteSpace($Details)) {
    return "unknown"
  }

  if ($Details.IndexOf("self-triggered via SendInput", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    return "self-triggered"
  }

  if ($Details.IndexOf("synthetic WM_HOTKEY post", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    return "synthetic-post"
  }

  if ($Details.IndexOf("manual trigger", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    return "manual"
  }

  return "unknown"
}

function Invoke-HotkeySpike {
  param(
    [Parameter(Mandatory = $true)][string]$SpikeDllPath,
    [Parameter(Mandatory = $true)][string]$ModifierArgument,
    [Parameter(Mandatory = $true)][string]$BindingDisplay,
    [Parameter(Mandatory = $true)][int]$TimeoutSecondsValue
  )

  $output = & dotnet $SpikeDllPath hotkey --modifiers $ModifierArgument --virtual-key 32 --timeout-seconds $TimeoutSecondsValue --self-trigger true 2>&1
  $exitCode = $LASTEXITCODE
  $rawOutput = ($output | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
  $jsonLine = @($output | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_.StartsWith("{") -and $_.EndsWith("}") } | Select-Object -Last 1)
  if ($jsonLine.Count -eq 0) {
    throw "Hotkey spike for '$BindingDisplay' did not emit JSON output. Raw output: $rawOutput"
  }

  $parsed = $jsonLine[0] | ConvertFrom-Json
  $verificationMode = Get-VerificationMode -Details ([string]$parsed.details)

  return [PSCustomObject]@{
    bindingDisplay = $BindingDisplay
    modifiers = $ModifierArgument
    exitCode = $exitCode
    success = [bool]$parsed.success
    summary = [string]$parsed.summary
    details = [string]$parsed.details
    duration = [string]$parsed.duration
    verificationMode = $verificationMode
    rawOutput = $rawOutput
  }
}

$repoRoot = Resolve-RepoRoot
Set-Location -Path $repoRoot

$resolvedOutputDirectory = Resolve-RepoPath -PathValue $OutputDirectory -RepoRoot $repoRoot
$resolvedReportPath = Resolve-RepoPath -PathValue $ReportPath -RepoRoot $repoRoot
$resolvedSummaryPath = Resolve-RepoPath -PathValue $SummaryPath -RepoRoot $repoRoot
$resolvedSpikeProjectPath = Resolve-RepoPath -PathValue $SpikeProjectPath -RepoRoot $repoRoot
$resolvedSpikeProjectDirectory = Split-Path -Parent $resolvedSpikeProjectPath
$resolvedSpikeDllPath = Join-Path -Path $resolvedSpikeProjectDirectory -ChildPath "bin/Release/net8.0-windows/DictateAnywhere.Spikes.dll"

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedSummaryPath) -Force | Out-Null

$rows = New-Object System.Collections.Generic.List[object]

Write-Step "Building spike runner"
$buildOutput = & dotnet build $resolvedSpikeProjectPath -c Release --no-restore 2>&1
if ($LASTEXITCODE -ne 0) {
  $buildMessage = ($buildOutput | ForEach-Object { $_.ToString() }) -join [Environment]::NewLine
  throw "Spike runner build failed. $buildMessage"
}

if (-not (Test-Path -Path $resolvedSpikeDllPath)) {
  throw "Spike runner DLL not found after build: $resolvedSpikeDllPath"
}

Write-Step "Probing Alt + Space registration"
$preferred = Invoke-HotkeySpike `
  -SpikeDllPath $resolvedSpikeDllPath `
  -ModifierArgument "Alt" `
  -BindingDisplay "Alt + Space" `
  -TimeoutSecondsValue $TimeoutSeconds

Write-Step "Probing Win + Alt + Space fallback registration"
$fallback = Invoke-HotkeySpike `
  -SpikeDllPath $resolvedSpikeDllPath `
  -ModifierArgument "Windows,Alt" `
  -BindingDisplay "Win + Alt + Space" `
  -TimeoutSecondsValue $TimeoutSeconds

Add-ResultRow -Rows $rows -Criterion "Preferred hotkey registration" -Status $(if ($preferred.success) { "PASS" } else { "FAIL" }) -Evidence "$($preferred.bindingDisplay): $($preferred.summary) [$($preferred.verificationMode)] $($preferred.details)"
Add-ResultRow -Rows $rows -Criterion "Fallback hotkey registration" -Status $(if ($fallback.success) { "PASS" } else { "FAIL" }) -Evidence "$($fallback.bindingDisplay): $($fallback.summary) [$($fallback.verificationMode)] $($fallback.details)"

$decisionStatus = if ($preferred.success) {
  "GO"
}
elseif ($fallback.success) {
  "GO_WITH_FALLBACK"
}
else {
  "NO_GO"
}

$activeBindingDisplay = if ($preferred.success) {
  "Alt + Space"
}
elseif ($fallback.success) {
  "Win + Alt + Space"
}
else {
  "Unavailable"
}

$decisionEvidence = switch ($decisionStatus) {
  "GO" { "Preferred binding 'Alt + Space' registered on this machine. Active binding: Alt + Space. Manual certified-app matrix must still pass." }
  "GO_WITH_FALLBACK" { "Preferred binding failed, but fallback 'Win + Alt + Space' registered successfully. Active binding: Win + Alt + Space. Manual certified-app matrix must still pass with the fallback binding." }
  default { "Both preferred and fallback global-toggle bindings failed to register on this machine." }
}

Add-ResultRow -Rows $rows -Criterion "Global toggle hotkey strategy decision" -Status $(if ($decisionStatus -eq "NO_GO") { "FAIL" } else { "PASS" }) -Evidence $decisionEvidence

$summary = [PSCustomObject]@{
  schemaVersion = 1
  generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
  machine = $env:COMPUTERNAME
  user = $env:USERNAME
  osDescription = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
  timeoutSeconds = $TimeoutSeconds
  preferredBinding = $preferred
  fallbackBinding = $fallback
  decision = [PSCustomObject]@{
    status = $decisionStatus
    activeBindingDisplay = $activeBindingDisplay
    evidence = $decisionEvidence
  }
  criteria = $rows
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $resolvedSummaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Global Toggle Hotkey Validation Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- OS: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)")
$reportLines.Add("- Summary JSON: $resolvedSummaryPath")
$reportLines.Add("- Active binding decision: $decisionStatus ($activeBindingDisplay)")
$reportLines.Add("")
$reportLines.Add("| Criterion | Status | Evidence |")
$reportLines.Add("|---|---|---|")
foreach ($row in $rows) {
  $safeEvidence = ([string]$row.evidence).Replace("|", "/")
  $reportLines.Add("| $([string]$row.criterion) | $([string]$row.status) | $safeEvidence |")
}
$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Step "Global toggle hotkey validation complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $resolvedSummaryPath"

if ($decisionStatus -eq "NO_GO") {
  throw "Global toggle hotkey validation failed: no supported binding is available on this machine."
}
