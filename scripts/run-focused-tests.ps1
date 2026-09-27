param(
  [ValidateSet("Deterministic", "WindowsWpf", "ProcessIntegration", "ModelIntegration", "Hardware", "All")]
  [string]$Suite = "Deterministic",
  [switch]$NoBuild,
  [switch]$ListOnly,
  [switch]$Diagnostics
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
    "--nologo",
    "--maxcpucount:1"
  )
  if ($NoBuild) {
    $arguments += "--no-build"
  }
  if ($ForDiscovery) {
    # Keep discovery and execution serial so test projects do not compete for
    # worker threads while timing-sensitive deterministic tests are running.
    $arguments += "--list-tests"
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
$resultDirectory = Join-Path $repoRoot ("artifacts/focused-tests/{0}/{1}" -f $Suite, [Guid]::NewGuid().ToString("N"))
$testArguments += @("--logger", "trx;LogFilePrefix=focused", "--results-directory", $resultDirectory)
if ($Diagnostics) {
  # Raw diagnostics stay local to this run; public evidence preparation does not upload this directory.
  [IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
  & dotnet --info | Set-Content -LiteralPath (Join-Path $resultDirectory "runtime-info.txt")
  if ($LASTEXITCODE -ne 0) { throw "Could not capture .NET diagnostic versions." }
  $testArguments += @("--blame-crash", "--blame-crash-dump-type", "mini", "--blame-hang", "--blame-hang-dump-type", "mini", "--blame-hang-timeout", "2m")
}
& dotnet @testArguments
$testExitCode = $LASTEXITCODE
if ($testExitCode -ne 0) {
  if ($Diagnostics) {
    # Keep useful last-test metadata in CI logs without publishing dumps, TRX payloads, or theory arguments.
    try {
      foreach ($sequence in Get-ChildItem -LiteralPath $resultDirectory -Filter "Sequence_*.xml" -Recurse -File) {
        if ($sequence.Length -gt 1MB) { continue }
        [xml]$document = Get-Content -LiteralPath $sequence.FullName -Raw
        @($document.SelectNodes("//*[local-name()='Test']")) | Select-Object -Last 5 | ForEach-Object {
          $testName = $_.GetAttribute("Name").Split('(')[0]
          if ($testName -match '^[A-Za-z_][A-Za-z0-9_.+]*$') { Write-Host "Recent test (not proof of crash cause): $testName" }
        }
      }
    } catch { Write-Warning "Could not read diagnostic test-sequence metadata." }
  }
  throw "Focused suite '$Suite' failed with exit code $testExitCode."
}
