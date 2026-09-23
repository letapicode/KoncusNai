param(
  [ValidateSet("Deterministic", "WindowsWpf", "ProcessIntegration", "ModelIntegration", "Hardware", "All")]
  [string]$Suite = "Deterministic",
  [switch]$NoBuild,
  [switch]$ListOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
Set-Location -Path $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required for focused test suites."
}

$filters = @{
  Deterministic = "Category!=WindowsWpf&Category!=ProcessIntegration&Category!=ModelIntegration&Category!=Hardware"
  WindowsWpf = "Category=WindowsWpf"
  ProcessIntegration = "Category=ProcessIntegration"
  ModelIntegration = "Category=ModelIntegration"
  Hardware = "Category=Hardware"
  All = $null
}
$filter = $filters[$Suite]

function New-TestArguments {
  param([switch]$ForDiscovery)

  $arguments = @(
    "test",
    "DictateAnywhere.sln",
    "--configuration", "Release",
    "--no-restore",
    "--nologo"
  )
  if ($NoBuild) {
    $arguments += "--no-build"
  }
  if ($ForDiscovery) {
    # Solution-level discovery output is parsed below. Keep MSBuild serial so
    # project output cannot interleave and make real tests disappear from the
    # count transiently.
    $arguments += @("--list-tests", "--maxcpucount:1")
  }
  if (-not [string]::IsNullOrWhiteSpace($filter)) {
    $arguments += @("--filter", $filter)
  }

  return $arguments
}

$discoveryArguments = New-TestArguments -ForDiscovery
$discoveryOutput = @(& dotnet @discoveryArguments 2>&1)
$discoveryExitCode = $LASTEXITCODE
if ($discoveryExitCode -ne 0) {
  $discoveryOutput | Out-Host
  throw "Focused test discovery failed with exit code $discoveryExitCode."
}

$discovered = @($discoveryOutput | Where-Object { $_ -match "^\s{4}\S" }).Count
if ($discovered -eq 0) {
  throw "Focused suite '$Suite' discovered no tests."
}

Write-Host "Focused suite '$Suite' discovered $discovered tests."
if ($ListOnly) {
  return
}

$testArguments = New-TestArguments
& dotnet @testArguments
if ($LASTEXITCODE -ne 0) {
  throw "Focused suite '$Suite' failed with exit code $LASTEXITCODE."
}
