[CmdletBinding()]
param(
  [ValidateSet("Standard", "Elevated")]
  [string]$Scope = "Standard",
  [ValidateSet("Debug", "Release")]
  [string]$Configuration = "Debug",
  [switch]$NoRestore,
  [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
  param([Parameter(Mandatory = $true)][string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-IsAdministrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-RepoRoot {
  return [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
}

function Invoke-AppRunner {
  param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$ConfigurationName,
    [Parameter(Mandatory = $true)][bool]$SkipRestore,
    [Parameter(Mandatory = $true)][bool]$SkipBuild
  )

  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet is required. Install .NET SDK 8 and retry."
  }

  $arguments = @(
    "run",
    "--project", $ProjectPath,
    "-c", $ConfigurationName
  )

  if ($SkipRestore) {
    $arguments += "--no-restore"
  }

  if ($SkipBuild) {
    $arguments += "--no-build"
  }

  & dotnet @arguments | Out-Host
  if ($LASTEXITCODE -ne 0) {
    throw "dotnet run failed ($LASTEXITCODE)."
  }
}

$repoRoot = Resolve-RepoRoot
Set-Location -Path $repoRoot

$projectPath = Join-Path -Path $repoRoot -ChildPath "src/DictateAnywhere.App/DictateAnywhere.App.csproj"
if (-not (Test-Path -Path $projectPath)) {
  throw "App project file not found: $projectPath"
}

if ($Scope -eq "Elevated" -and -not (Test-IsAdministrator)) {
  Write-Step "Relaunching in elevated PowerShell"
  $scriptPath = [IO.Path]::GetFullPath($PSCommandPath)
  $relaunchArgs = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-File", $scriptPath,
    "-Scope", "Elevated",
    "-Configuration", $Configuration
  )

  if ($NoRestore) {
    $relaunchArgs += "-NoRestore"
  }

  if ($NoBuild) {
    $relaunchArgs += "-NoBuild"
  }

  try {
    $process = Start-Process -FilePath "powershell" -ArgumentList $relaunchArgs -Verb RunAs -Wait -PassThru
  }
  catch {
    throw "Elevation was cancelled or failed. Re-run and approve the UAC prompt."
  }

  if ($process.ExitCode -ne 0) {
    throw "Elevated app runner exited with code $($process.ExitCode)."
  }

  return
}

if ($Scope -eq "Elevated") {
  Write-Step "Starting Koncus Nai from source (elevated)"
}
else {
  Write-Step "Starting Koncus Nai from source (standard)"
}

Write-Host "Project: $projectPath"
Write-Host "Configuration: $Configuration"
Write-Host "Note: if a packaged tray instance is already running, exit it first."

Invoke-AppRunner -ProjectPath $projectPath -ConfigurationName $Configuration -SkipRestore:$NoRestore -SkipBuild:$NoBuild
