param(
  [string]$OutputDirectory = "artifacts/milestone3",
  [string]$ReportPath = "docs/release/milestone-3-elevated-acceptance-report.md",
  [switch]$RunDynamicScenarios,
  [string]$PublishedAppDirectory = "artifacts/publish/DictateAnywhere.App"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
  param([string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Condition {
  param(
    [bool]$Condition,
    [string]$Message
  )

  if (-not $Condition) {
    throw $Message
  }
}

function Test-IsAdministrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = [Security.Principal.WindowsPrincipal]::new($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-CheckedCommand {
  param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [string[]]$Arguments = @()
  )

  & $Executable @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "Command failed ($LASTEXITCODE): $Executable $($Arguments -join ' ')"
  }
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
Set-Location -Path $repoRoot

$resolvedOutputDirectory = Join-Path -Path $repoRoot -ChildPath $OutputDirectory
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

Write-Step "Running static Milestone 3 acceptance checks"
Invoke-CheckedCommand -Executable "dotnet" -Arguments @(
  "test",
  "tests/DictateAnywhere.Insertion.Tests/DictateAnywhere.Insertion.Tests.csproj",
  "-c", "Release",
  "--nologo",
  "--filter", "FullyQualifiedName~BlockedByPrivilegeBoundary_WithElevatedInsertionEnabled|FullyQualifiedName~UiAccessHelperProcessBridgeTests")

Invoke-CheckedCommand -Executable "powershell" -Arguments @(
  "-NoProfile",
  "-ExecutionPolicy", "Bypass",
  "-File", ".\scripts\packaging-smoke.ps1")

$results = New-Object System.Collections.Generic.List[object]
$results.Add([PSCustomObject]@{
  criterion = "Elevated routing tests"
  status = "PASS"
  evidence = "Insertion elevated-routing and helper-policy tests passed."
})
$results.Add([PSCustomObject]@{
  criterion = "Installer/UIAccess packaging contract"
  status = "PASS"
  evidence = "packaging-smoke.ps1 validated ENABLE_UIACCESS wiring, helper manifest uiAccess=true, and signed-binary policy fields."
})

if ($RunDynamicScenarios) {
  Write-Step "Running dynamic elevated acceptance prerequisites"
  Assert-Condition -Condition (Test-IsAdministrator) -Message "Dynamic elevated acceptance requires an elevated PowerShell session."

  $publishDirectory = [IO.Path]::GetFullPath((Join-Path -Path $repoRoot -ChildPath $PublishedAppDirectory))
  $appExe = Join-Path -Path $publishDirectory -ChildPath "DictateAnywhere.App.exe"
  $helperExe = Join-Path -Path $publishDirectory -ChildPath "DictateAnywhere.UiAccessHelper.exe"

  Assert-Condition -Condition (Test-Path -Path $appExe) -Message "Published app executable not found: $appExe"
  Assert-Condition -Condition (Test-Path -Path $helperExe) -Message "Published UIAccess helper executable not found: $helperExe"

  $appSignature = Get-AuthenticodeSignature -FilePath $appExe
  $helperSignature = Get-AuthenticodeSignature -FilePath $helperExe

  Assert-Condition -Condition ($appSignature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) -Message "App executable must be signed for dynamic elevated validation."
  Assert-Condition -Condition ($helperSignature.Status -eq [System.Management.Automation.SignatureStatus]::Valid) -Message "UIAccess helper executable must be signed for dynamic elevated validation."

  $results.Add([PSCustomObject]@{
    criterion = "Dynamic elevated prerequisites"
    status = "PASS"
    evidence = "Admin session + signed app/helper binaries verified."
  })

  $results.Add([PSCustomObject]@{
    criterion = "Admin-app insertion dynamic execution"
    status = "MANUAL_REQUIRED"
    evidence = "Run manual admin-app insertion matrix (e.g., elevated Notepad/Terminal/Office) and attach logs/screenshots."
  })
}
else {
  $results.Add([PSCustomObject]@{
    criterion = "Dynamic elevated prerequisites"
    status = "NOT_RUN"
    evidence = "Run with -RunDynamicScenarios in elevated PowerShell to validate signed-binary prerequisites."
  })
}

$summaryPath = Join-Path -Path $resolvedOutputDirectory -ChildPath "milestone-3-elevated-acceptance-results.json"
$results | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath -Encoding UTF8

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add("# Milestone 3 Elevated Insertion Acceptance Report")
$reportLines.Add("")
$reportLines.Add("- Generated: $(Get-Date -Format o)")
$reportLines.Add("- Machine: $env:COMPUTERNAME")
$reportLines.Add("- User: $env:USERNAME")
$reportLines.Add("- Dynamic scenarios: $([bool]$RunDynamicScenarios)")
$reportLines.Add("- Summary JSON: $summaryPath")
$reportLines.Add("")
$reportLines.Add("## Acceptance Matrix")
$reportLines.Add("")
$reportLines.Add("| Criterion | Status | Evidence |")
$reportLines.Add("|---|---|---|")

foreach ($row in $results) {
  $reportLines.Add("| $([string]$row.criterion) | $([string]$row.status) | $([string]$row.evidence) |")
}

$resolvedReportPath = Join-Path -Path $repoRoot -ChildPath $ReportPath
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReportPath) -Force | Out-Null
$reportLines | Set-Content -Path $resolvedReportPath -Encoding UTF8

Write-Step "Milestone 3 elevated acceptance run complete"
Write-Host "Report: $resolvedReportPath"
Write-Host "JSON:   $summaryPath"
