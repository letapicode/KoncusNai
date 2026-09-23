[CmdletBinding()]
param(
  [switch]$SkipPrerequisites,
  [switch]$SkipRestore,
  [switch]$SkipBuild,
  [switch]$SkipTests,
  [switch]$SkipPackagingSmoke,
  [switch]$SkipInstallerUpgradeCompatibility,
  [switch]$SkipSecurityCompliance,
  [switch]$InstallLocalModelRuntime,
  [switch]$InstallWixToolset,
  [switch]$ForceDotnetInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
  param([Parameter(Mandatory = $true)][string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-CommandExists {
  param([Parameter(Mandatory = $true)][string]$CommandName)
  return $null -ne (Get-Command -Name $CommandName -ErrorAction SilentlyContinue)
}

function Invoke-CheckedCommand {
  param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $false)][string[]]$Arguments = @()
  )

  if (-not (Test-Path $Executable) -and -not (Test-CommandExists $Executable)) {
    throw "Executable not found: $Executable"
  }

  & $Executable @Arguments
  if ($LASTEXITCODE -ne 0) {
    $joined = $Arguments -join " "
    throw "Command failed ($LASTEXITCODE): $Executable $joined"
  }
}

function Get-DotnetExecutable {
  if (Test-CommandExists "dotnet") {
    return "dotnet"
  }

  $dotnetPath = Join-Path $env:ProgramFiles "dotnet\dotnet.exe"
  if (Test-Path $dotnetPath) {
    return $dotnetPath
  }

  return $null
}

function Ensure-DotnetSdk {
  param(
    [Parameter(Mandatory = $true)][int]$RequiredMajorVersion
  )

  $dotnetExe = Get-DotnetExecutable
  if ($null -ne $dotnetExe -and -not $ForceDotnetInstall) {
    $sdkLines = & $dotnetExe --list-sdks 2>$null
    if ($LASTEXITCODE -eq 0) {
      foreach ($line in $sdkLines) {
        if ($line -match "^(\d+)\.") {
          $major = [int]$Matches[1]
          if ($major -eq $RequiredMajorVersion) {
            Write-Host ".NET SDK $RequiredMajorVersion already installed."
            return $dotnetExe
          }
        }
      }
    }
  }

  if (-not (Test-CommandExists "winget")) {
    throw "winget is required to auto-install .NET SDK $RequiredMajorVersion. Install winget or install .NET SDK manually, then re-run setup.ps1."
  }

  Write-Step "Installing .NET SDK $RequiredMajorVersion via winget"
  Invoke-CheckedCommand -Executable "winget" -Arguments @(
    "install",
    "--id", "Microsoft.DotNet.SDK.$RequiredMajorVersion",
    "--exact",
    "--accept-package-agreements",
    "--accept-source-agreements",
    "--silent"
  )

  $dotnetExe = Get-DotnetExecutable
  if ($null -eq $dotnetExe) {
    throw ".NET SDK installation completed but dotnet executable was not found."
  }

  $sdkLines = & $dotnetExe --list-sdks
  if ($LASTEXITCODE -ne 0 -or -not ($sdkLines | Where-Object { $_ -match "^$RequiredMajorVersion\." })) {
    throw ".NET SDK $RequiredMajorVersion is still unavailable after installation."
  }

  return $dotnetExe
}

function Ensure-WixToolset {
  if (Test-CommandExists "wix") {
    Write-Host "WiX Toolset already installed."
    return
  }

  if (-not (Test-CommandExists "winget")) {
    throw "winget is required to auto-install WiX Toolset."
  }

  Write-Step "Installing WiX Toolset via winget"
  Invoke-CheckedCommand -Executable "winget" -Arguments @(
    "install",
    "--id", "WiXToolset.WiXToolset",
    "--exact",
    "--accept-package-agreements",
    "--accept-source-agreements",
    "--silent"
  )
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($repoRoot)) {
  throw "Unable to determine repository root from setup.ps1 path."
}

Set-Location $repoRoot

if (-not (Test-Path "DictateAnywhere.sln")) {
  throw "DictateAnywhere.sln was not found. Run setup.ps1 from repository root."
}

Write-Step "Starting repository setup"
Write-Host "Repository: $repoRoot"

$dotnet = Get-DotnetExecutable
if (-not $SkipPrerequisites) {
  $dotnet = Ensure-DotnetSdk -RequiredMajorVersion 8
  if ($InstallWixToolset) {
    Ensure-WixToolset
  }
} elseif ($null -eq $dotnet) {
  throw "dotnet executable was not found. Remove -SkipPrerequisites or install .NET SDK 8 manually."
}

if (-not $SkipRestore) {
  Write-Step "Restoring solution packages"
  Invoke-CheckedCommand -Executable $dotnet -Arguments @("restore", "DictateAnywhere.sln")
}

if (-not $SkipBuild) {
  Write-Step "Building solution"
  $buildArgs = @("build", "DictateAnywhere.sln", "--configuration", "Release")
  if (-not $SkipRestore) {
    $buildArgs += "--no-restore"
  }

  Invoke-CheckedCommand -Executable $dotnet -Arguments $buildArgs
}

if ($InstallLocalModelRuntime) {
  Write-Step "Installing local model runtime"
  Invoke-CheckedCommand -Executable "powershell" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\setup-local-model-runtime.ps1")
}

if (-not $SkipTests) {
  Write-Step "Running tests"
  $testArgs = @("test", "DictateAnywhere.sln", "--configuration", "Release", "--logger", "trx;LogFileName=test-results.trx")
  if (-not $SkipBuild) {
    $testArgs += "--no-build"
  }

  Invoke-CheckedCommand -Executable $dotnet -Arguments $testArgs
}

if (-not $SkipPackagingSmoke) {
  Write-Step "Running packaging smoke checks"
  Invoke-CheckedCommand -Executable "powershell" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\packaging-smoke.ps1")
}

if (-not $SkipInstallerUpgradeCompatibility) {
  Write-Step "Running installer upgrade compatibility checks"
  Invoke-CheckedCommand -Executable "powershell" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\installer-upgrade-compatibility.ps1")
}

if (-not $SkipSecurityCompliance) {
  Write-Step "Running security compliance checks"
  Invoke-CheckedCommand -Executable "powershell" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ".\scripts\security-compliance.ps1")
}

Write-Step "Setup completed successfully"
Write-Host "You can now run the app and continue implementation."
