param(
  [string]$OutputDirectory = "artifacts/controlled-fault-seeds",
  [ValidateRange(10, 600)]
  [int]$TestTimeoutSeconds = 120,
  [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path -Path $repoRoot -ChildPath "artifacts"))
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path -Path $repoRoot -ChildPath $OutputDirectory))
if (-not $resolvedOutput.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Fault-seed output must remain under the repository artifacts directory."
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
  throw "dotnet is required for controlled fault seeding."
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
  throw "git is required to enumerate the tracked isolated workspace input."
}

function Invoke-DotNetProcess {
  param(
    [string]$WorkingDirectory,
    [string[]]$Arguments,
    [int]$TimeoutSeconds
  )

  $startInfo = [Diagnostics.ProcessStartInfo]::new("dotnet")
  $startInfo.WorkingDirectory = $WorkingDirectory
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $startInfo.CreateNoWindow = $true
  foreach ($argument in $Arguments) {
    $startInfo.ArgumentList.Add($argument)
  }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  $started = [DateTimeOffset]::UtcNow
  if (-not $process.Start()) {
    throw "Failed to start dotnet."
  }
  $stdoutTask = $process.StandardOutput.ReadToEndAsync()
  $stderrTask = $process.StandardError.ReadToEndAsync()
  $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
  if ($timedOut) {
    try {
      $process.Kill($true)
      $process.WaitForExit(5000) | Out-Null
    }
    catch [InvalidOperationException] {
    }
  }

  $streamDrainTimeout = [TimeSpan]::FromSeconds(5)
  $stdout = $stdoutTask.WaitAsync($streamDrainTimeout).GetAwaiter().GetResult()
  $stderr = $stderrTask.WaitAsync($streamDrainTimeout).GetAwaiter().GetResult()
  $exitCode = if ($timedOut) { $null } else { $process.ExitCode }
  $process.Dispose()

  return [pscustomobject]@{
    ExitCode = $exitCode
    TimedOut = $timedOut
    RuntimeSeconds = [Math]::Round(([DateTimeOffset]::UtcNow - $started).TotalSeconds, 3)
    Output = ($stdout + [Environment]::NewLine + $stderr).Trim()
  }
}

function Read-TestCounters {
  param([string]$ResultsDirectory)

  $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter "*.trx" -File)
  if ($trxFiles.Count -ne 1) {
    throw "Expected one TRX result in '$ResultsDirectory' but found $($trxFiles.Count)."
  }

  [xml]$document = Get-Content -LiteralPath $trxFiles[0].FullName -Raw
  $counters = $document.SelectSingleNode("//*[local-name()='Counters']")
  if ($null -eq $counters) {
    throw "TRX result has no counters: $($trxFiles[0].FullName)"
  }

  return [pscustomobject]@{
    Total = [int]$counters.total
    Executed = [int]$counters.executed
    Passed = [int]$counters.passed
    Failed = [int]$counters.failed + [int]$counters.error + [int]$counters.timeout + [int]$counters.aborted
    Skipped = [int]$counters.notExecuted
  }
}

function Invoke-TargetedTest {
  param(
    [string]$Workspace,
    [string]$Project,
    [string]$Filter,
    [string]$ResultsDirectory,
    [int]$TimeoutSeconds
  )

  [IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
  $arguments = @(
    "test", $Project,
    "--configuration", "Release",
    "--no-restore",
    "--nologo",
    "--filter", $Filter,
    "--logger", "trx;LogFileName=result.trx",
    "--results-directory", $ResultsDirectory
  )
  $processResult = Invoke-DotNetProcess $Workspace $arguments $TimeoutSeconds
  $counters = $null
  if (-not $processResult.TimedOut -and (Test-Path -LiteralPath (Join-Path $ResultsDirectory "result.trx"))) {
    $counters = Read-TestCounters $ResultsDirectory
  }

  return [pscustomobject]@{
    Process = $processResult
    Counters = $counters
  }
}

function Get-FaultClassification {
  param([object]$Mutant)

  if ($Mutant.Process.TimedOut) {
    return "TimedOut"
  }
  if ($null -eq $Mutant.Counters -or $Mutant.Counters.Total -eq 0) {
    return "Invalid"
  }
  if ($Mutant.Process.ExitCode -eq 0 -and $Mutant.Counters.Failed -eq 0) {
    return "Survived"
  }
  if ($Mutant.Process.ExitCode -ne 0 -and $Mutant.Counters.Failed -gt 0) {
    return "Killed"
  }

  return "Invalid"
}

function Invoke-FaultClassificationSelfTest {
  $fixtures = @(
    @{ Name = "killed"; Expected = "Killed"; Value = [pscustomobject]@{ Process = [pscustomobject]@{ TimedOut = $false; ExitCode = 1 }; Counters = [pscustomobject]@{ Total = 1; Failed = 1 } } },
    @{ Name = "survivor"; Expected = "Survived"; Value = [pscustomobject]@{ Process = [pscustomobject]@{ TimedOut = $false; ExitCode = 0 }; Counters = [pscustomobject]@{ Total = 1; Failed = 0 } } },
    @{ Name = "timeout"; Expected = "TimedOut"; Value = [pscustomobject]@{ Process = [pscustomobject]@{ TimedOut = $true; ExitCode = $null }; Counters = $null } },
    @{ Name = "zero-tests"; Expected = "Invalid"; Value = [pscustomobject]@{ Process = [pscustomobject]@{ TimedOut = $false; ExitCode = 0 }; Counters = [pscustomobject]@{ Total = 0; Failed = 0 } } },
    @{ Name = "tool-failure"; Expected = "Invalid"; Value = [pscustomobject]@{ Process = [pscustomobject]@{ TimedOut = $false; ExitCode = 1 }; Counters = $null } }
  )
  foreach ($fixture in $fixtures) {
    $actual = Get-FaultClassification $fixture.Value
    if ($actual -ne $fixture.Expected) {
      throw "Fault classification '$($fixture.Name)' returned '$actual' instead of '$($fixture.Expected)'."
    }
  }

  Write-Host "Fault classification self-test distinguishes killed, survived, timed-out, zero-test, and tool-failure outcomes."
}

if ($SelfTest) {
  Invoke-FaultClassificationSelfTest
  return
}

$faultSeeds = @(
  [pscustomobject]@{
    Id = "migration-pre-version-normalization"
    Concern = "migration"
    SourcePath = "src/DictateAnywhere.Settings/JsonSettingsStore.cs"
    Original = ": LegacySettingsReader.Read(root, Math.Max(schemaVersion, 0));"
    Replacement = ": LegacySettingsReader.Read(root, schemaVersion);"
    TestProject = "tests/DictateAnywhere.Settings.Tests/DictateAnywhere.Settings.Tests.csproj"
    TestFilter = "FullyQualifiedName~JsonSettingsStoreTests.LoadAsync_TreatsNegativeSchemaVersionAsBestEffortPreVersionInput"
  },
  [pscustomobject]@{
    Id = "current-settings-overlay-invariant"
    Concern = "state-policy"
    SourcePath = "src/DictateAnywhere.Core/Services/CurrentSettingsPolicy.cs"
    Original = "      OverlayEnabled = true,"
    Replacement = "      OverlayEnabled = false,"
    TestProject = "tests/DictateAnywhere.App.Tests/DictateAnywhere.App.Tests.csproj"
    TestFilter = "FullyQualifiedName~CurrentSettingsPolicyTests.Normalize_EnforcesCurrentProductConfiguration"
  },
  [pscustomobject]@{
    Id = "diagnostics-audio-marker-redaction"
    Concern = "redaction"
    SourcePath = "src/DictateAnywhere.Diagnostics/SensitiveDiagnosticsRedactor.cs"
    Original = "    current = RedactAllMarkers(current, AudioMarkers);"
    Replacement = "    // WP-27 fault seed: bypass audio marker redaction."
    TestProject = "tests/DictateAnywhere.Diagnostics.Tests/DictateAnywhere.Diagnostics.Tests.csproj"
    TestFilter = "FullyQualifiedName~SensitiveDiagnosticsRedactorTests.Redact_RedactsAudioMarkers"
  },
  [pscustomobject]@{
    Id = "model-snapshot-promotion-rollback"
    Concern = "recovery"
    SourcePath = "src/DictateAnywhere.Models/HuggingFaceSnapshotModelManager.cs"
    Original = "        Directory.Move(backupPath, destinationPath);"
    Replacement = "        TryDeleteDirectory(backupPath);"
    TestProject = "tests/DictateAnywhere.Models.Tests/DictateAnywhere.Models.Tests.csproj"
    TestFilter = "FullyQualifiedName~HuggingFaceSnapshotModelManagerTests.PromoteSnapshotDirectory_WhenPromotionFails_RestoresExistingSnapshot"
  }
)
if ($faultSeeds.Count -eq 0) {
  throw "Controlled fault-seed gate has no configured seeds."
}

Set-Location -LiteralPath $repoRoot
$runId = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ") + "-" + [Guid]::NewGuid().ToString("N")
$runDirectory = Join-Path $resolvedOutput $runId
$workspace = Join-Path $runDirectory "workspace"
[IO.Directory]::CreateDirectory($workspace) | Out-Null

$trackedFiles = @(& git -c core.quotepath=false ls-files --cached)
if ($LASTEXITCODE -ne 0 -or $trackedFiles.Count -eq 0) {
  throw "Could not enumerate tracked files for the isolated workspace."
}
foreach ($relativePath in $trackedFiles) {
  $source = Join-Path $repoRoot $relativePath
  if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Tracked workspace input is missing: $relativePath"
  }
  $destination = Join-Path $workspace $relativePath
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
  [IO.File]::Copy($source, $destination, $true)
}

$restoreResult = Invoke-DotNetProcess $workspace @("restore", "DictateAnywhere.sln", "--nologo") 300
if ($restoreResult.TimedOut -or $restoreResult.ExitCode -ne 0) {
  throw "Isolated solution restore failed or timed out.`n$($restoreResult.Output)"
}

$results = @()
foreach ($seed in $faultSeeds) {
  $sourcePath = Join-Path $workspace $seed.SourcePath
  $originalContent = [IO.File]::ReadAllText($sourcePath)
  $occurrences = [regex]::Matches($originalContent, [regex]::Escape($seed.Original)).Count
  if ($occurrences -ne 1) {
    throw "Fault seed '$($seed.Id)' expected one source match but found $occurrences."
  }

  $caseDirectory = Join-Path $runDirectory $seed.Id
  $baselineDirectory = Join-Path $caseDirectory "baseline"
  $baseline = Invoke-TargetedTest $workspace $seed.TestProject $seed.TestFilter $baselineDirectory $TestTimeoutSeconds
  if ($baseline.Process.TimedOut) {
    throw "Baseline for fault seed '$($seed.Id)' timed out."
  }
  if ($null -eq $baseline.Counters -or $baseline.Counters.Total -eq 0) {
    throw "Baseline for fault seed '$($seed.Id)' executed zero tests."
  }
  if ($baseline.Process.ExitCode -ne 0 -or $baseline.Counters.Failed -ne 0 -or $baseline.Counters.Skipped -ne 0) {
    throw "Baseline for fault seed '$($seed.Id)' was not a complete pass.`n$($baseline.Process.Output)"
  }

  $mutant = $null
  try {
    [IO.File]::WriteAllText($sourcePath, $originalContent.Replace($seed.Original, $seed.Replacement))
    $mutantDirectory = Join-Path $caseDirectory "faulted"
    $mutant = Invoke-TargetedTest $workspace $seed.TestProject $seed.TestFilter $mutantDirectory $TestTimeoutSeconds
  }
  finally {
    [IO.File]::WriteAllText($sourcePath, $originalContent)
  }

  $classification = Get-FaultClassification $mutant

  $results += [pscustomobject]@{
    Id = $seed.Id
    Concern = $seed.Concern
    SourcePath = $seed.SourcePath
    TestProject = $seed.TestProject
    TestFilter = $seed.TestFilter
    Classification = $classification
    BaselineTests = $baseline.Counters.Total
    FaultedTests = if ($null -eq $mutant.Counters) { 0 } else { $mutant.Counters.Total }
    FaultedFailures = if ($null -eq $mutant.Counters) { 0 } else { $mutant.Counters.Failed }
    RuntimeSeconds = [Math]::Round($baseline.Process.RuntimeSeconds + $mutant.Process.RuntimeSeconds, 3)
  }

  Write-Host ("Fault seed {0}: {1}." -f $seed.Id, $classification)
}

$counts = [ordered]@{
  Total = $results.Count
  Killed = @($results | Where-Object Classification -eq "Killed").Count
  Survived = @($results | Where-Object Classification -eq "Survived").Count
  Excluded = 0
  TimedOut = @($results | Where-Object Classification -eq "TimedOut").Count
  Equivalent = 0
  Invalid = @($results | Where-Object Classification -eq "Invalid").Count
}
$summary = [ordered]@{
  GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
  Scope = "Four explicit, non-equivalent controlled faults in migration, state/policy, redaction, and recovery code"
  Threshold = "Every configured fault must be killed; zero tests, zero seeds, timeout, invalid execution, or survivor fails the gate"
  Counts = $counts
  Results = $results
  IsolatedWorkspace = [IO.Path]::GetRelativePath($repoRoot, $workspace).Replace('\', '/')
}
$summaryPath = Join-Path $resolvedOutput "fault-seed-summary.json"
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 7))

if ($counts.Killed -ne $counts.Total -or $counts.Survived -ne 0 -or $counts.TimedOut -ne 0 -or $counts.Invalid -ne 0) {
  throw "Controlled fault-seed gate failed. Summary: $summaryPath"
}

Write-Host ("Controlled fault-seed gate passed: {0} killed, {1} survived, {2} excluded, {3} timed out, {4} equivalent, {5} invalid." -f $counts.Killed, $counts.Survived, $counts.Excluded, $counts.TimedOut, $counts.Equivalent, $counts.Invalid)
Write-Host "Machine-readable summary: $summaryPath"
