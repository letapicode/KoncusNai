param(
  [string]$OutputDirectory = "artifacts/coverage",
  [switch]$NoBuild,
  [switch]$SelfTest,
  [string]$ValidateOnlyDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
$thresholdPath = Join-Path $PSScriptRoot "coverage-thresholds.json"
$runSettingsPath = Join-Path $repoRoot "tests/coverage.runsettings"
$deterministicFilter = "Category!=WindowsWpf&Category!=ProcessIntegration&Category!=ModelIntegration&Category!=Hardware"

function ConvertTo-NormalizedCoveragePath {
  param([string]$Path)

  return $Path.Replace('\', '/').TrimStart([char[]]@('.', '/')).ToLowerInvariant()
}

function Get-CoveragePercent {
  param([int]$Covered, [int]$Valid)

  if ($Valid -eq 0) {
    return 100.0
  }

  return [Math]::Round(($Covered * 100.0) / $Valid, 4)
}

function Test-IsGeneratedCoveragePath {
  param([string]$NormalizedPath)

  return $NormalizedPath.Contains('/obj/') `
    -or $NormalizedPath.EndsWith('.g.cs', [StringComparison]::OrdinalIgnoreCase) `
    -or $NormalizedPath.EndsWith('.g.i.cs', [StringComparison]::OrdinalIgnoreCase) `
    -or $NormalizedPath.EndsWith('.designer.cs', [StringComparison]::OrdinalIgnoreCase) `
    -or $NormalizedPath.EndsWith('.assemblyinfo.cs', [StringComparison]::OrdinalIgnoreCase) `
    -or $NormalizedPath.EndsWith('.assemblyattributes.cs', [StringComparison]::OrdinalIgnoreCase)
}

function Read-CoverageReports {
  param(
    [string]$ReportsDirectory,
    [object]$ThresholdConfiguration
  )

  if (-not (Test-Path -LiteralPath $ReportsDirectory -PathType Container)) {
    throw "Coverage report directory does not exist: $ReportsDirectory"
  }

  # TRX deployment folders contain copied attachments. Read only reports at
  # the collector root or one collector-result directory below it so the same
  # module data is never counted twice.
  $reports = @(
    Get-ChildItem -LiteralPath $ReportsDirectory -Filter "coverage.cobertura.xml" -File
    Get-ChildItem -LiteralPath $ReportsDirectory -Directory | ForEach-Object {
      Get-ChildItem -LiteralPath $_.FullName -Filter "coverage.cobertura.xml" -File
    }
  )
  $expectedReportCount = [int]$ThresholdConfiguration.expectedReportCount
  if ($reports.Count -ne $expectedReportCount) {
    throw "Expected $expectedReportCount coverage reports but found $($reports.Count)."
  }

  $mergedFiles = @{}
  foreach ($report in $reports) {
    try {
      [xml]$document = Get-Content -LiteralPath $report.FullName -Raw
    }
    catch {
      throw "Coverage report is not valid XML: $($report.FullName)"
    }

    if ($null -eq $document.coverage) {
      throw "Coverage report has no coverage root: $($report.FullName)"
    }

    foreach ($package in @($document.SelectNodes("/coverage/packages/package"))) {
      $assembly = $package.GetAttribute("name")
      if ([string]::IsNullOrWhiteSpace($assembly)) {
        continue
      }
      if ($assembly.EndsWith(".Tests", [StringComparison]::OrdinalIgnoreCase)) {
        throw "Test assembly '$assembly' was included in production coverage."
      }

      foreach ($class in @($package.SelectNodes("classes/class"))) {
        $fileName = $class.GetAttribute("filename")
        if ([string]::IsNullOrWhiteSpace($fileName)) {
          continue
        }

        $normalizedPath = ConvertTo-NormalizedCoveragePath $fileName
        if (Test-IsGeneratedCoveragePath $normalizedPath) {
          continue
        }

        $key = $assembly.ToLowerInvariant() + "|" + $normalizedPath
        if (-not $mergedFiles.ContainsKey($key)) {
          $mergedFiles[$key] = @{
            Assembly = $assembly
            Path = $fileName.Replace('\', '/')
            NormalizedPath = $normalizedPath
            Lines = @{}
            Branches = @{}
          }
        }

        $entry = $mergedFiles[$key]
        foreach ($line in @($class.SelectNodes("lines/line"))) {
          $lineNumber = [int]$line.GetAttribute("number")
          $hits = [int]$line.GetAttribute("hits")
          if (-not $entry.Lines.ContainsKey($lineNumber) -or $hits -gt $entry.Lines[$lineNumber]) {
            $entry.Lines[$lineNumber] = $hits
          }

          if ($line.GetAttribute("branch") -eq "True") {
            $match = [regex]::Match($line.GetAttribute("condition-coverage"), '\((\d+)/(\d+)\)')
            if (-not $match.Success) {
              throw "Malformed branch coverage at $fileName line $lineNumber in $($report.FullName)."
            }

            $covered = [int]$match.Groups[1].Value
            $valid = [int]$match.Groups[2].Value
            if ($entry.Branches.ContainsKey($lineNumber) -and $valid -ne $entry.Branches[$lineNumber][1]) {
              throw "Inconsistent branch total at $fileName line $lineNumber across coverage reports."
            }
            if (-not $entry.Branches.ContainsKey($lineNumber) -or $covered -gt $entry.Branches[$lineNumber][0]) {
              $entry.Branches[$lineNumber] = @($covered, $valid)
            }
          }
        }
      }
    }
  }

  $fileMetrics = @()
  foreach ($entry in $mergedFiles.Values) {
    $linesValid = $entry.Lines.Count
    $linesCovered = @($entry.Lines.Values | Where-Object { $_ -gt 0 }).Count
    $branchesCovered = 0
    $branchesValid = 0
    foreach ($branch in $entry.Branches.Values) {
      $branchesCovered += $branch[0]
      $branchesValid += $branch[1]
    }

    $fileMetrics += [pscustomobject]@{
      Assembly = $entry.Assembly
      Path = $entry.Path
      NormalizedPath = $entry.NormalizedPath
      LinesCovered = $linesCovered
      LinesValid = $linesValid
      LinePercent = Get-CoveragePercent $linesCovered $linesValid
      BranchesCovered = $branchesCovered
      BranchesValid = $branchesValid
      BranchPercent = Get-CoveragePercent $branchesCovered $branchesValid
    }
  }

  $totalLinesCovered = ($fileMetrics | Measure-Object -Property LinesCovered -Sum).Sum
  $totalLinesValid = ($fileMetrics | Measure-Object -Property LinesValid -Sum).Sum
  $totalBranchesCovered = ($fileMetrics | Measure-Object -Property BranchesCovered -Sum).Sum
  $totalBranchesValid = ($fileMetrics | Measure-Object -Property BranchesValid -Sum).Sum
  if ($fileMetrics.Count -eq 0 -or $totalLinesValid -eq 0) {
    throw "Coverage reports contain no production lines."
  }
  if ($totalBranchesValid -eq 0) {
    throw "Coverage reports contain no production branches."
  }

  $aggregate = [pscustomobject]@{
    LinesCovered = $totalLinesCovered
    LinesValid = $totalLinesValid
    LinePercent = Get-CoveragePercent $totalLinesCovered $totalLinesValid
    BranchesCovered = $totalBranchesCovered
    BranchesValid = $totalBranchesValid
    BranchPercent = Get-CoveragePercent $totalBranchesCovered $totalBranchesValid
    MinimumLinePercent = [double]$ThresholdConfiguration.aggregate.minimumLinePercent
    MinimumBranchPercent = [double]$ThresholdConfiguration.aggregate.minimumBranchPercent
  }

  if ($aggregate.LinePercent -lt $aggregate.MinimumLinePercent) {
    throw "Aggregate line coverage $($aggregate.LinePercent)% is below $($aggregate.MinimumLinePercent)%."
  }
  if ($aggregate.BranchPercent -lt $aggregate.MinimumBranchPercent) {
    throw "Aggregate branch coverage $($aggregate.BranchPercent)% is below $($aggregate.MinimumBranchPercent)%."
  }

  $criticalResults = @()
  foreach ($target in @($ThresholdConfiguration.criticalTargets)) {
    $normalizedTargetPath = ConvertTo-NormalizedCoveragePath ([string]$target.path)
    $matches = @($fileMetrics | Where-Object {
      $_.Assembly.Equals([string]$target.assembly, [StringComparison]::OrdinalIgnoreCase) `
        -and $_.NormalizedPath.EndsWith($normalizedTargetPath, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matches.Count -ne 1) {
      throw "Critical coverage target '$($target.name)' resolved to $($matches.Count) files."
    }

    $metric = $matches[0]
    $minimumLine = [double]$target.minimumLinePercent
    $minimumBranch = [double]$target.minimumBranchPercent
    if ($metric.LinesValid -eq 0 -or $metric.BranchesValid -eq 0) {
      throw "Critical coverage target '$($target.name)' has no measurable lines or branches."
    }
    if ($metric.LinePercent -lt $minimumLine) {
      throw "Critical target '$($target.name)' line coverage $($metric.LinePercent)% is below $minimumLine%."
    }
    if ($metric.BranchPercent -lt $minimumBranch) {
      throw "Critical target '$($target.name)' branch coverage $($metric.BranchPercent)% is below $minimumBranch%."
    }

    $criticalResults += [pscustomobject]@{
      Name = [string]$target.name
      Assembly = $metric.Assembly
      Path = $metric.Path
      LinesCovered = $metric.LinesCovered
      LinesValid = $metric.LinesValid
      LinePercent = $metric.LinePercent
      MinimumLinePercent = $minimumLine
      BranchesCovered = $metric.BranchesCovered
      BranchesValid = $metric.BranchesValid
      BranchPercent = $metric.BranchPercent
      MinimumBranchPercent = $minimumBranch
    }
  }

  $assemblyResults = @($fileMetrics | Group-Object Assembly | ForEach-Object {
    $linesCovered = ($_.Group | Measure-Object -Property LinesCovered -Sum).Sum
    $linesValid = ($_.Group | Measure-Object -Property LinesValid -Sum).Sum
    $branchesCovered = ($_.Group | Measure-Object -Property BranchesCovered -Sum).Sum
    $branchesValid = ($_.Group | Measure-Object -Property BranchesValid -Sum).Sum
    [pscustomobject]@{
      Assembly = $_.Name
      LinesCovered = $linesCovered
      LinesValid = $linesValid
      LinePercent = Get-CoveragePercent $linesCovered $linesValid
      BranchesCovered = $branchesCovered
      BranchesValid = $branchesValid
      BranchPercent = Get-CoveragePercent $branchesCovered $branchesValid
    }
  } | Sort-Object Assembly)

  return [pscustomobject]@{
    ReportCount = $reports.Count
    MergeRule = "module + normalized case-insensitive path + line; maximum observed hits/covered branches"
    Aggregate = $aggregate
    CriticalTargets = $criticalResults
    Assemblies = $assemblyResults
  }
}

function Invoke-CoverageValidatorSelfTest {
  $selfTestRoot = Join-Path $repoRoot ("artifacts/coverage-validator-self-test/" + [Guid]::NewGuid().ToString("N"))
  [IO.Directory]::CreateDirectory($selfTestRoot) | Out-Null
  $minimalThresholds = [pscustomobject]@{
    expectedReportCount = 1
    aggregate = [pscustomobject]@{ minimumLinePercent = 0; minimumBranchPercent = 0 }
    criticalTargets = @([pscustomobject]@{
      name = "Required target"
      assembly = "DictateAnywhere.Core"
      path = "DictateAnywhere.Core/Required.cs"
      minimumLinePercent = 0
      minimumBranchPercent = 0
    })
  }

  $regressionThresholds = [pscustomobject]@{
    expectedReportCount = 1
    aggregate = [pscustomobject]@{ minimumLinePercent = 100; minimumBranchPercent = 100 }
    criticalTargets = @([pscustomobject]@{
      name = "Required target"
      assembly = "DictateAnywhere.Core"
      path = "DictateAnywhere.Core/Required.cs"
      minimumLinePercent = 100
      minimumBranchPercent = 100
    })
  }
  $cases = @(
    @{ Name = "missing"; Content = $null; Thresholds = $minimalThresholds },
    @{ Name = "malformed"; Content = "<coverage"; Thresholds = $minimalThresholds },
    @{ Name = "empty"; Content = "<coverage><packages /></coverage>"; Thresholds = $minimalThresholds },
    @{ Name = "vanished-critical"; Content = '<coverage><packages><package name="DictateAnywhere.Core"><classes><class filename="DictateAnywhere.Core/Other.cs"><lines><line number="1" hits="1" branch="True" condition-coverage="100% (1/1)" /></lines></class></classes></package></packages></coverage>'; Thresholds = $minimalThresholds },
    @{ Name = "threshold-regression"; Content = '<coverage><packages><package name="DictateAnywhere.Core"><classes><class filename="DictateAnywhere.Core/Required.cs"><lines><line number="1" hits="0" branch="True" condition-coverage="0% (0/1)" /></lines></class></classes></package></packages></coverage>'; Thresholds = $regressionThresholds }
  )

  foreach ($case in $cases) {
    $caseDirectory = Join-Path $selfTestRoot $case.Name
    [IO.Directory]::CreateDirectory($caseDirectory) | Out-Null
    if ($null -ne $case.Content) {
      [IO.File]::WriteAllText((Join-Path $caseDirectory "coverage.cobertura.xml"), $case.Content)
    }

    $rejected = $false
    try {
      $null = Read-CoverageReports $caseDirectory $case.Thresholds
    }
    catch {
      $rejected = $true
    }
    if (-not $rejected) {
      throw "Coverage validator accepted the '$($case.Name)' negative fixture."
    }
  }

  Write-Host "Coverage validator self-test rejected missing, malformed, empty, vanished-critical, and threshold-regressed evidence."
}

if (-not (Test-Path -LiteralPath $thresholdPath -PathType Leaf)) {
  throw "Coverage thresholds are missing: $thresholdPath"
}
$thresholds = Get-Content -LiteralPath $thresholdPath -Raw | ConvertFrom-Json

if ($SelfTest) {
  Invoke-CoverageValidatorSelfTest
  return
}

if (-not [string]::IsNullOrWhiteSpace($ValidateOnlyDirectory)) {
  $validationDirectory = [IO.Path]::GetFullPath((Join-Path $repoRoot $ValidateOnlyDirectory))
  $validationArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
  if (-not $validationDirectory.StartsWith($validationArtifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Coverage validation input must remain under the repository artifacts directory."
  }
  $validated = Read-CoverageReports $validationDirectory $thresholds
  Write-Host ("Validated aggregate coverage: line {0:N2}%; branch {1:N2}%." -f $validated.Aggregate.LinePercent, $validated.Aggregate.BranchPercent)
  foreach ($target in $validated.CriticalTargets) {
    Write-Host ("Validated critical {0}: line {1:N2}%; branch {2:N2}%." -f $target.Name, $target.LinePercent, $target.BranchPercent)
  }
  return
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required for coverage collection."
}

Set-Location -Path $repoRoot
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
if (-not $resolvedOutput.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Coverage output must remain under the repository artifacts directory."
}
$runDirectory = Join-Path $resolvedOutput ((Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ") + "-" + [Guid]::NewGuid().ToString("N"))
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null

if ($NoBuild) {
  & (Join-Path $PSScriptRoot "run-focused-tests.ps1") -Suite Deterministic -ListOnly -NoBuild
}
else {
  & (Join-Path $PSScriptRoot "run-focused-tests.ps1") -Suite Deterministic -ListOnly
}

$testArguments = @(
  "test",
  "DictateAnywhere.sln",
  "--configuration", "Release",
  "--no-restore",
  "--nologo",
  "--maxcpucount:1",
  "--filter", $deterministicFilter,
  "--settings", $runSettingsPath,
  "--collect:XPlat Code Coverage",
  "--blame-hang-timeout", "5m",
  "--logger", "trx;LogFilePrefix=coverage",
  "--results-directory", $runDirectory
)
if ($NoBuild) {
  $testArguments += "--no-build"
}

$started = [DateTimeOffset]::UtcNow
& dotnet @testArguments
$testExitCode = $LASTEXITCODE
$elapsed = [DateTimeOffset]::UtcNow - $started
if ($testExitCode -ne 0) {
  throw "Deterministic coverage tests failed with exit code $testExitCode."
}

$trxFiles = @(Get-ChildItem -LiteralPath $runDirectory -Filter "*.trx" -File)
if ($trxFiles.Count -ne [int]$thresholds.expectedReportCount) {
  throw "Expected $($thresholds.expectedReportCount) TRX results but found $($trxFiles.Count)."
}

$testTotals = @{ Total = 0; Executed = 0; Passed = 0; Failed = 0; Skipped = 0 }
foreach ($trx in $trxFiles) {
  [xml]$trxDocument = Get-Content -LiteralPath $trx.FullName -Raw
  $counters = $trxDocument.SelectSingleNode("//*[local-name()='Counters']")
  if ($null -eq $counters) {
    throw "TRX result has no counters: $($trx.FullName)"
  }
  $testTotals.Total += [int]$counters.total
  $testTotals.Executed += [int]$counters.executed
  $testTotals.Passed += [int]$counters.passed
  $testTotals.Failed += [int]$counters.failed + [int]$counters.error + [int]$counters.timeout + [int]$counters.aborted
  $testTotals.Skipped += [int]$counters.notExecuted
}
if ($testTotals.Total -eq 0 -or $testTotals.Executed -eq 0) {
  throw "Coverage execution reported zero tests."
}
if ($testTotals.Failed -ne 0 -or $testTotals.Skipped -ne 0 -or $testTotals.Passed -ne $testTotals.Total) {
  throw "Coverage execution was not a complete pass: $($testTotals | ConvertTo-Json -Compress)"
}

$coverage = Read-CoverageReports $runDirectory $thresholds
$summary = [ordered]@{
  GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
  Scope = "Release deterministic taxonomy; excludes WindowsWpf, ProcessIntegration, ModelIntegration, and Hardware"
  TestExitCode = $testExitCode
  Tests = $testTotals
  RuntimeSeconds = [Math]::Round($elapsed.TotalSeconds, 3)
  Coverage = $coverage
}
$summaryPath = Join-Path $resolvedOutput "coverage-summary.json"
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 8))

Write-Host ("Coverage tests: {0} passed, {1} failed, {2} skipped in {3:N1}s." -f $testTotals.Passed, $testTotals.Failed, $testTotals.Skipped, $elapsed.TotalSeconds)
Write-Host ("Aggregate coverage: line {0}/{1} ({2:N2}%, floor {3:N2}%); branch {4}/{5} ({6:N2}%, floor {7:N2}%)." -f $coverage.Aggregate.LinesCovered, $coverage.Aggregate.LinesValid, $coverage.Aggregate.LinePercent, $coverage.Aggregate.MinimumLinePercent, $coverage.Aggregate.BranchesCovered, $coverage.Aggregate.BranchesValid, $coverage.Aggregate.BranchPercent, $coverage.Aggregate.MinimumBranchPercent)
foreach ($target in $coverage.CriticalTargets) {
  Write-Host ("Critical {0}: line {1:N2}% (floor {2:N2}%); branch {3:N2}% (floor {4:N2}%)." -f $target.Name, $target.LinePercent, $target.MinimumLinePercent, $target.BranchPercent, $target.MinimumBranchPercent)
}
Write-Host "Machine-readable summary: $summaryPath"
