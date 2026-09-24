[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$testRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot ("ci-evidence-self-test/" + [Guid]::NewGuid().ToString("N"))))
if (-not $testRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
  throw "CI evidence self-test path escaped artifacts."
}
$inputs = Join-Path $testRoot "inputs"
$packet = Join-Path $testRoot "packet"
$script = Join-Path $PSScriptRoot "prepare-ci-evidence.ps1"

function Assert-Rejected {
  param([scriptblock]$Action, [string]$ExpectedMessage)
  $caught = $null
  try { & $Action | Out-Null }
  catch { $caught = $_.Exception.Message }
  if ($null -eq $caught -or -not $caught.Contains($ExpectedMessage)) {
    throw "CI evidence safeguard did not reject the case as expected: $ExpectedMessage; actual: $caught"
  }
}

try {
  [IO.Directory]::CreateDirectory($inputs) | Out-Null
  $reports = [ordered]@{
    "coverage-summary.json" = '{"Tests":{"Total":1},"Coverage":{"Aggregate":{"LinePercent":100},"CriticalTargets":[],"Assemblies":[]}}'
    "fault-seed-summary.json" = '{"Counts":{"Total":1},"Results":[{"Classification":"Killed"}]}'
    "compliance.json" = '{"schemaVersion":1,"status":"passed"}'
    "python-advisories.json" = '{"queries":1,"findings":[]}'
  }
  foreach ($name in $reports.Keys) {
    [IO.File]::WriteAllText((Join-Path $inputs $name), $reports[$name])
  }
  $arguments = @{
    OutputDirectory = $packet
    CoverageReport = Join-Path $inputs "coverage-summary.json"
    FaultReport = Join-Path $inputs "fault-seed-summary.json"
    ComplianceReport = Join-Path $inputs "compliance.json"
    AdvisoryReport = Join-Path $inputs "python-advisories.json"
  }

  & $script @arguments -RequireComplete | Out-Null
  & $script -OutputDirectory $packet -ValidateOnly | Out-Null

  foreach ($name in @("unexpected.dll", "recording.wav", "weights.safetensors", "credentials.json", "history.json")) {
    $path = Join-Path $packet $name
    [IO.File]::WriteAllText($path, "not approved")
    Assert-Rejected { & $script -OutputDirectory $packet -ValidateOnly } "unapproved file"
    [IO.File]::Delete($path)
  }

  $workspace = Join-Path $packet "workspace"
  [IO.Directory]::CreateDirectory($workspace) | Out-Null
  [IO.File]::WriteAllText((Join-Path $workspace "copied-source.cs"), "not approved")
  Assert-Rejected { & $script -OutputDirectory $packet -ValidateOnly } "directory or reparse point"
  [IO.File]::Delete((Join-Path $workspace "copied-source.cs"))
  [IO.Directory]::Delete($workspace)

  $coverage = Join-Path $packet "coverage-summary.json"
  foreach ($privateText in @(("github_pat_" + ("A" * 32)), ('C:' + '\\Users\\Private\\history.json'), ("voice" + ".wav"))) {
    [IO.File]::WriteAllText($coverage, '{"Tests":{"Total":1},"Coverage":{"Aggregate":{}},"note":"' + $privateText + '"}')
    Assert-Rejected { & $script -OutputDirectory $packet -ValidateOnly } "forbidden private path, credential, media, model, or binary reference"
  }
  [IO.File]::Copy((Join-Path $inputs "coverage-summary.json"), $coverage, $true)
  & $script -OutputDirectory $packet -ValidateOnly | Out-Null

  [IO.File]::WriteAllText($coverage, '{"Tests":{"Total":2},"Coverage":{"Aggregate":{},"CriticalTargets":[],"Assemblies":[]}}')
  Assert-Rejected { & $script -OutputDirectory $packet -ValidateOnly } "manifest does not match report bytes"
  [IO.File]::WriteAllText($coverage, "x" * (512KB + 1))
  Assert-Rejected { & $script -OutputDirectory $packet -ValidateOnly } "empty, oversized, or not a file"
  [IO.File]::WriteAllText($coverage, '{"Tests":{"Total":1},"Coverage":{"Aggregate":{},"CriticalTargets":[],"Assemblies":[]},"transcript":"private words"}')
  Assert-Rejected { & $script -OutputDirectory $packet -ValidateOnly } "unreviewed fields"
  [IO.File]::Copy((Join-Path $inputs "coverage-summary.json"), $coverage, $true)
  & $script -OutputDirectory $packet -ValidateOnly | Out-Null

  $missingArguments = $arguments.Clone()
  $missingArguments.OutputDirectory = Join-Path $testRoot "missing-packet"
  $missingArguments.AdvisoryReport = Join-Path $inputs "absent.json"
  Assert-Rejected { & $script @missingArguments -RequireComplete } "Required CI evidence is missing"
  Assert-Rejected { & $script -OutputDirectory (Join-Path $repoRoot "outside-artifacts") -ValidateOnly } "CI evidence must remain under artifacts"

  $partialArguments = $arguments.Clone()
  $partialArguments.OutputDirectory = Join-Path $testRoot "partial-packet"
  foreach ($key in @("CoverageReport", "FaultReport", "ComplianceReport", "AdvisoryReport")) {
    $partialArguments[$key] = Join-Path $inputs ("absent-" + $key + ".json")
  }
  & $script @partialArguments | Out-Null
  & $script -OutputDirectory $partialArguments.OutputDirectory -ValidateOnly | Out-Null

  Write-Host "CI evidence safeguard self-test passed: allowlist, workspace, media/model, credential, user path, hashes, size, path boundary, completeness, and partial failure evidence."
}
finally {
  if (Test-Path -LiteralPath $testRoot) {
    if (-not $testRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Refusing to remove CI evidence self-test path outside artifacts."
    }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
  }
}
