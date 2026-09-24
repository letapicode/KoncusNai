[CmdletBinding()]
param(
  [string]$OutputDirectory = "artifacts/ci-evidence-upload",
  [string]$CoverageReport = "artifacts/coverage-ci/coverage-summary.json",
  [string]$FaultReport = "artifacts/controlled-fault-seeds-ci/fault-seed-summary.json",
  [string]$ComplianceReport = "artifacts/supply-chain-ci/compliance.json",
  [string]$AdvisoryReport = "artifacts/supply-chain-ci/python-advisories.json",
  [switch]$RequireComplete,
  [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
$allowedReports = @(
  "coverage-summary.json",
  "fault-seed-summary.json",
  "compliance.json",
  "python-advisories.json"
)
$maxReportBytes = 512KB
$maxPacketBytes = 2MB
$privateContentPattern = '(?i)-----BEGIN [A-Z ]*PRIVATE KEY-----|github_pat_[A-Za-z0-9_]{20,}|gh[pousr]_[A-Za-z0-9]{25,}|hf_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9]{20,}|(?:authorization|access[_-]?token|client[_-]?secret)\s*[:=]\s*["'']?[A-Za-z0-9._-]{12,}|[A-Z]:\\{1,2}Users\\{1,2}|/Users/|/home/|(?i)\.(?:wav|mp3|flac|ogg|m4a|mp4|webm|safetensors|gguf|ggml|ckpt|pth|onnx|pkl|dll|exe|pdb|so|dylib|zip)\b'

function Resolve-ArtifactPath {
  param([string]$Value)
  if ([string]::IsNullOrWhiteSpace($Value)) { throw "Artifact path is empty." }
  $path = if ([IO.Path]::IsPathRooted($Value)) {
    [IO.Path]::GetFullPath($Value)
  }
  else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $Value))
  }
  if (-not $path.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "CI evidence must remain under artifacts: $Value"
  }
  return $path
}

function Assert-NoReparsePoint {
  param([string]$Path)
  $current = $Path
  while ($current.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    if (Test-Path -LiteralPath $current) {
      $item = Get-Item -LiteralPath $current -Force
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "CI evidence path uses a reparse point: $current"
      }
    }
    if ($current -eq $artifactsRoot) { break }
    $current = [IO.Path]::GetDirectoryName($current)
  }
}

function Assert-KnownFields {
  param([object]$Data, [string[]]$Allowed, [string]$Label)
  if ($null -eq $Data -or $Data -isnot [pscustomobject]) {
    throw "CI evidence $Label is not a JSON object."
  }
  $unexpected = @($Data.PSObject.Properties | ForEach-Object { $_.Name } | Where-Object { $_ -notin $Allowed })
  if ($unexpected.Count -gt 0) {
    throw "CI evidence $Label contains unreviewed fields: $($unexpected -join ', ')"
  }
}

function Assert-Report {
  param([string]$Path, [string]$Name)
  Assert-NoReparsePoint $Path
  $item = Get-Item -LiteralPath $Path -Force
  if ($item.PSIsContainer -or $item.Length -gt $maxReportBytes -or $item.Length -eq 0) {
    throw "CI evidence report is empty, oversized, or not a file: $Name"
  }
  $raw = [IO.File]::ReadAllText($Path)
  if ($raw -match $privateContentPattern) {
    throw "CI evidence report contains a forbidden private path, credential, media, model, or binary reference: $Name"
  }
  try { $data = $raw | ConvertFrom-Json -ErrorAction Stop }
  catch { throw "CI evidence report is not valid JSON: $Name" }
  switch ($Name) {
    "coverage-summary.json" {
      if ($null -eq $data.Tests -or $null -eq $data.Coverage -or $null -eq $data.Coverage.Aggregate) {
        throw "Coverage report lacks test or coverage results."
      }
      Assert-KnownFields $data @("GeneratedAtUtc", "Scope", "TestExitCode", "Tests", "RuntimeSeconds", "Coverage") "coverage report"
      Assert-KnownFields $data.Tests @("Total", "Executed", "Passed", "Failed", "Skipped") "coverage test counts"
      Assert-KnownFields $data.Coverage @("ReportCount", "MergeRule", "Aggregate", "CriticalTargets", "Assemblies") "coverage metrics"
      Assert-KnownFields $data.Coverage.Aggregate @("LinesCovered", "LinesValid", "LinePercent", "BranchesCovered", "BranchesValid", "BranchPercent", "MinimumLinePercent", "MinimumBranchPercent") "coverage aggregate"
      foreach ($target in @($data.Coverage.CriticalTargets | Where-Object { $null -ne $_ })) {
        Assert-KnownFields $target @("Name", "Assembly", "Path", "LinesCovered", "LinesValid", "LinePercent", "MinimumLinePercent", "BranchesCovered", "BranchesValid", "BranchPercent", "MinimumBranchPercent") "critical coverage target"
      }
      foreach ($assembly in @($data.Coverage.Assemblies | Where-Object { $null -ne $_ })) {
        Assert-KnownFields $assembly @("Assembly", "LinesCovered", "LinesValid", "LinePercent", "BranchesCovered", "BranchesValid", "BranchPercent") "assembly coverage"
      }
    }
    "fault-seed-summary.json" {
      if ($null -eq $data.Counts -or $null -eq $data.Results) { throw "Fault report lacks counts or results." }
      Assert-KnownFields $data @("GeneratedAtUtc", "Scope", "Threshold", "Counts", "Results", "IsolatedWorkspace") "fault report"
      Assert-KnownFields $data.Counts @("Total", "Killed", "Survived", "Excluded", "TimedOut", "Equivalent", "Invalid") "fault counts"
      foreach ($result in @($data.Results | Where-Object { $null -ne $_ })) {
        Assert-KnownFields $result @("Id", "Concern", "SourcePath", "TestProject", "TestFilter", "Classification", "BaselineTests", "FaultedTests", "FaultedFailures", "RuntimeSeconds") "fault result"
      }
    }
    "compliance.json" {
      if ($null -eq $data.schemaVersion -or [string]::IsNullOrWhiteSpace([string]$data.status)) {
        throw "Compliance report lacks schema or status."
      }
      Assert-KnownFields $data @("schemaVersion", "status", "nugetProjectLocks", "nugetResolvedPackages", "pythonLockedRequirements", "pythonPackageLicenses", "models", "runtimes", "assets", "bundledBinaries", "ciActions", "credentials") "compliance report"
    }
    "python-advisories.json" {
      if ($null -eq $data.queries -or $null -eq $data.findings) {
        throw "Python advisory report lacks query count or findings."
      }
      Assert-KnownFields $data @("queries", "queriedAtUtc", "source", "findings", "limitation") "Python advisory report"
    }
    default { throw "Unapproved CI evidence report: $Name" }
  }
  return $item.Length
}

function Assert-Packet {
  param([string]$Directory)
  Assert-NoReparsePoint $Directory
  $items = @(Get-ChildItem -LiteralPath $Directory -Recurse -Force)
  $names = @($items | ForEach-Object {
    if ($_.PSIsContainer -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
      throw "CI evidence packet contains a directory or reparse point: $($_.FullName)"
    }
    if ($_.Name -notin ($allowedReports + "evidence-manifest.json")) {
      throw "CI evidence packet contains an unapproved file: $($_.Name)"
    }
    $_.Name
  })
  if ("evidence-manifest.json" -notin $names) { throw "CI evidence manifest is missing." }
  $manifestPath = Join-Path $Directory "evidence-manifest.json"
  $manifestItem = Get-Item -LiteralPath $manifestPath -Force
  if ($manifestItem.Length -gt 64KB -or $manifestItem.Length -eq 0) { throw "CI evidence manifest is empty or oversized." }
  $manifestText = [IO.File]::ReadAllText($manifestPath)
  if ($manifestText -match $privateContentPattern) { throw "CI evidence manifest contains private content." }
  $manifest = $manifestText | ConvertFrom-Json -ErrorAction Stop
  if ($manifest.schemaVersion -ne 1 -or $null -eq $manifest.PSObject.Properties["reports"] -or
      $null -eq $manifest.PSObject.Properties["missingReports"]) {
    throw "CI evidence manifest schema is invalid."
  }
  $listed = @($manifest.reports | ForEach-Object { $_.fileName })
  $missing = @($manifest.missingReports | ForEach-Object { $_ })
  $actual = @($names | Where-Object { $_ -ne "evidence-manifest.json" })
  if ($listed.Count -ne $actual.Count -or
      ($listed.Count -gt 0 -and @(Compare-Object $listed $actual).Count -ne 0) -or
      $listed.Count -ne (@($listed | Sort-Object -Unique)).Count) {
    throw "CI evidence manifest does not match packet files."
  }
  $accountedFor = @($listed) + @($missing)
  if (@($missing | Where-Object { $_ -notin $allowedReports }).Count -ne 0 -or
      $accountedFor.Count -ne $allowedReports.Count -or
      @($accountedFor | Sort-Object -Unique).Count -ne $allowedReports.Count) {
    throw "CI evidence manifest does not account for the approved report set."
  }
  $totalBytes = 0L
  foreach ($entry in @($manifest.reports)) {
    if ($entry.fileName -notin $allowedReports) { throw "CI evidence manifest lists an unapproved report." }
    $path = Join-Path $Directory $entry.fileName
    $size = Assert-Report $path $entry.fileName
    $digest = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($entry.sizeBytes -ne $size -or $entry.sha256 -cne $digest) {
      throw "CI evidence manifest does not match report bytes: $($entry.fileName)"
    }
    $totalBytes += $size
  }
  if ($totalBytes -gt $maxPacketBytes) { throw "CI evidence packet exceeds $maxPacketBytes bytes." }
  Write-Host "CI evidence packet passed: $($actual.Count) reports, $totalBytes report bytes."
}

$outputPath = Resolve-ArtifactPath $OutputDirectory
if ($ValidateOnly) {
  if (-not (Test-Path -LiteralPath $outputPath -PathType Container)) { throw "CI evidence packet is missing." }
  Assert-Packet $outputPath
  return
}

$sources = [ordered]@{
  "coverage-summary.json" = $CoverageReport
  "fault-seed-summary.json" = $FaultReport
  "compliance.json" = $ComplianceReport
  "python-advisories.json" = $AdvisoryReport
}
$present = @()
$missing = @()
foreach ($name in $allowedReports) {
  $path = Resolve-ArtifactPath $sources[$name]
  if (Test-Path -LiteralPath $path -PathType Leaf) {
    $null = Assert-Report $path $name
    $present += [pscustomobject]@{ Name = $name; Path = $path }
  }
  else {
    $missing += $name
  }
}
if ($RequireComplete -and $missing.Count -gt 0) {
  throw "Required CI evidence is missing: $($missing -join ', ')"
}
Assert-NoReparsePoint $outputPath
if (Test-Path -LiteralPath $outputPath) {
  throw "CI evidence output already exists; use a fresh directory: $outputPath"
}
[IO.Directory]::CreateDirectory($outputPath) | Out-Null
$entries = @()
foreach ($report in $present) {
  $destination = Join-Path $outputPath $report.Name
  [IO.File]::Copy($report.Path, $destination, $false)
  $entries += [ordered]@{
    fileName = $report.Name
    sizeBytes = (Get-Item -LiteralPath $destination).Length
    sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
  }
}
$manifest = [ordered]@{
  schemaVersion = 1
  reports = $entries
  missingReports = $missing
}
[IO.File]::WriteAllText((Join-Path $outputPath "evidence-manifest.json"), ($manifest | ConvertTo-Json -Depth 5))
Assert-Packet $outputPath
