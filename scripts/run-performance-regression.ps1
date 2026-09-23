[CmdletBinding()]
param(
  [string]$OutputDirectory = "artifacts/performance",
  [string]$ReportPath = "docs/release/performance-regression-report.md",
  [string]$AudioPath,
  [string]$ProviderId = "cohere-local",
  [string]$ModelId = "cohere-transcribe-03-2026",
  [string]$Language = "en",
  [string]$FixtureId = "operator-supplied",
  [string]$ExpectedPhrase,
  [ValidateRange(0, 3600)][double]$MaxAudioSeconds = 0,
  [ValidateRange(0, 60)][double]$IdleAfterColdSeconds = 0,
  [switch]$CancelAfterWarm,
  [ValidateRange(0, 60)][double]$PostCancellationObservationSeconds = 0,
  [ValidateRange(2, 20)][int]$Iterations = 5,
  [ValidateRange(1, 7200)][int]$ProcessTimeoutSeconds = 900,
  [switch]$RequireModelRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
Set-Location -Path $repoRoot
$resolvedOutput = [IO.Path]::GetFullPath((Join-Path -Path $repoRoot -ChildPath $OutputDirectory))
$resolvedReport = [IO.Path]::GetFullPath((Join-Path -Path $repoRoot -ChildPath $ReportPath))
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $resolvedReport) -Force | Out-Null

$rows = New-Object System.Collections.Generic.List[object]

function Get-DescendantProcessIds {
  param(
    [Parameter(Mandatory)][int]$RootProcessId,
    [Parameter(Mandatory)][object[]]$ProcessSnapshot
  )

  $ids = [Collections.Generic.HashSet[int]]::new()
  [void]$ids.Add($RootProcessId)
  do {
    $countBefore = $ids.Count
    foreach ($entry in $ProcessSnapshot) {
      if ($ids.Contains([int]$entry.ParentProcessId)) {
        [void]$ids.Add([int]$entry.ProcessId)
      }
    }
  } while ($ids.Count -gt $countBefore)
  return @($ids)
}

function Assert-FiniteNonNegativeValue {
  param(
    [Parameter(Mandatory)][object]$Value,
    [Parameter(Mandatory)][string]$Label
  )

  $number = [double]$Value
  if ([double]::IsNaN($number) -or [double]::IsInfinity($number) -or $number -lt 0) {
    throw "$Label must be finite and non-negative."
  }
}

function Assert-ModelBenchmarkEvidence {
  param(
    [Parameter(Mandatory)][string]$ResultFile,
    [Parameter(Mandatory)][string]$MetricsFile,
    [Parameter(Mandatory)][string]$AudioFile
  )

  try {
    $report = Get-Content -LiteralPath $ResultFile -Raw | ConvertFrom-Json -ErrorAction Stop
    $metrics = Get-Content -LiteralPath $MetricsFile -Raw | ConvertFrom-Json -ErrorAction Stop
  }
  catch {
    throw "Benchmark evidence is missing or malformed. $($_.Exception.Message)"
  }

  if ([int]$report.schemaVersion -ne 2) {
    throw "Benchmark evidence schema must be 2."
  }
  if (-not [string]::Equals([string]$report.fixtureId, $FixtureId, [StringComparison]::Ordinal)) {
    throw "Benchmark fixture identity does not match the requested fixture."
  }
  $expectedHash = (Get-FileHash -LiteralPath $AudioFile -Algorithm SHA256).Hash
  if (-not [string]::Equals([string]$report.audioSha256, $expectedHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Benchmark audio hash does not match the requested fixture."
  }
  if ([int]$report.iterations -ne $Iterations) {
    throw "Benchmark evidence reported $($report.iterations) iterations; expected $Iterations."
  }
  Assert-FiniteNonNegativeValue $report.audioDurationMs "Audio duration"
  if ([double]$report.audioDurationMs -le 0) {
    throw "Benchmark audio duration must be greater than zero."
  }
  if ($MaxAudioSeconds -gt 0 -and [double]$report.audioDurationMs -gt (($MaxAudioSeconds * 1000) + 1)) {
    throw "Benchmark audio duration exceeds the requested clip length."
  }

  $measurements = @($report.measurements)
  if ($measurements.Count -ne 1) {
    throw "Benchmark evidence must contain exactly one requested provider/model measurement."
  }
  $measurement = $measurements[0]
  $providerMatches = [string]::Equals(
    [string]$measurement.providerId,
    $ProviderId,
    [StringComparison]::OrdinalIgnoreCase)
  $modelMatches = [string]::Equals(
    [string]$measurement.modelId,
    $ModelId,
    [StringComparison]::OrdinalIgnoreCase)
  if (-not $providerMatches -or -not $modelMatches) {
    throw "Benchmark provider/model identity does not match the requested selection."
  }

  $iterationEvidence = @($measurement.iterationMeasurements)
  if ($iterationEvidence.Count -ne $Iterations) {
    throw "Benchmark iteration evidence count does not match the requested count."
  }
  for ($index = 0; $index -lt $iterationEvidence.Count; $index++) {
    $iteration = $iterationEvidence[$index]
    $expectedTemperature = if ($index -eq 0) { "cold" } else { "warm" }
    if (-not [string]::Equals([string]$iteration.temperature, $expectedTemperature, [StringComparison]::Ordinal)) {
      throw "Benchmark iteration $($index + 1) has an invalid cold/warm classification."
    }
    foreach ($propertyName in @("elapsedMs", "audioPreparationMs", "workerStartupMs", "workerInvocationMs", "workerInferenceMs")) {
      $property = $iteration.PSObject.Properties[$propertyName]
      if ($null -eq $property -or $null -eq $property.Value) {
        throw "Benchmark iteration $($index + 1) is missing $propertyName."
      }
      Assert-FiniteNonNegativeValue $property.Value "Benchmark iteration $($index + 1) $propertyName"
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedPhrase) -and $iteration.accuracyPhraseMatched -ne $true) {
      throw "Benchmark iteration $($index + 1) did not satisfy the accuracy criterion."
    }
  }

  foreach ($propertyName in @("coldStartMs", "warmAverageMs", "warmP50Ms", "warmP95Ms", "p50Ms", "p95Ms")) {
    $property = $measurement.PSObject.Properties[$propertyName]
    if ($null -eq $property -or $null -eq $property.Value) {
      throw "Benchmark measurement is missing $propertyName."
    }
    Assert-FiniteNonNegativeValue $property.Value "Benchmark $propertyName"
  }
  if ($measurement.accuracyPassed -ne $true) {
    throw "Benchmark measurement did not satisfy the accuracy criterion."
  }

  if ([int]$metrics.exitCode -ne 0) {
    throw "Process evidence recorded a non-zero benchmark exit code."
  }
  $samples = @($metrics.samples)
  if ($samples.Count -eq 0) {
    throw "Process evidence contains no samples."
  }
  foreach ($sample in $samples) {
    foreach ($propertyName in @("elapsedMs", "processCount", "pythonProcessCount", "modelWorkerCount", "workingSetBytes", "privateBytes", "cpuSeconds")) {
      $property = $sample.PSObject.Properties[$propertyName]
      if ($null -eq $property -or $null -eq $property.Value) {
        throw "Process evidence is missing $propertyName."
      }
      Assert-FiniteNonNegativeValue $property.Value "Process sample $propertyName"
    }
  }
  $maximumLeafWorkers = [int](($samples | Measure-Object modelWorkerCount -Maximum).Maximum)
  if ($maximumLeafWorkers -ne 1) {
    throw "Process evidence must observe exactly one leaf model worker; observed $maximumLeafWorkers."
  }
}

function Invoke-SampledModelBenchmark {
  param(
    [Parameter(Mandatory)][string]$AudioFile,
    [Parameter(Mandatory)][string]$ResultFile,
    [Parameter(Mandatory)][string]$MetricsFile
  )

  $arguments = @(
    "run", "--project", "tools/DictateAnywhere.ModelBenchmark/DictateAnywhere.ModelBenchmark.csproj",
    "-c", "Release", "--no-build", "--no-restore", "--",
    "--provider", $ProviderId,
    "--model", $ModelId,
    "--audio", $AudioFile,
    "--fixture-id", $FixtureId,
    "--expected-phrase", $ExpectedPhrase,
    "--language", $Language,
    "--iterations", $Iterations.ToString([Globalization.CultureInfo]::InvariantCulture),
    "--output", $ResultFile)
  if ($MaxAudioSeconds -gt 0) {
    $arguments += @(
      "--max-audio-seconds",
      $MaxAudioSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
  }
  if ($IdleAfterColdSeconds -gt 0) {
    $arguments += @(
      "--idle-after-cold-seconds",
      $IdleAfterColdSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
  }
  if ($CancelAfterWarm) {
    $arguments += "--cancel-after-warm"
  }
  if ($PostCancellationObservationSeconds -gt 0) {
    $arguments += @(
      "--post-cancellation-observation-seconds",
      $PostCancellationObservationSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
  }

  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = "dotnet"
  $startInfo.UseShellExecute = $false
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  foreach ($argument in $arguments) {
    [void]$startInfo.ArgumentList.Add([string]$argument)
  }

  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  try {
    $captureStartedUtc = [DateTimeOffset]::UtcNow
    if (-not $process.Start()) {
      throw "The model benchmark process did not start."
    }

    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $samples = [Collections.Generic.List[object]]::new()
    $sampleClock = [Diagnostics.Stopwatch]::StartNew()
    while (-not $process.HasExited) {
      if ($sampleClock.Elapsed.TotalSeconds -gt $ProcessTimeoutSeconds) {
        $process.Kill($true)
        if (-not $process.WaitForExit(10000)) {
          throw "The owned benchmark process tree did not exit after timeout cleanup."
        }
        $null = $stdout.WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
        $null = $stderr.WaitAsync([TimeSpan]::FromSeconds(10)).GetAwaiter().GetResult()
        throw "The model benchmark timed out after $ProcessTimeoutSeconds seconds."
      }

      $snapshot = @(Get-CimInstance Win32_Process)
      $descendantIds = @(Get-DescendantProcessIds -RootProcessId $process.Id -ProcessSnapshot $snapshot)
      $processes = @($descendantIds | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue })
      $pythonEntries = @($snapshot | Where-Object {
        $descendantIds -contains [int]$_.ProcessId -and $_.Name -match '^python'
      })
      $leafModelWorkers = @($pythonEntries | Where-Object {
        $candidateId = [int]$_.ProcessId
        -not ($pythonEntries | Where-Object { [int]$_.ParentProcessId -eq $candidateId })
      })
      [long]$workingSetBytes = 0
      [long]$privateBytes = 0
      [double]$cpuSeconds = 0
      foreach ($sampleProcess in $processes) {
        $workingSetBytes += [long]$sampleProcess.WorkingSet64
        $privateBytes += [long]$sampleProcess.PrivateMemorySize64
        if ($null -ne $sampleProcess.CPU) {
          $cpuSeconds += [double]$sampleProcess.CPU
        }
      }
      $samples.Add([PSCustomObject]@{
        elapsedMs = [math]::Round($sampleClock.Elapsed.TotalMilliseconds, 2)
        processCount = $processes.Count
        pythonProcessCount = $pythonEntries.Count
        modelWorkerCount = $leafModelWorkers.Count
        workingSetBytes = $workingSetBytes
        privateBytes = $privateBytes
        cpuSeconds = [math]::Round($cpuSeconds, 3)
      })
      Start-Sleep -Milliseconds 250
    }

    $process.WaitForExit()
    $sampleClock.Stop()
    $stdoutText = $stdout.GetAwaiter().GetResult()
    $stderrText = $stderr.GetAwaiter().GetResult()
    if (-not [string]::IsNullOrWhiteSpace($stdoutText)) {
      Write-Host $stdoutText.TrimEnd()
    }
    if (-not [string]::IsNullOrWhiteSpace($stderrText)) {
      Write-Error $stderrText.TrimEnd() -ErrorAction Continue
    }

    $processor = Get-CimInstance Win32_Processor | Select-Object -First 1
    $computer = Get-CimInstance Win32_ComputerSystem
    $video = @(Get-CimInstance Win32_VideoController | Select-Object Name,AdapterRAM)
    $metrics = [PSCustomObject]@{
      schemaVersion = 1
      captureStartedUtc = $captureStartedUtc.ToString("o")
      capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
      rootProcessId = $process.Id
      exitCode = $process.ExitCode
      durationMs = [math]::Round($sampleClock.Elapsed.TotalMilliseconds, 2)
      hardware = [PSCustomObject]@{
        cpu = $processor.Name
        logicalProcessorCount = [Environment]::ProcessorCount
        installedMemoryBytes = [long]$computer.TotalPhysicalMemory
        gpu = $video
        gpuUtilization = $null
        note = "GPU utilization/VRAM residency require a vendor-specific sampler and are intentionally not inferred from adapter capacity."
      }
      peak = [PSCustomObject]@{
        workingSetBytes = [long](($samples | Measure-Object workingSetBytes -Maximum).Maximum ?? 0)
        privateBytes = [long](($samples | Measure-Object privateBytes -Maximum).Maximum ?? 0)
        processCount = [int](($samples | Measure-Object processCount -Maximum).Maximum ?? 0)
        pythonProcessCount = [int](($samples | Measure-Object pythonProcessCount -Maximum).Maximum ?? 0)
        modelWorkerCount = [int](($samples | Measure-Object modelWorkerCount -Maximum).Maximum ?? 0)
      }
      samples = $samples
    }
    $metrics | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $MetricsFile -Encoding UTF8
    return $process.ExitCode
  }
  finally {
    $process.Dispose()
  }
}

& dotnet test tests/DictateAnywhere.Benchmark.Tests/DictateAnywhere.Benchmark.Tests.csproj -c Release --no-restore --nologo
$rows.Add([PSCustomObject]@{
  criterion = "Benchmark contract tests"
  status = if ($LASTEXITCODE -eq 0) { "PASS" } else { "FAIL" }
  evidence = "Benchmark test process exit code $LASTEXITCODE."
})
if ($LASTEXITCODE -ne 0) {
  throw "Benchmark contract tests failed."
}

& dotnet build tools/DictateAnywhere.ModelBenchmark/DictateAnywhere.ModelBenchmark.csproj -c Release --no-restore --nologo
if ($LASTEXITCODE -ne 0) {
  throw "Model benchmark build failed."
}

$rawResultPath = Join-Path -Path $resolvedOutput -ChildPath "model-benchmark.json"
$processMetricsPath = Join-Path -Path $resolvedOutput -ChildPath "model-benchmark-process-metrics.json"
if (-not [string]::IsNullOrWhiteSpace($AudioPath)) {
  $resolvedAudio = [IO.Path]::GetFullPath((Join-Path -Path $repoRoot -ChildPath $AudioPath))
  $modelExitCode = Invoke-SampledModelBenchmark `
    -AudioFile $resolvedAudio `
    -ResultFile $rawResultPath `
    -MetricsFile $processMetricsPath
  if ($modelExitCode -eq 0) {
    Assert-ModelBenchmarkEvidence `
      -ResultFile $rawResultPath `
      -MetricsFile $processMetricsPath `
      -AudioFile $resolvedAudio
  }
  $rows.Add([PSCustomObject]@{
    criterion = "Current provider cold/warm model run"
    status = if ($modelExitCode -eq 0) { "PASS" } else { "FAIL" }
    evidence = "$ProviderId/$ModelId, fixture=$FixtureId, language=$Language, iterations=$Iterations, raw=$rawResultPath, processMetrics=$processMetricsPath."
  })
}
else {
  $rows.Add([PSCustomObject]@{
    criterion = "Current provider cold/warm model run"
    status = if ($RequireModelRun) { "FAIL" } else { "NOT_RUN" }
    evidence = "Pass -AudioPath with a non-sensitive repeatable fixture to run the installed provider/model."
  })
}

$summaryPath = Join-Path -Path $resolvedOutput -ChildPath "performance-regression-results.json"
$summary = [PSCustomObject]@{
  schemaVersion = 2
  generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
  commit = (& git rev-parse HEAD 2>$null)
  machine = $env:COMPUTERNAME
  os = [Environment]::OSVersion.VersionString
  processorCount = [Environment]::ProcessorCount
  fixtureId = $FixtureId
  rows = $rows
}
$summary | ConvertTo-Json -Depth 6 | Set-Content -Path $summaryPath -Encoding UTF8

$lines = @(
  "# Performance Regression Report",
  "",
  "- Generated: $([DateTimeOffset]::Now.ToString('o'))",
  "- Machine: $env:COMPUTERNAME",
  "- Summary: $summaryPath",
  "- Raw model result: $(if (Test-Path $rawResultPath) { $rawResultPath } else { 'not run' })",
  "- Process metrics: $(if (Test-Path $processMetricsPath) { $processMetricsPath } else { 'not run' })",
  "",
  "| Criterion | Status | Evidence |",
  "|---|---|---|"
)
foreach ($row in $rows) {
  $lines += "| $($row.criterion) | $($row.status) | $(([string]$row.evidence).Replace('|', '/')) |"
}
$lines | Set-Content -Path $resolvedReport -Encoding UTF8

if (@($rows | Where-Object { $_.status -eq "FAIL" }).Count -gt 0) {
  throw "Performance regression checks detected failures."
}
