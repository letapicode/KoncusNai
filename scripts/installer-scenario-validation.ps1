[CmdletBinding()]
param(
  [string]$BaseInstallerPath,
  [string]$UpgradeInstallerPath,
  [switch]$RunDynamicScenarios,
  [int]$InstallerIdleTimeoutSeconds = 180,
  [int]$BestEffortIdleTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
  param([Parameter(Mandatory = $true)][string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Condition {
  param(
    [Parameter(Mandatory = $true)][bool]$Condition,
    [Parameter(Mandatory = $true)][string]$Message
  )

  if (-not $Condition) {
    throw $Message
  }
}

function Resolve-RepoRoot {
  $scriptsRoot = $script:PSScriptRoot
  if ([string]::IsNullOrWhiteSpace($scriptsRoot)) {
    throw "Failed to resolve scripts directory."
  }

  return [IO.Path]::GetFullPath((Join-Path -Path $scriptsRoot -ChildPath ".."))
}

function Resolve-PathAgainstRepoRoot {
  param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$RepoRoot
  )

  if ([string]::IsNullOrWhiteSpace($InputPath)) {
    throw "InputPath must not be empty."
  }

  if ([IO.Path]::IsPathRooted($InputPath)) {
    return [IO.Path]::GetFullPath($InputPath)
  }

  return [IO.Path]::GetFullPath((Join-Path -Path $RepoRoot -ChildPath $InputPath))
}

function Test-IsAdministrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-CheckedCommand {
  param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $false)][string[]]$Arguments = @(),
    [Parameter(Mandatory = $false)][int[]]$SuccessExitCodes = @(0)
  )

  & $Executable @Arguments
  if ($SuccessExitCodes -notcontains $LASTEXITCODE) {
    throw "Command failed ($LASTEXITCODE): $Executable $($Arguments -join ' ')"
  }
}

function Wait-WindowsInstallerIdle {
  param(
    [Parameter(Mandatory = $false)][int]$TimeoutSeconds = 180,
    [Parameter(Mandatory = $false)][string]$Context = "installer operation"
  )

  if ($TimeoutSeconds -le 0) {
    throw "TimeoutSeconds must be greater than zero."
  }

  $stopwatch = [Diagnostics.Stopwatch]::StartNew()
  $nextStatusUpdateAtSeconds = 15
  while ($true) {
    $installerProcesses = @(Get-CimInstance Win32_Process -Filter "Name='msiexec.exe'" -ErrorAction SilentlyContinue | ForEach-Object {
      $commandLine = [string]$_.CommandLine
      $normalizedCommandLine = $commandLine.ToLowerInvariant().Trim()

      # Windows Installer service host processes (for example "msiexec.exe /V" or "/Embedding")
      # can remain alive when no install transaction is active.
      $isServiceHost = $normalizedCommandLine -match "msiexec(\.exe)?\s+\/v\b" -or
        $normalizedCommandLine -match "\/embedding\b"

      [PSCustomObject]@{
        ProcessId = [int]$_.ProcessId
        CommandLine = $commandLine
        IsServiceHost = $isServiceHost
      }
    })

    $activeInstallerProcesses = @($installerProcesses | Where-Object { -not [bool]$_.IsServiceHost })
    if ($activeInstallerProcesses.Count -eq 0) {
      return
    }

    $elapsed = [int][Math]::Floor($stopwatch.Elapsed.TotalSeconds)
    if ($elapsed -ge $nextStatusUpdateAtSeconds) {
      $processSummary = $activeInstallerProcesses |
        ForEach-Object { "pid=$($_.ProcessId) cmd='$($_.CommandLine)'" }
      Write-Host "Windows Installer busy during $Context; waiting... ($($processSummary -join ', '))" -ForegroundColor Yellow
      $nextStatusUpdateAtSeconds += 15
    }

    if ($stopwatch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
      $processDetails = $activeInstallerProcesses |
        ForEach-Object { "pid=$($_.ProcessId), cmd='$($_.CommandLine)'" }
      throw "Timed out waiting for Windows Installer to become idle during $Context after $TimeoutSeconds seconds. Active msiexec: $($processDetails -join '; ')"
    }

    Start-Sleep -Seconds 2
  }
}

function Convert-HexExitCodeToInt {
  param([Parameter(Mandatory = $true)][string]$HexText)

  $normalized = $HexText
  if ($normalized.StartsWith("0x", [StringComparison]::OrdinalIgnoreCase)) {
    $normalized = $normalized.Substring(2)
  }

  return [Convert]::ToInt32($normalized, 16)
}

function Wait-BurnLogCompletion {
  param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $false)][int]$TimeoutSeconds = 900
  )

  $stopwatch = [Diagnostics.Stopwatch]::StartNew()
  while ($true) {
    if (Test-Path -Path $LogPath) {
      $logContent = Get-Content -Path $LogPath -ErrorAction SilentlyContinue
      $applyMatch = @($logContent | Select-String -Pattern "i399:\s+Apply complete, result:\s+(0x[0-9A-Fa-f]+)" | Select-Object -Last 1)
      $exitMatch = @($logContent | Select-String -Pattern "i007:\s+Exit code:\s+(0x[0-9A-Fa-f]+)" | Select-Object -Last 1)

      if ($applyMatch.Count -gt 0 -and $exitMatch.Count -gt 0) {
        $applyHexMatch = [Regex]::Match($applyMatch[0].Line, "result:\s+(0x[0-9A-Fa-f]+)")
        $exitHexMatch = [Regex]::Match($exitMatch[0].Line, "Exit code:\s+(0x[0-9A-Fa-f]+)")
        if ($applyHexMatch.Success -and $exitHexMatch.Success) {
          $applyHex = $applyHexMatch.Groups[1].Value
          $exitHex = $exitHexMatch.Groups[1].Value
          return [PSCustomObject]@{
            ApplyCodeHex = $applyHex
            ApplyCodeDec = Convert-HexExitCodeToInt -HexText $applyHex
            ExitCodeHex = $exitHex
            ExitCodeDec = Convert-HexExitCodeToInt -HexText $exitHex
          }
        }
      }
    }

    if ($stopwatch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
      throw "Timed out waiting for Burn completion markers in log '$LogPath'."
    }

    Start-Sleep -Seconds 1
  }
}

function Resolve-InstallerType {
  param([Parameter(Mandatory = $true)][string]$InstallerPath)

  if ($InstallerPath.EndsWith(".msi", [StringComparison]::OrdinalIgnoreCase)) {
    return "msi"
  }

  if ($InstallerPath.EndsWith(".exe", [StringComparison]::OrdinalIgnoreCase)) {
    return "exe"
  }

  throw "Unsupported installer extension for '$InstallerPath'. Only .msi and .exe are supported."
}

function Invoke-MsiExec {
  param(
    [Parameter(Mandatory = $true)][string[]]$Arguments,
    [Parameter(Mandatory = $true)][string]$LogPath
  )

  $fullArguments = @($Arguments + @("/qn", "/norestart", "/L*V", $LogPath))
  Invoke-CheckedCommand -Executable "msiexec.exe" -Arguments $fullArguments -SuccessExitCodes @(0, 1641, 3010)
}

function Invoke-ExeInstaller {
  param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $false)][AllowEmptyCollection()][string[]]$Arguments = @(),
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $false)][int[]]$SuccessExitCodes = @(0, 1641, 3010)
  )

  $fullArguments = @($Arguments + @("/quiet", "/norestart", "/log", $LogPath))
  Invoke-CheckedCommand -Executable $InstallerPath -Arguments $fullArguments -SuccessExitCodes @(0, 1641, 3010)

  $burnResult = Wait-BurnLogCompletion -LogPath $LogPath
  if ($SuccessExitCodes -notcontains $burnResult.ApplyCodeDec) {
    throw "Burn apply result indicates failure ($($burnResult.ApplyCodeHex)) in log '$LogPath'."
  }

  if ($SuccessExitCodes -notcontains $burnResult.ExitCodeDec) {
    throw "Burn exit code indicates failure ($($burnResult.ExitCodeHex)) in log '$LogPath'."
  }
}

function Invoke-InstallerAction {
  param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][ValidateSet("Install", "Repair", "Uninstall")][string]$Action,
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $false)][switch]$SkipPreIdleWait,
    [Parameter(Mandatory = $false)][switch]$SkipPostIdleWait
  )

  $type = Resolve-InstallerType -InstallerPath $InstallerPath
  if (-not $SkipPreIdleWait) {
    Wait-WindowsInstallerIdle -TimeoutSeconds $InstallerIdleTimeoutSeconds -Context "$Action ($InstallerPath)"
  }
  switch ($type) {
    "msi" {
      switch ($Action) {
        "Install" { Invoke-MsiExec -Arguments @("/i", $InstallerPath, "STARTUP_ON_LOGIN=0") -LogPath $LogPath }
        "Repair" { Invoke-MsiExec -Arguments @("/fa", $InstallerPath) -LogPath $LogPath }
        "Uninstall" { Invoke-MsiExec -Arguments @("/x", $InstallerPath) -LogPath $LogPath }
      }
    }
    "exe" {
      switch ($Action) {
        "Install" { Invoke-ExeInstaller -InstallerPath $InstallerPath -LogPath $LogPath }
        "Repair" { Invoke-ExeInstaller -InstallerPath $InstallerPath -Arguments @("/repair") -LogPath $LogPath }
        "Uninstall" { Invoke-ExeInstaller -InstallerPath $InstallerPath -Arguments @("/uninstall") -LogPath $LogPath }
      }
    }
  }
  if (-not $SkipPostIdleWait) {
    Wait-WindowsInstallerIdle -TimeoutSeconds $InstallerIdleTimeoutSeconds -Context "post-$Action ($InstallerPath)"
  }
}

function Invoke-BestEffortUninstall {
  param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$LogPath
  )

  try {
    try {
      Wait-WindowsInstallerIdle -TimeoutSeconds $BestEffortIdleTimeoutSeconds -Context "best-effort pre-uninstall ($InstallerPath)"
    }
    catch {
      Write-Host "Best-effort cleanup skip for '$InstallerPath': $($_.Exception.Message)" -ForegroundColor Yellow
      return
    }

    Invoke-InstallerAction -InstallerPath $InstallerPath -Action "Uninstall" -LogPath $LogPath -SkipPreIdleWait -SkipPostIdleWait
  }
  catch {
    Write-Host "Best-effort cleanup uninstall failed for '$InstallerPath': $($_.Exception.Message)" -ForegroundColor Yellow
  }
}

function Invoke-StandardScenarioSet {
  param(
    [Parameter(Mandatory = $true)][string]$BaseInstaller,
    [Parameter(Mandatory = $false)][string]$UpgradeInstaller,
    [Parameter(Mandatory = $true)][string]$LogDirectory
  )

  $scenarioResults = New-Object System.Collections.Generic.List[object]

  Write-Step "Scenario 1: Fresh install"
  $scenario1Log = Join-Path -Path $LogDirectory -ChildPath "01-fresh-install.log"
  Invoke-InstallerAction -InstallerPath $BaseInstaller -Action "Install" -LogPath $scenario1Log
  $scenarioResults.Add([PSCustomObject]@{
      scenarioId = "1"
      scenarioName = "Fresh install"
      status = "PASS"
      logs = @($scenario1Log)
    })

  Write-Step "Scenario 2: Repair"
  $scenario2Log = Join-Path -Path $LogDirectory -ChildPath "02-repair.log"
  Invoke-InstallerAction -InstallerPath $BaseInstaller -Action "Repair" -LogPath $scenario2Log
  $scenarioResults.Add([PSCustomObject]@{
      scenarioId = "2"
      scenarioName = "Repair"
      status = "PASS"
      logs = @($scenario2Log)
    })

  if (-not [string]::IsNullOrWhiteSpace($UpgradeInstaller)) {
    Write-Step "Scenario 3: Upgrade install"
    $scenario3Log = Join-Path -Path $LogDirectory -ChildPath "03-upgrade.log"
    Invoke-InstallerAction -InstallerPath $UpgradeInstaller -Action "Install" -LogPath $scenario3Log
    $scenarioResults.Add([PSCustomObject]@{
        scenarioId = "3"
        scenarioName = "Upgrade install"
        status = "PASS"
        logs = @($scenario3Log)
      })
  }

  Write-Step "Scenario 4: Uninstall"
  $uninstallTarget = if (-not [string]::IsNullOrWhiteSpace($UpgradeInstaller)) { $UpgradeInstaller } else { $BaseInstaller }
  $scenario4Log = Join-Path -Path $LogDirectory -ChildPath "04-uninstall.log"
  Invoke-InstallerAction -InstallerPath $uninstallTarget -Action "Uninstall" -LogPath $scenario4Log
  $scenarioResults.Add([PSCustomObject]@{
      scenarioId = "4"
      scenarioName = "Uninstall"
      status = "PASS"
      logs = @($scenario4Log)
    })

  return $scenarioResults
}

$repoRoot = Resolve-RepoRoot
Set-Location $repoRoot

Write-Step "Running static installer validation prerequisites"
Invoke-CheckedCommand -Executable "powershell" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\packaging-smoke.ps1")
Invoke-CheckedCommand -Executable "powershell" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\installer-upgrade-compatibility.ps1")

if (-not $RunDynamicScenarios) {
  Write-Step "Dynamic installer scenarios not requested"
  Write-Host "Static installer checks passed." -ForegroundColor Green
  Write-Host "To run install/repair/upgrade/uninstall scenarios, use -RunDynamicScenarios -BaseInstallerPath (+ optional -UpgradeInstallerPath)."
  exit 0
}

Assert-Condition -Condition (Test-IsAdministrator) -Message "Dynamic installer scenarios require an elevated PowerShell session (Run as Administrator)."

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logDirectory = Join-Path -Path $repoRoot -ChildPath "artifacts/installer-validation/$timestamp"
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$scenarioResults = New-Object System.Collections.Generic.List[object]
$summaryInputs = [PSCustomObject]@{
  baseInstallerPath = $null
  upgradeInstallerPath = $null
}
$runMode = "standard"
Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace($BaseInstallerPath)) -Message "BaseInstallerPath is required when -RunDynamicScenarios is set."

  $baseInstaller = Resolve-PathAgainstRepoRoot -InputPath $BaseInstallerPath -RepoRoot $repoRoot
  Assert-Condition -Condition (Test-Path -Path $baseInstaller) -Message "Base installer not found: $baseInstaller"
  [void](Resolve-InstallerType -InstallerPath $baseInstaller)
  $summaryInputs.baseInstallerPath = $baseInstaller

  $upgradeInstaller = $null
  if (-not [string]::IsNullOrWhiteSpace($UpgradeInstallerPath)) {
    $upgradeInstaller = Resolve-PathAgainstRepoRoot -InputPath $UpgradeInstallerPath -RepoRoot $repoRoot
    Assert-Condition -Condition (Test-Path -Path $upgradeInstaller) -Message "Upgrade installer not found: $upgradeInstaller"
    [void](Resolve-InstallerType -InstallerPath $upgradeInstaller)
    $summaryInputs.upgradeInstallerPath = $upgradeInstaller
  }

  $scenarioResults = Invoke-StandardScenarioSet -BaseInstaller $baseInstaller -UpgradeInstaller $upgradeInstaller -LogDirectory $logDirectory

$summaryPath = Join-Path -Path $logDirectory -ChildPath "installer-scenario-summary.json"
$summary = [PSCustomObject]@{
  schemaVersion = 1
  generatedAtUtc = [DateTime]::UtcNow.ToString("o")
  runDynamicScenarios = $true
  runMode = $runMode
  installerIdleTimeoutSeconds = $InstallerIdleTimeoutSeconds
  bestEffortIdleTimeoutSeconds = $BestEffortIdleTimeoutSeconds
  inputs = $summaryInputs
  scenarioResults = $scenarioResults.ToArray()
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryPath -Encoding UTF8

Write-Step "Installer scenario validation completed"
Write-Host "Validation logs: $logDirectory" -ForegroundColor Green
Write-Host "Summary JSON: $summaryPath" -ForegroundColor Green
