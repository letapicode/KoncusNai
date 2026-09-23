[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$Version,
  [string]$ReleaseLine = "1.x",
  [string]$OutputDirectory = "artifacts/release-orchestration",
  [string]$ReportPath = "docs/release/release-orchestration-report.md",
  [switch]$NonInteractive,
  [switch]$ContinueOnError,
  [switch]$SkipQualitySuite,
  [switch]$SkipBuildInstaller,
  [switch]$SkipStaticPackagingChecks,
  [switch]$SkipDynamicInstallerValidation,
  [switch]$SkipWorkbenchAcceptance,
  [switch]$SkipGlobalToggleAcceptance,
  [switch]$SkipMultilingualAcceptance,
  [switch]$SkipCompatibilityGate,
  [switch]$SignInstallerArtifacts,
  [string]$SigningCertificateThumbprint,
  [string]$SigningTimestampUrl = "http://timestamp.digicert.com",
  [string]$SignToolPath = "signtool",
  [switch]$VerifyInstallerSignatures,
  [int]$SoakIterations = 10,
  [switch]$RunEndToEndSmoke,
  [int]$InstallerIdleTimeoutSeconds = 300,
  [int]$BestEffortIdleTimeoutSeconds = 20,
  [switch]$IncludeElevatedScope,
  [string]$SetupPath,
  [string]$InstallerValidationSummaryPath,
  [ValidateSet("PASS", "FAIL", "MANUAL_REQUIRED")]
  [string]$TerminalNonAdminStatus,
  [string]$TerminalNonAdminEvidence,
  [ValidateSet("PASS", "FAIL", "MANUAL_REQUIRED")]
  [string]$WorkbenchTextboxFlowStatus,
  [string]$WorkbenchTextboxFlowEvidence,
  [ValidateSet("PASS", "FAIL", "MANUAL_REQUIRED")]
  [string]$WorkbenchNoGlobalInsertionStatus,
  [string]$WorkbenchNoGlobalInsertionEvidence,
  [ValidateSet("PASS", "FAIL", "MANUAL_REQUIRED")]
  [string]$WorkbenchOverlayHiddenStatus,
  [string]$WorkbenchOverlayHiddenEvidence,
  [string]$EvidenceExecutor,
  [string]$EvidenceMachine
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
    [string[]]$Arguments = @(),
    [int[]]$SuccessExitCodes = @(0)
  )

  & $Executable @Arguments | Out-Host
  if ($SuccessExitCodes -notcontains $LASTEXITCODE) {
    throw "Command failed ($LASTEXITCODE): $Executable $($Arguments -join ' ')"
  }
}

function Invoke-PowerShellScript {
  param(
    [Parameter(Mandatory = $true)][string]$ScriptPath,
    [string[]]$Arguments = @(),
    [int[]]$SuccessExitCodes = @(0)
  )

  $pwshArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath) + $Arguments
  Invoke-ExternalCommand -Executable "powershell" -Arguments $pwshArgs -SuccessExitCodes $SuccessExitCodes
}

function Test-IsAdministrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
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

function Resolve-StatusValue {
  param(
    [Parameter(Mandatory = $true)][string]$Label,
    [Parameter(Mandatory = $false)][string]$ProvidedValue,
    [Parameter(Mandatory = $false)][string]$ExistingValue,
    [Parameter(Mandatory = $true)][bool]$NonInteractiveMode
  )

  $allowed = @("PASS", "FAIL", "MANUAL_REQUIRED")

  if (-not [string]::IsNullOrWhiteSpace($ProvidedValue)) {
    $normalized = $ProvidedValue.ToUpperInvariant()
    if ($allowed -contains $normalized) {
      return $normalized
    }

    throw "$Label value '$ProvidedValue' is invalid. Allowed values: $($allowed -join ', ')."
  }

  if (-not [string]::IsNullOrWhiteSpace($ExistingValue)) {
    $existingNormalized = $ExistingValue.ToUpperInvariant()
    if ($allowed -contains $existingNormalized) {
      return $existingNormalized
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
    [Parameter(Mandatory = $false)][string]$ProvidedValue,
    [Parameter(Mandatory = $false)][string]$ExistingValue,
    [Parameter(Mandatory = $false)][string]$FallbackValue,
    [Parameter(Mandatory = $true)][bool]$NonInteractiveMode
  )

  if (-not [string]::IsNullOrWhiteSpace($ProvidedValue)) {
    if (Contains-PlaceholderValue -Value $ProvidedValue) {
      throw "$Label contains placeholder text. Replace it with real evidence."
    }

    return $ProvidedValue
  }

  if (-not [string]::IsNullOrWhiteSpace($ExistingValue) -and -not (Contains-PlaceholderValue -Value $ExistingValue)) {
    return $ExistingValue
  }

  if (-not [string]::IsNullOrWhiteSpace($FallbackValue) -and -not (Contains-PlaceholderValue -Value $FallbackValue)) {
    return $FallbackValue
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

function Ensure-ManualEvidenceFile {
  param(
    [Parameter(Mandatory = $true)][string]$FilePath,
    [Parameter(Mandatory = $true)][string]$TemplatePath
  )

  if (Test-Path -Path $FilePath) {
    return
  }

  if (-not (Test-Path -Path $TemplatePath)) {
    throw "Template file not found: $TemplatePath"
  }

  New-Item -ItemType Directory -Path (Split-Path -Parent $FilePath) -Force | Out-Null
  Copy-Item -Path $TemplatePath -Destination $FilePath -Force
}

function Read-JsonFile {
  param([Parameter(Mandatory = $true)][string]$Path)
  return Get-Content -Path $Path -Raw | ConvertFrom-Json
}

function Write-JsonFile {
  param(
    [Parameter(Mandatory = $true)]$Object,
    [Parameter(Mandatory = $true)][string]$Path
  )

  $Object | ConvertTo-Json -Depth 10 | Set-Content -Path $Path -Encoding UTF8
}

function Ensure-ResultEntry {
  param(
    [Parameter(Mandatory = $true)]$EvidenceObject,
    [Parameter(Mandatory = $true)][string]$Key
  )

  $entry = @($EvidenceObject.results | Where-Object {
      [string]::Equals([string]$_.key, $Key, [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1)
  if ($entry.Count -gt 0) {
    return $entry[0]
  }

  $newEntry = [PSCustomObject]@{
    key = $Key
    status = "MANUAL_REQUIRED"
    evidence = ""
  }

  $EvidenceObject.results += $newEntry
  return $newEntry
}

function Add-StepResult {
  param(
    [Parameter(Mandatory = $true)]$Results,
    [Parameter(Mandatory = $true)][string]$Step,
    [Parameter(Mandatory = $true)][string]$Status,
    [Parameter(Mandatory = $true)][string]$Details,
    [Parameter(Mandatory = $true)][double]$DurationSeconds,
    [Parameter(Mandatory = $true)][bool]$Blocking,
    [string[]]$Artifacts = @()
  )

  $Results.Add([PSCustomObject]@{
      step = $Step
      status = $Status
      details = $Details
      durationSeconds = [Math]::Round($DurationSeconds, 2)
      blocking = $Blocking
      artifacts = @($Artifacts)
    })
}

function Invoke-OrchestrationStep {
  param(
    [Parameter(Mandatory = $true)][string]$StepName,
    [Parameter(Mandatory = $true)][scriptblock]$Action,
    [Parameter(Mandatory = $true)]$Results,
    [Parameter(Mandatory = $true)][bool]$Blocking,
    [Parameter(Mandatory = $true)][bool]$ContinueOnErrorFlag
  )

  Write-Step $StepName
  $stopwatch = [Diagnostics.Stopwatch]::StartNew()
  try {
    $result = & $Action
    $details = "Completed."
    $artifacts = @()
    if ($null -ne $result) {
      if ($result.PSObject.Properties.Match("details").Count -gt 0 -and -not [string]::IsNullOrWhiteSpace([string]$result.details)) {
        $details = [string]$result.details
      }

      if ($result.PSObject.Properties.Match("artifacts").Count -gt 0 -and $null -ne $result.artifacts) {
        $artifacts = @($result.artifacts | ForEach-Object { [string]$_ })
      }
    }

    Add-StepResult -Results $Results -Step $StepName -Status "PASS" -Details $details -DurationSeconds $stopwatch.Elapsed.TotalSeconds -Blocking:$Blocking -Artifacts $artifacts
    return
  }
  catch {
    $message = $_.Exception.Message
    Add-StepResult -Results $Results -Step $StepName -Status "FAIL" -Details $message -DurationSeconds $stopwatch.Elapsed.TotalSeconds -Blocking:$Blocking
    if ($Blocking -and -not $ContinueOnErrorFlag) {
      throw
    }
  }
}

$repoRoot = Resolve-RepoRoot
Set-Location -Path $repoRoot

$resolvedOutputDirectory = Resolve-RepoPath -PathValue $OutputDirectory -RepoRoot $repoRoot
$resolvedReportPath = Resolve-RepoPath -PathValue $ReportPath -RepoRoot $repoRoot
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null

$qualityOutputDirectory = Resolve-RepoPath -PathValue "artifacts/quality" -RepoRoot $repoRoot
$qualityReportPath = Resolve-RepoPath -PathValue "docs/release/quality-suite-report.md" -RepoRoot $repoRoot
$qualitySummaryPath = Join-Path -Path $qualityOutputDirectory -ChildPath "quality-suite-results.json"

$installerOutputDirectory = Resolve-RepoPath -PathValue "artifacts/installer" -RepoRoot $repoRoot
$defaultSetupPath = Join-Path -Path $installerOutputDirectory -ChildPath "KoncusNai-Setup-Small-$Version-x64.exe"
$artifactManifestPath = Join-Path -Path $installerOutputDirectory -ChildPath "KoncusNai-$Version-artifact-manifest.json"

$resolvedSetupPath = if ([string]::IsNullOrWhiteSpace($SetupPath)) { $defaultSetupPath } else { Resolve-RepoPath -PathValue $SetupPath -RepoRoot $repoRoot }

$manualMatrixTemplatePath = Resolve-RepoPath -PathValue "docs/release/manual-app-matrix-results.template.json" -RepoRoot $repoRoot
$manualMatrixPath = Resolve-RepoPath -PathValue "artifacts/compatibility/manual-app-matrix-results.json" -RepoRoot $repoRoot
$workbenchManualPath = Resolve-RepoPath -PathValue "artifacts/workbench-acceptance/manual-workbench-acceptance-results.json" -RepoRoot $repoRoot
$globalToggleManualPath = Resolve-RepoPath -PathValue "artifacts/global-toggle-acceptance/manual-global-toggle-results.json" -RepoRoot $repoRoot
$multilingualManualPath = Resolve-RepoPath -PathValue "artifacts/multilingual-acceptance/manual-multilingual-acceptance-results.json" -RepoRoot $repoRoot

$workbenchSummaryPath = Resolve-RepoPath -PathValue "artifacts/workbench-acceptance/workbench-acceptance-results.json" -RepoRoot $repoRoot
$workbenchReportPath = Resolve-RepoPath -PathValue "docs/release/workbench-acceptance-report.md" -RepoRoot $repoRoot
$globalToggleHotkeyValidationSummaryPath = Resolve-RepoPath -PathValue "artifacts/global-toggle-acceptance/global-toggle-hotkey-validation.json" -RepoRoot $repoRoot
$globalToggleHotkeyValidationReportPath = Resolve-RepoPath -PathValue "docs/release/global-toggle-hotkey-validation-report.md" -RepoRoot $repoRoot
$globalToggleSummaryPath = Resolve-RepoPath -PathValue "artifacts/global-toggle-acceptance/global-toggle-acceptance-results.json" -RepoRoot $repoRoot
$globalToggleReportPath = Resolve-RepoPath -PathValue "docs/release/global-toggle-acceptance-report.md" -RepoRoot $repoRoot
$multilingualSummaryPath = Resolve-RepoPath -PathValue "artifacts/multilingual-acceptance/multilingual-acceptance-results.json" -RepoRoot $repoRoot
$multilingualReportPath = Resolve-RepoPath -PathValue "docs/release/multilingual-acceptance-report.md" -RepoRoot $repoRoot
$compatibilitySummaryPath = Resolve-RepoPath -PathValue "artifacts/compatibility/compatibility-gate-results.json" -RepoRoot $repoRoot
$compatibilityReportPath = Resolve-RepoPath -PathValue "docs/release/compatibility-gate-report.md" -RepoRoot $repoRoot
$elevatedOutputDirectory = Resolve-RepoPath -PathValue "artifacts/milestone3" -RepoRoot $repoRoot
$elevatedReportPath = Resolve-RepoPath -PathValue "docs/release/milestone-3-elevated-acceptance-report.md" -RepoRoot $repoRoot
$elevatedSummaryPath = Join-Path -Path $elevatedOutputDirectory -ChildPath "milestone-3-elevated-acceptance-results.json"

$compatibilityMatrixPath = Resolve-RepoPath -PathValue "docs/release/compatibility-matrix.json" -RepoRoot $repoRoot
$compatibilityMatrix = Read-JsonFile -Path $compatibilityMatrixPath
$requireMultilingualAcceptanceEvidence = [bool]$compatibilityMatrix.releaseGatePolicy.requiresMultilingualAcceptanceEvidence

$resolvedEvidenceExecutor = Resolve-TextValue -Label "Evidence executor" -ProvidedValue $EvidenceExecutor -ExistingValue "" -FallbackValue $env:USERNAME -NonInteractiveMode:$NonInteractive
$resolvedEvidenceMachine = Resolve-TextValue -Label "Evidence machine" -ProvidedValue $EvidenceMachine -ExistingValue "" -FallbackValue $env:COMPUTERNAME -NonInteractiveMode:$NonInteractive

$stepResults = New-Object System.Collections.Generic.List[object]
$installerScenarioSummaryPath = $null

Write-Step "Preparing release orchestration"
Write-Host "Version: $Version"
Write-Host "Release line: $ReleaseLine"
Write-Host "Include elevated scope: $([bool]$IncludeElevatedScope)"
Write-Host "Non-interactive mode: $([bool]$NonInteractive)"

if (-not $SkipQualitySuite) {
  Invoke-OrchestrationStep -StepName "Quality suite" -Results $stepResults -Blocking:$true -ContinueOnErrorFlag:$ContinueOnError -Action {
    $qualityArgs = @(
      "-OutputDirectory", $qualityOutputDirectory,
      "-ReportPath", $qualityReportPath,
      "-SoakIterations", $SoakIterations.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    )
    if ($RunEndToEndSmoke) {
      $qualityArgs += "-RunEndToEndSmoke"
    }

    Invoke-PowerShellScript -ScriptPath ".\scripts\run-quality-suite.ps1" -Arguments $qualityArgs
    return [PSCustomObject]@{
      details = "Quality suite completed."
      artifacts = @($qualityReportPath, $qualitySummaryPath)
    }
  }
}
else {
  Add-StepResult -Results $stepResults -Step "Quality suite" -Status "SKIPPED" -Details "Skipped by switch." -DurationSeconds 0 -Blocking:$true
}

if (-not $SkipBuildInstaller) {
  Invoke-OrchestrationStep -StepName "Build installers" -Results $stepResults -Blocking:$true -ContinueOnErrorFlag:$ContinueOnError -Action {
    $buildArgs = @(
      "-Version", $Version,
      "-DistributionMode", "small"
    )
    if ($SignInstallerArtifacts) {
      if ([string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
        throw "SigningCertificateThumbprint is required when SignInstallerArtifacts is enabled."
      }

      $buildArgs += @(
        "-SignInstallerArtifacts",
        "-SigningCertificateThumbprint", $SigningCertificateThumbprint,
        "-SigningTimestampUrl", $SigningTimestampUrl,
        "-SignToolPath", $SignToolPath
      )
    }
    if ($VerifyInstallerSignatures) {
      $buildArgs += "-VerifyArtifactSignatures"
    }

    Invoke-PowerShellScript -ScriptPath ".\scripts\build-installer.ps1" -Arguments $buildArgs
    return [PSCustomObject]@{
      details = "Installer build completed."
      artifacts = @($defaultSetupPath, $artifactManifestPath)
    }
  }
}
else {
  Add-StepResult -Results $stepResults -Step "Build installer" -Status "SKIPPED" -Details "Skipped by switch." -DurationSeconds 0 -Blocking:$true -Artifacts @($resolvedSetupPath, $artifactManifestPath)
}

if (-not $SkipStaticPackagingChecks) {
  Invoke-OrchestrationStep -StepName "Static packaging/security checks" -Results $stepResults -Blocking:$true -ContinueOnErrorFlag:$ContinueOnError -Action {
    Invoke-PowerShellScript -ScriptPath ".\scripts\packaging-smoke.ps1"
    Invoke-PowerShellScript -ScriptPath ".\scripts\installer-upgrade-compatibility.ps1"
    Invoke-PowerShellScript -ScriptPath ".\scripts\security-compliance.ps1"
    return [PSCustomObject]@{
      details = "Packaging smoke, upgrade compatibility, and security checks completed."
      artifacts = @()
    }
  }
}
else {
  Add-StepResult -Results $stepResults -Step "Static packaging/security checks" -Status "SKIPPED" -Details "Skipped by switch." -DurationSeconds 0 -Blocking:$true
}

if (-not $SkipDynamicInstallerValidation) {
  Invoke-OrchestrationStep -StepName "Dynamic installer validation" -Results $stepResults -Blocking:$true -ContinueOnErrorFlag:$ContinueOnError -Action {
    if (-not (Test-IsAdministrator)) {
      throw "Dynamic installer validation requires an elevated PowerShell session."
    }

    if (-not (Test-Path -Path $resolvedSetupPath)) {
      throw "Setup EXE not found: $resolvedSetupPath"
    }

    $validationArgs = @(
      "-RunDynamicScenarios",
      "-BaseInstallerPath", $resolvedSetupPath,
      "-InstallerIdleTimeoutSeconds", $InstallerIdleTimeoutSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture),
      "-BestEffortIdleTimeoutSeconds", $BestEffortIdleTimeoutSeconds.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    )
    Invoke-PowerShellScript -ScriptPath ".\scripts\installer-scenario-validation.ps1" -Arguments $validationArgs

    $installerScenarioSummaryPath = Resolve-LatestInstallerValidationSummary -RepoRoot $repoRoot
    if ([string]::IsNullOrWhiteSpace($installerScenarioSummaryPath) -or -not (Test-Path -Path $installerScenarioSummaryPath)) {
      throw "Installer scenario summary was not generated."
    }

    return [PSCustomObject]@{
      details = "Dynamic installer scenarios completed."
      artifacts = @($installerScenarioSummaryPath)
    }
  }
}
else {
  if ([string]::IsNullOrWhiteSpace($InstallerValidationSummaryPath)) {
    $installerScenarioSummaryPath = Resolve-LatestInstallerValidationSummary -RepoRoot $repoRoot
  }
  else {
    $installerScenarioSummaryPath = Resolve-RepoPath -PathValue $InstallerValidationSummaryPath -RepoRoot $repoRoot
  }

  if ([string]::IsNullOrWhiteSpace($installerScenarioSummaryPath) -or -not (Test-Path -Path $installerScenarioSummaryPath)) {
    Add-StepResult -Results $stepResults -Step "Dynamic installer validation" -Status "FAIL" -Details "Skipped dynamic validation but no installer scenario summary is available." -DurationSeconds 0 -Blocking:$true
    if (-not $ContinueOnError) {
      throw "Missing installer scenario summary."
    }
  }
  else {
    Add-StepResult -Results $stepResults -Step "Dynamic installer validation" -Status "SKIPPED" -Details "Skipped by switch; using existing summary artifact." -DurationSeconds 0 -Blocking:$true -Artifacts @($installerScenarioSummaryPath)
  }
}

Invoke-OrchestrationStep -StepName "Capture manual app matrix evidence" -Results $stepResults -Blocking:$true -ContinueOnErrorFlag:$ContinueOnError -Action {
  Ensure-ManualEvidenceFile -FilePath $manualMatrixPath -TemplatePath $manualMatrixTemplatePath
  $manualMatrix = Read-JsonFile -Path $manualMatrixPath
  if ($null -eq $manualMatrix.results) {
    $manualMatrix.results = @()
  }

  $terminalEntry = Ensure-ResultEntry -EvidenceObject $manualMatrix -Key "windows-terminal-non-admin"
  $resolvedTerminalStatus = Resolve-StatusValue -Label "Manual terminal evidence status" -ProvidedValue $TerminalNonAdminStatus -ExistingValue ([string]$terminalEntry.status) -NonInteractiveMode:$NonInteractive
  $resolvedTerminalEvidence = Resolve-TextValue -Label "Manual terminal evidence text" -ProvidedValue $TerminalNonAdminEvidence -ExistingValue ([string]$terminalEntry.evidence) -FallbackValue "" -NonInteractiveMode:$NonInteractive

  $manualMatrix.generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
  $manualMatrix.executor = $resolvedEvidenceExecutor
  $manualMatrix.machine = $resolvedEvidenceMachine
  $terminalEntry.status = $resolvedTerminalStatus
  $terminalEntry.evidence = $resolvedTerminalEvidence

  Write-JsonFile -Object $manualMatrix -Path $manualMatrixPath
  return [PSCustomObject]@{
    details = "Manual compatibility evidence captured for windows-terminal-non-admin."
    artifacts = @($manualMatrixPath)
  }
}

if (-not $SkipWorkbenchAcceptance) {
  Invoke-OrchestrationStep -StepName "Workbench acceptance evidence" -Results $stepResults -Blocking:$true -ContinueOnErrorFlag:$ContinueOnError -Action {
    Ensure-ManualEvidenceFile -FilePath $workbenchManualPath -TemplatePath (Resolve-RepoPath -PathValue "docs/release/manual-workbench-acceptance-results.template.json" -RepoRoot $repoRoot)
    $workbenchManual = Read-JsonFile -Path $workbenchManualPath
    if ($null -eq $workbenchManual.results) {
      $workbenchManual.results = @()
    }

    $textboxEntry = Ensure-ResultEntry -EvidenceObject $workbenchManual -Key "workbench-record-stop-transcribe-textbox"
    $noGlobalInsertionEntry = Ensure-ResultEntry -EvidenceObject $workbenchManual -Key "workbench-no-global-insertion-side-effects"
    $overlayEntry = Ensure-ResultEntry -EvidenceObject $workbenchManual -Key "workbench-overlay-suppressed"

    $resolvedTextboxStatus = Resolve-StatusValue -Label "Workbench textbox flow status" -ProvidedValue $WorkbenchTextboxFlowStatus -ExistingValue ([string]$textboxEntry.status) -NonInteractiveMode:$NonInteractive
    $resolvedTextboxEvidence = Resolve-TextValue -Label "Workbench textbox flow evidence" -ProvidedValue $WorkbenchTextboxFlowEvidence -ExistingValue ([string]$textboxEntry.evidence) -FallbackValue "" -NonInteractiveMode:$NonInteractive
    $resolvedNoGlobalInsertionStatus = Resolve-StatusValue -Label "Workbench no-global-insertion status" -ProvidedValue $WorkbenchNoGlobalInsertionStatus -ExistingValue ([string]$noGlobalInsertionEntry.status) -NonInteractiveMode:$NonInteractive
    $resolvedNoGlobalInsertionEvidence = Resolve-TextValue -Label "Workbench no-global-insertion evidence" -ProvidedValue $WorkbenchNoGlobalInsertionEvidence -ExistingValue ([string]$noGlobalInsertionEntry.evidence) -FallbackValue "" -NonInteractiveMode:$NonInteractive
    $resolvedOverlayStatus = Resolve-StatusValue -Label "Workbench overlay hidden status" -ProvidedValue $WorkbenchOverlayHiddenStatus -ExistingValue ([string]$overlayEntry.status) -NonInteractiveMode:$NonInteractive
    $resolvedOverlayEvidence = Resolve-TextValue -Label "Workbench overlay hidden evidence" -ProvidedValue $WorkbenchOverlayHiddenEvidence -ExistingValue ([string]$overlayEntry.evidence) -FallbackValue "" -NonInteractiveMode:$NonInteractive

    $workbenchArgs = @(
      "-Executor", $resolvedEvidenceExecutor,
      "-Machine", $resolvedEvidenceMachine,
      "-TextboxFlowStatus", $resolvedTextboxStatus,
      "-TextboxFlowEvidence", $resolvedTextboxEvidence,
      "-NoGlobalInsertionStatus", $resolvedNoGlobalInsertionStatus,
      "-NoGlobalInsertionEvidence", $resolvedNoGlobalInsertionEvidence,
      "-OverlayHiddenStatus", $resolvedOverlayStatus,
      "-OverlayHiddenEvidence", $resolvedOverlayEvidence,
      "-EnforceEvidence"
    )

    Invoke-PowerShellScript -ScriptPath ".\scripts\run-workbench-acceptance.ps1" -Arguments $workbenchArgs
    return [PSCustomObject]@{
      details = "Workbench acceptance captured and validated."
      artifacts = @($workbenchManualPath, $workbenchReportPath, $workbenchSummaryPath)
    }
  }
}
else {
  Add-StepResult -Results $stepResults -Step "Workbench acceptance evidence" -Status "SKIPPED" -Details "Skipped by switch." -DurationSeconds 0 -Blocking:$true -Artifacts @($workbenchManualPath, $workbenchReportPath, $workbenchSummaryPath)
}

if (-not $SkipGlobalToggleAcceptance) {
  Invoke-OrchestrationStep -StepName "Global toggle acceptance evidence" -Results $stepResults -Blocking:$true -ContinueOnErrorFlag:$ContinueOnError -Action {
    Ensure-ManualEvidenceFile -FilePath $globalToggleManualPath -TemplatePath (Resolve-RepoPath -PathValue "docs/release/manual-global-toggle-results.template.json" -RepoRoot $repoRoot)

    $globalToggleArgs = @(
      "-Executor", $resolvedEvidenceExecutor,
      "-Machine", $resolvedEvidenceMachine,
      "-EnforceEvidence"
    )
    if ($NonInteractive) {
      $globalToggleArgs += "-NonInteractive"
    }

      Invoke-PowerShellScript -ScriptPath ".\scripts\run-global-toggle-acceptance.ps1" -Arguments $globalToggleArgs
      return [PSCustomObject]@{
        details = "Global toggle acceptance captured and validated."
        artifacts = @($globalToggleManualPath, $globalToggleHotkeyValidationReportPath, $globalToggleHotkeyValidationSummaryPath, $globalToggleReportPath, $globalToggleSummaryPath)
      }
    }
  }
  else {
    Add-StepResult -Results $stepResults -Step "Global toggle acceptance evidence" -Status "SKIPPED" -Details "Skipped by switch." -DurationSeconds 0 -Blocking:$true -Artifacts @($globalToggleManualPath, $globalToggleHotkeyValidationReportPath, $globalToggleHotkeyValidationSummaryPath, $globalToggleReportPath, $globalToggleSummaryPath)
  }

if ($requireMultilingualAcceptanceEvidence) {
  if (-not $SkipMultilingualAcceptance) {
    Invoke-OrchestrationStep -StepName "Multilingual acceptance evidence" -Results $stepResults -Blocking:$true -ContinueOnErrorFlag:$ContinueOnError -Action {
      Ensure-ManualEvidenceFile -FilePath $multilingualManualPath -TemplatePath (Resolve-RepoPath -PathValue "docs/release/manual-multilingual-acceptance-results.template.json" -RepoRoot $repoRoot)

      $multilingualArgs = @(
        "-Executor", $resolvedEvidenceExecutor,
        "-Machine", $resolvedEvidenceMachine,
        "-EnforceEvidence"
      )
      if ($NonInteractive) {
        $multilingualArgs += "-NonInteractive"
      }

      Invoke-PowerShellScript -ScriptPath ".\scripts\run-multilingual-acceptance.ps1" -Arguments $multilingualArgs
      return [PSCustomObject]@{
        details = "Multilingual acceptance captured and validated."
        artifacts = @($multilingualManualPath, $multilingualReportPath, $multilingualSummaryPath)
      }
    }
  }
  else {
    Add-StepResult -Results $stepResults -Step "Multilingual acceptance evidence" -Status "SKIPPED" -Details "Skipped by switch although required by compatibility policy." -DurationSeconds 0 -Blocking:$true -Artifacts @($multilingualManualPath, $multilingualReportPath, $multilingualSummaryPath)
  }
}
else {
  Add-StepResult -Results $stepResults -Step "Multilingual acceptance evidence" -Status "SKIPPED" -Details "Not required by compatibility policy for this release line." -DurationSeconds 0 -Blocking:$false -Artifacts @($multilingualManualPath, $multilingualReportPath, $multilingualSummaryPath)
}

$requireElevatedEvidence = $false
if ($IncludeElevatedScope) {
  Invoke-OrchestrationStep -StepName "Elevated scope acceptance" -Results $stepResults -Blocking:$true -ContinueOnErrorFlag:$ContinueOnError -Action {
    if (-not (Test-IsAdministrator)) {
      throw "IncludeElevatedScope requires an elevated PowerShell session."
    }

    if (-not $NonInteractive) {
      $confirmation = Read-Host "Run dynamic elevated acceptance now? [Y/N]"
      if (-not [string]::Equals($confirmation, "Y", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Elevated scope execution was cancelled by operator."
      }
    }

    $elevatedArgs = @(
      "-RunDynamicScenarios",
      "-OutputDirectory", $elevatedOutputDirectory,
      "-ReportPath", $elevatedReportPath
    )
    Invoke-PowerShellScript -ScriptPath ".\scripts\run-milestone3-elevated-acceptance.ps1" -Arguments $elevatedArgs
    $requireElevatedEvidence = $true
    return [PSCustomObject]@{
      details = "Elevated acceptance completed."
      artifacts = @($elevatedReportPath, $elevatedSummaryPath)
    }
  }
}
else {
  Add-StepResult -Results $stepResults -Step "Elevated scope acceptance" -Status "SKIPPED" -Details "Not requested." -DurationSeconds 0 -Blocking:$false
}

if (-not $SkipCompatibilityGate) {
  Invoke-OrchestrationStep -StepName "Strict compatibility gate" -Results $stepResults -Blocking:$true -ContinueOnErrorFlag:$ContinueOnError -Action {
    if ([string]::IsNullOrWhiteSpace($installerScenarioSummaryPath)) {
      $installerScenarioSummaryPath = Resolve-LatestInstallerValidationSummary -RepoRoot $repoRoot
    }
    if ([string]::IsNullOrWhiteSpace($installerScenarioSummaryPath) -or -not (Test-Path -Path $installerScenarioSummaryPath)) {
      throw "Installer scenario summary artifact is required before strict compatibility gating."
    }

    $compatArgs = @(
      "-EnforceReleaseEvidence",
      "-ReleaseLine", $ReleaseLine,
      "-InstallerValidationSummaryPath", $installerScenarioSummaryPath,
      "-WorkbenchAcceptanceResultsPath", $workbenchSummaryPath,
      "-GlobalToggleAcceptanceResultsPath", $globalToggleSummaryPath,
      "-MultilingualAcceptanceResultsPath", $multilingualSummaryPath,
      "-ManualEvidencePath", $manualMatrixPath
    )
    if ($requireElevatedEvidence) {
      $compatArgs += "-RequireElevatedEvidence"
    }

    Invoke-PowerShellScript -ScriptPath ".\scripts\run-compatibility-gate.ps1" -Arguments $compatArgs
    return [PSCustomObject]@{
      details = "Strict compatibility gate passed."
      artifacts = @($compatibilityReportPath, $compatibilitySummaryPath)
    }
  }
}
else {
  Add-StepResult -Results $stepResults -Step "Strict compatibility gate" -Status "SKIPPED" -Details "Skipped by switch." -DurationSeconds 0 -Blocking:$true -Artifacts @($compatibilityReportPath, $compatibilitySummaryPath)
}

if ([string]::IsNullOrWhiteSpace($installerScenarioSummaryPath)) {
  $dynamicStep = @($stepResults | Where-Object {
      [string]::Equals([string]$_.step, "Dynamic installer validation", [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1)
  if ($dynamicStep.Count -gt 0) {
    $dynamicArtifacts = @($dynamicStep[0].artifacts)
    if ($dynamicArtifacts.Count -gt 0) {
      $firstDynamicArtifact = [string]$dynamicArtifacts[0]
      if (-not [string]::IsNullOrWhiteSpace($firstDynamicArtifact) -and (Test-Path -Path $firstDynamicArtifact)) {
        $installerScenarioSummaryPath = $firstDynamicArtifact
      }
    }
  }
}

$failedBlocking = @($stepResults | Where-Object {
    [string]::Equals([string]$_.status, "FAIL", [StringComparison]::OrdinalIgnoreCase) -and [bool]$_.blocking
  })
$overallStatus = if ($failedBlocking.Count -eq 0) { "PASS" } else { "FAIL" }

$summaryJsonPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "release-orchestration-results.json"
$summary = [PSCustomObject]@{
  schemaVersion = 1
  generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
  version = $Version
  releaseLine = $ReleaseLine
  overallStatus = $overallStatus
  continueOnError = [bool]$ContinueOnError
  nonInteractive = [bool]$NonInteractive
  includeElevatedScope = [bool]$IncludeElevatedScope
  summaryArtifacts = [PSCustomObject]@{
    qualityReportPath = $qualityReportPath
    artifactManifestPath = $artifactManifestPath
    installerScenarioSummaryPath = $installerScenarioSummaryPath
    workbenchReportPath = $workbenchReportPath
    globalToggleReportPath = $globalToggleReportPath
    multilingualReportPath = $multilingualReportPath
    compatibilityReportPath = $compatibilityReportPath
    elevatedReportPath = $elevatedReportPath
  }
  steps = $stepResults
  blockingFailures = @($failedBlocking | ForEach-Object { [string]$_.step + ": " + [string]$_.details })
}
Write-JsonFile -Object $summary -Path $summaryJsonPath

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Release Orchestration Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Version: $Version")
$reportLines.Add("- Release line: $ReleaseLine")
$reportLines.Add("- Overall status: $overallStatus")
$reportLines.Add("- Continue on error: $([bool]$ContinueOnError)")
$reportLines.Add("- Include elevated scope: $([bool]$IncludeElevatedScope)")
$reportLines.Add("- Summary JSON: $summaryJsonPath")
$reportLines.Add("")
$reportLines.Add("## Step Results")
$reportLines.Add("")
$reportLines.Add("| Step | Status | Blocking | Duration (s) | Details |")
$reportLines.Add("|---|---|---|---:|---|")
foreach ($row in $stepResults) {
  $safeDetails = ([string]$row.details).Replace("|", "/")
  $reportLines.Add("| $([string]$row.step) | $([string]$row.status) | $([bool]$row.blocking) | $([double]$row.durationSeconds) | $safeDetails |")
}

$reportLines.Add("")
$reportLines.Add("## Artifacts")
$reportLines.Add("")
$allArtifacts = @($stepResults | ForEach-Object { @($_.artifacts) }) | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique
foreach ($artifact in $allArtifacts) {
  $reportLines.Add("- $artifact")
}

if ($failedBlocking.Count -gt 0) {
  $reportLines.Add("")
  $reportLines.Add("## Blocking Reasons")
  $reportLines.Add("")
  foreach ($failure in $failedBlocking) {
    $reportLines.Add("- $([string]$failure.step): $([string]$failure.details)")
  }
}

$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Step "Release orchestration completed"
Write-Host "Overall status: $overallStatus"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryJsonPath"

if ($failedBlocking.Count -gt 0) {
  throw "Release orchestration failed with $($failedBlocking.Count) blocking step(s)."
}
