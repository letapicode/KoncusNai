param(
  [string]$ExpectedCommit,
  [ValidateSet("Release")]
  [string]$Configuration = "Release",
  [ValidateRange(30, 600)]
  [int]$TimeoutSeconds = 180,
  [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$policyPath = Join-Path $scriptRoot "history-preflight-scenarios.json"

function Normalize-RepositoryRelativePath {
  param([string]$Path)
  if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) {
    throw "History preflight paths must be nonempty repository-relative paths."
  }
  $normalized = $Path.Replace('\', '/').Trim()
  if ($normalized -match '(^|/)\.\.(/|$)' -or $normalized.StartsWith('/')) {
    throw "History preflight paths must remain repository-relative without traversal."
  }
  return $normalized
}

function Get-RequiredString {
  param($Object, [string]$Property, [string]$Context)
  $propertyValue = $Object.PSObject.Properties[$Property]
  $value = if ($null -eq $propertyValue) { $null } else { [string]$propertyValue.Value }
  if ([string]::IsNullOrWhiteSpace($value)) {
    throw "$Context requires nonempty '$Property'."
  }
  return $value.Trim()
}

function Assert-HistoryPreflightPolicy {
  param($Policy, [string]$RepositoryRoot)
  if ($null -eq $Policy -or [int]$Policy.schemaVersion -ne 1) {
    throw "History preflight policy schemaVersion must be 1."
  }
  if ([string]$Policy.manualEvidenceStatus -cne "NotTested") {
    throw "History preflight manualEvidenceStatus must remain NotTested."
  }

  [string]$accessibilityPath = Normalize-RepositoryRelativePath (Get-RequiredString $Policy "accessibilityPreflight" "Policy")
  [string]$windowXaml = Normalize-RepositoryRelativePath (Get-RequiredString $Policy.historyWindow "xamlPath" "History window")
  [string]$windowCodeBehind = Normalize-RepositoryRelativePath (Get-RequiredString $Policy.historyWindow "codeBehindPath" "History window")
  if ((Get-RequiredString $Policy.historyWindow "title" "History window") -cne "Dictation History") {
    throw "History preflight must retain the Dictation History window title."
  }
  $null = Get-RequiredString $Policy.historyWindow "openRoute" "History window"
  $null = Get-RequiredString $Policy.historyWindow "lifetimeOwner" "History window"
  $null = Get-RequiredString $Policy.historyWindow "presentationOwner" "History window"

  [string]$scratchRoot = Normalize-RepositoryRelativePath (Get-RequiredString $Policy.scratchBoundary "root" "Scratch boundary")
  if ($scratchRoot -cne "artifacts/history-preflight") {
    throw "History preflight scratch output must remain under artifacts/history-preflight."
  }
  if ((Get-RequiredString $Policy.scratchBoundary "environmentVariable" "Scratch boundary") -cne "NOTYPE_HISTORY_PREFLIGHT_SCRATCH") {
    throw "History preflight scratch environment variable is invalid."
  }
  if (-not [bool]$Policy.scratchBoundary.mustBeUnique -or -not [bool]$Policy.scratchBoundary.mustBeEmptyAfterTests) {
    throw "History preflight scratch must be unique and empty after tests."
  }

  $owners = @($Policy.owners)
  $states = @($Policy.states)
  $operatorCases = @($Policy.operatorCases)
  if ($owners.Count -eq 0) { throw "History preflight policy has zero owners." }
  if ($states.Count -eq 0) { throw "History preflight policy has zero states." }
  if ($operatorCases.Count -eq 0) { throw "History preflight policy has zero operator cases." }

  $ownerIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($owner in $owners) {
    $id = Get-RequiredString $owner "id" "History owner"
    if (-not $ownerIds.Add($id)) { throw "History preflight has duplicate owner id '$id'." }
    $null = Get-RequiredString $owner "owner" "History owner '$id'"
    $null = Get-RequiredString $owner "responsibility" "History owner '$id'"
  }

  $stateIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $coverageTests = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  $requiredControls = @("search", "list", "editor", "save", "delete")
  foreach ($state in $states) {
    $id = Get-RequiredString $state "id" "History state"
    if (-not $stateIds.Add($id)) { throw "History preflight has duplicate state id '$id'." }
    $ownerId = Get-RequiredString $state "ownerId" "History state '$id'"
    if (-not $ownerIds.Contains($ownerId)) { throw "History state '$id' has unknown owner '$ownerId'." }
    foreach ($control in $requiredControls) {
      $null = Get-RequiredString $state.controls $control "History state '$id' controls"
    }
    $tests = @($state.coverageTests)
    if ($tests.Count -eq 0) { throw "History state '$id' has zero coverage tests." }
    foreach ($test in $tests) {
      $testName = [string]$test
      if ([string]::IsNullOrWhiteSpace($testName) -or $testName -notmatch '^DictateAnywhere\.App\.Tests\.') {
        throw "History state '$id' has an invalid coverage test."
      }
      $null = $coverageTests.Add($testName)
    }
  }

  $caseIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($operatorCase in $operatorCases) {
    $id = Get-RequiredString $operatorCase "id" "History operator case"
    if (-not $caseIds.Add($id)) { throw "History preflight has duplicate operator case id '$id'." }
    if ([string]$operatorCase.status -cne "NotTested") {
      throw "History operator case '$id' must remain NotTested."
    }
    foreach ($field in @("setup", "action", "expected", "report")) {
      $null = Get-RequiredString $operatorCase $field "History operator case '$id'"
    }
    if ($null -eq $operatorCase.PSObject.Properties["requiresTestOwnedEntry"]) {
      throw "History operator case '$id' must declare requiresTestOwnedEntry."
    }
  }

  if (-not [string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    foreach ($relative in @($accessibilityPath, $windowXaml, $windowCodeBehind)) {
      $absolute = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $relative))
      $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
      if (-not $absolute.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "History preflight path '$relative' escapes the repository."
      }
      if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
        throw "History preflight path is missing: $relative"
      }
    }
  }

  return [PSCustomObject]@{
    OwnerCount = $owners.Count
    StateCount = $states.Count
    OperatorCaseCount = $operatorCases.Count
    CoverageTests = @($coverageTests | Sort-Object)
    AccessibilityPreflight = $accessibilityPath
    ScratchRoot = $scratchRoot
  }
}

function Convert-ToQuotedArgument {
  param([string]$Value)
  if ($Value -notmatch '[\s"]') { return $Value }
  return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Invoke-BoundedProcess {
  param(
    [string]$FileName,
    [string[]]$Arguments,
    [int]$Timeout,
    [string]$WorkingDirectory,
    [hashtable]$EnvironmentVariables = @{}
  )
  $info = [Diagnostics.ProcessStartInfo]::new()
  $info.FileName = $FileName
  $info.Arguments = [string]::Join(" ", @($Arguments | ForEach-Object { Convert-ToQuotedArgument ([string]$_) }))
  $info.WorkingDirectory = $WorkingDirectory
  $info.UseShellExecute = $false
  $info.CreateNoWindow = $true
  $info.RedirectStandardOutput = $true
  $info.RedirectStandardError = $true
  foreach ($name in $EnvironmentVariables.Keys) {
    $info.EnvironmentVariables[[string]$name] = [string]$EnvironmentVariables[$name]
  }
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $info
  if (-not $process.Start()) { throw "Failed to start '$FileName'." }
  $stdout = $process.StandardOutput.ReadToEndAsync()
  $stderr = $process.StandardError.ReadToEndAsync()
  if (-not $process.WaitForExit($Timeout * 1000)) {
    try { $process.Kill($true) } catch { try { $process.Kill() } catch {} }
    throw "'$FileName' exceeded the bounded $Timeout-second History preflight timeout."
  }
  return [PSCustomObject]@{
    ExitCode = $process.ExitCode
    StandardOutput = $stdout.GetAwaiter().GetResult()
    StandardError = $stderr.GetAwaiter().GetResult()
  }
}

function Assert-NoReparseAncestor {
  param([string]$Path, [string]$Boundary)
  $boundaryPath = [IO.Path]::GetFullPath($Boundary).TrimEnd([IO.Path]::DirectorySeparatorChar)
  $cursor = [IO.Path]::GetFullPath($Path)
  if (-not $cursor.StartsWith($boundaryPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "History preflight output must remain under the ignored artifacts boundary."
  }
  while ($cursor.StartsWith($boundaryPath, [StringComparison]::OrdinalIgnoreCase)) {
    if (Test-Path -LiteralPath $cursor) {
      $item = Get-Item -LiteralPath $cursor -Force
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "History preflight output traverses a reparse point."
      }
    }
    if ([string]::Equals($cursor, $boundaryPath, [StringComparison]::OrdinalIgnoreCase)) { break }
    $cursor = Split-Path -Parent $cursor
  }
}

function New-HistoryOperatorMatrix {
  param($Policy, [string]$Commit)
  $lines = [Collections.Generic.List[string]]::new()
  $lines.Add("# Dictation History operator matrix")
  $lines.Add("")
  $lines.Add("Generated for commit ``$Commit`` in ``Release``. This is preparation only; every result remains **NOT TESTED** until a person records a direct observation in the canonical evidence document.")
  $lines.Add("")
  $number = 0
  foreach ($operatorCase in $Policy.operatorCases) {
    $number++
    $lines.Add("## $number. $($operatorCase.id)")
    $lines.Add("")
    $lines.Add("1. Open: $($Policy.historyWindow.openRoute)")
    $lines.Add("2. Setup: $($operatorCase.setup)")
    $lines.Add("3. Do: $($operatorCase.action)")
    $lines.Add("4. Expect: $($operatorCase.expected)")
    $lines.Add("5. Report: $($operatorCase.report)")
    $lines.Add("6. Current result: **NOT TESTED**")
    $lines.Add("")
  }
  return [string]::Join([Environment]::NewLine, $lines)
}

function Assert-SanitizedGeneratedText {
  param([string]$Text, [string]$Name)
  if ([string]::IsNullOrWhiteSpace($Text)) { throw "Generated $Name is empty." }
  if ($Text -match '(?i)[A-Z]:[\\/]|\\Users\\|%LOCALAPPDATA%|password\s*[:=]|bearer\s+[A-Za-z0-9._-]+') {
    throw "Generated $Name contains machine-specific path or credential-shaped data."
  }
}

function Assert-Fails {
  param([scriptblock]$Action, [string]$Expected)
  try { & $Action; throw "Expected failure containing '$Expected'." }
  catch {
    if ($_.Exception.Message -notlike "*$Expected*") {
      throw "Unexpected self-test failure for '$Expected': $($_.Exception.Message)"
    }
  }
}

function Copy-Policy {
  param($Policy)
  return ($Policy | ConvertTo-Json -Depth 20 | ConvertFrom-Json)
}

try { $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json }
catch { throw "History preflight policy is malformed: $($_.Exception.Message)" }

if ($SelfTest) {
  $counts = Assert-HistoryPreflightPolicy $policy $repoRoot
  if ($counts.OwnerCount -ne 8 -or $counts.StateCount -ne 21 -or $counts.OperatorCaseCount -ne 9) {
    throw "Current History policy must expose 8 owners, 21 states, and 9 operator cases."
  }

  $invalid = Copy-Policy $policy; $invalid.schemaVersion = 0
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "schemaVersion"
  $invalid = Copy-Policy $policy; $invalid.manualEvidenceStatus = "Passed"
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "NotTested"
  $invalid = Copy-Policy $policy; $invalid.owners = @()
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "zero owners"
  $invalid = Copy-Policy $policy; $invalid.states = @()
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "zero states"
  $invalid = Copy-Policy $policy; $invalid.operatorCases = @()
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "zero operator cases"
  $invalid = Copy-Policy $policy; $invalid.states[1].id = $invalid.states[0].id.ToUpperInvariant()
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "duplicate state id"
  $invalid = Copy-Policy $policy; $invalid.operatorCases[1].id = $invalid.operatorCases[0].id.ToLowerInvariant()
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "duplicate operator case id"
  $invalid = Copy-Policy $policy; $invalid.states[0].ownerId = "missing"
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "unknown owner"
  $invalid = Copy-Policy $policy; $invalid.states[0].coverageTests = @()
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "zero coverage tests"
  $invalid = Copy-Policy $policy; $invalid.operatorCases[0].status = "Pass"
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "must remain NotTested"
  $invalid = Copy-Policy $policy; $invalid.operatorCases[0].action = ""
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "nonempty 'action'"
  $invalid = Copy-Policy $policy; $invalid.historyWindow.xamlPath = "../HistoryWindow.xaml"
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "repository-relative"
  $invalid = Copy-Policy $policy; $invalid.scratchBoundary.root = "artifacts"
  Assert-Fails { Assert-HistoryPreflightPolicy $invalid "" } "artifacts/history-preflight"
  Assert-Fails { Assert-SanitizedGeneratedText "C:\Users\Example\history.jsonl" "fixture" } "machine-specific"
  $matrix = New-HistoryOperatorMatrix $policy "0123456789012345678901234567890123456789"
  if ($matrix -notmatch '1\. HISTORY-01' -or $matrix -notmatch 'Current result: \*\*NOT TESTED\*\*') {
    throw "History operator matrix did not preserve numbering and Not tested status."
  }
  Assert-SanitizedGeneratedText $matrix "operator matrix"
  Write-Host "History preflight self-tests passed: 16/16; current policy has $($counts.OwnerCount) owners, $($counts.StateCount) states, $($counts.CoverageTests.Count) coverage tests, and $($counts.OperatorCaseCount) operator cases."
  return
}

if ([string]::IsNullOrWhiteSpace($ExpectedCommit) -or $ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') {
  throw "ExpectedCommit must be the exact 40-character commit under test."
}
$ExpectedCommit = $ExpectedCommit.ToLowerInvariant()
$counts = Assert-HistoryPreflightPolicy $policy $repoRoot

$accessibilityScript = Join-Path $repoRoot $counts.AccessibilityPreflight
$accessibility = Invoke-BoundedProcess "powershell" @(
  "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $accessibilityScript,
  "-ExpectedCommit", $ExpectedCommit, "-Configuration", $Configuration,
  "-TimeoutSeconds", $TimeoutSeconds
) $TimeoutSeconds $repoRoot
if ($accessibility.ExitCode -ne 0) {
  throw "The shared accessibility preflight failed with exit code $($accessibility.ExitCode)."
}
if ($accessibility.StandardOutput -notmatch 'Manual accessibility status remains NotTested\.') {
  throw "The shared accessibility preflight did not retain manual Not tested status."
}

$artifactsBoundary = Join-Path $repoRoot "artifacts"
$historyBoundary = Join-Path $artifactsBoundary "history-preflight"
if (-not (Test-Path -LiteralPath $artifactsBoundary)) {
  New-Item -ItemType Directory -Path $artifactsBoundary | Out-Null
}
Assert-NoReparseAncestor $historyBoundary $artifactsBoundary
if (-not (Test-Path -LiteralPath $historyBoundary)) {
  New-Item -ItemType Directory -Path $historyBoundary | Out-Null
}
$artifactRoot = Join-Path $historyBoundary ([Guid]::NewGuid().ToString("N"))
if (Test-Path -LiteralPath $artifactRoot) { throw "Generated History preflight directory already exists." }
Assert-NoReparseAncestor $artifactRoot $artifactsBoundary
New-Item -ItemType Directory -Path $artifactRoot | Out-Null
$scratchRoot = Join-Path $artifactRoot "scratch"
New-Item -ItemType Directory -Path $scratchRoot | Out-Null

$testProject = Join-Path $repoRoot "tests/DictateAnywhere.App.Tests/DictateAnywhere.App.Tests.csproj"
$filter = "FullyQualifiedName~LocalHistoryStoreTests|FullyQualifiedName~HistorySearchFilterTests|FullyQualifiedName~HistoryQueryCoordinatorTests|FullyQualifiedName~HistoryCommandCoordinatorTests|FullyQualifiedName~HistoryFileAccessCoordinatorTests|FullyQualifiedName~HistoryPersistenceFailureClassifierTests|FullyQualifiedName~CachingDictationHistoryRecorderTests|FullyQualifiedName~WorkbenchDictationHistoryRecorderTests|FullyQualifiedName~WorkbenchHistoryControllerTests|FullyQualifiedName~WorkbenchHistoryInteractionControllerTests|FullyQualifiedName~WorkbenchHistoryQueryCoordinatorTests|FullyQualifiedName~HistoryWindowArchitectureGuardrailTests|FullyQualifiedName~HistoryViewRefreshArchitectureGuardrailTests|FullyQualifiedName~HistoryPersistenceArchitectureGuardrailTests|FullyQualifiedName~HistoryWindowStateTests"
$common = @("test", $testProject, "--configuration", $Configuration, "--no-build", "--no-restore", "--nologo", "--filter", $filter)
$environment = @{ NOTYPE_HISTORY_PREFLIGHT_SCRATCH = $scratchRoot }

try {
  $discovery = Invoke-BoundedProcess "dotnet" ($common + "--list-tests") $TimeoutSeconds $repoRoot $environment
  if ($discovery.ExitCode -ne 0) { throw "History preflight discovery failed with exit code $($discovery.ExitCode)." }
  $discoveredNames = @($discovery.StandardOutput -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^DictateAnywhere\.App\.Tests\.' })
  if ($discoveredNames.Count -eq 0) { throw "History preflight discovered zero tests." }
  $discoveredSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
  foreach ($test in $discoveredNames) { $null = $discoveredSet.Add($test) }
  foreach ($coverageTest in $counts.CoverageTests) {
    if (-not $discoveredSet.Contains($coverageTest)) {
      throw "History state coverage test was not discovered: $coverageTest"
    }
  }

  $run = Invoke-BoundedProcess "dotnet" ($common + @(
    "--logger", "trx;LogFileName=history-preflight.trx",
    "--results-directory", $artifactRoot,
    "--blame-hang-timeout", "2m"
  )) $TimeoutSeconds $repoRoot $environment
  if ($run.ExitCode -ne 0) { throw "History preflight tests failed with exit code $($run.ExitCode)." }
  $trxPath = Join-Path $artifactRoot "history-preflight.trx"
  if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) { throw "History preflight test evidence is missing." }
  [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
  $counters = $trx.TestRun.ResultSummary.Counters
  $executed = [int]$counters.executed
  $passed = [int]$counters.passed
  $failed = [int]$counters.failed
  if ($executed -le 0 -or $passed -ne $executed -or $failed -ne 0) {
    throw "History preflight test evidence is empty or unsuccessful."
  }
  if ($executed -ne $discoveredNames.Count) {
    throw "History preflight executed $executed of $($discoveredNames.Count) discovered tests."
  }

  $scratchResidue = @(Get-ChildItem -LiteralPath $scratchRoot -Force)
  if ($scratchResidue.Count -ne 0) {
    throw "History preflight tests left scratch residue."
  }

  $report = [ordered]@{
    schemaVersion = 1
    commit = $ExpectedCommit
    configuration = $Configuration
    accessibilityPreflight = [ordered]@{ passed = $true; manualEvidenceStatus = "NotTested" }
    automatedTests = [ordered]@{
      discovered = $discoveredNames.Count
      executed = $executed
      passed = $passed
      failed = $failed
    }
    history = [ordered]@{
      owners = $counts.OwnerCount
      states = $counts.StateCount
      coverageTests = $counts.CoverageTests.Count
      operatorCases = $counts.OperatorCaseCount
    }
    dataBoundary = [ordered]@{
      testOwnedScratch = $true
      realHistoryAccessed = $false
      scratchCleaned = $true
    }
    manualEvidenceStatus = "NotTested"
  }
  $reportPath = Join-Path $artifactRoot "history-preflight-report.json"
  $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
  $matrixPath = Join-Path $artifactRoot "dictation-history-operator-matrix.md"
  New-HistoryOperatorMatrix $policy $ExpectedCommit | Set-Content -LiteralPath $matrixPath -Encoding UTF8
  $reportText = Get-Content -LiteralPath $reportPath -Raw
  $matrixText = Get-Content -LiteralPath $matrixPath -Raw
  $null = $reportText | ConvertFrom-Json
  Assert-SanitizedGeneratedText $reportText "History preflight report"
  Assert-SanitizedGeneratedText $matrixText "History operator matrix"
  if ($matrixText -notmatch 'Current result: \*\*NOT TESTED\*\*') {
    throw "Generated History operator matrix did not retain Not tested status."
  }

  foreach ($state in $policy.states) {
    Write-Host "[$($state.id)] owner=$($state.ownerId); save=$($state.controls.save); delete=$($state.controls.delete)"
  }
  Write-Host "History preflight passed: $executed/$executed automated tests; $($counts.OwnerCount) owners; $($counts.StateCount) states; $($counts.CoverageTests.Count) required coverage tests; $($counts.OperatorCaseCount) operator cases."
  Write-Host "Test-owned scratch was empty after execution; real History was not accessed."
  Write-Host "Manual History status remains NotTested."
  Write-Host "Sanitized report: artifacts/history-preflight/<run>/history-preflight-report.json"
  Write-Host "Operator matrix: artifacts/history-preflight/<run>/dictation-history-operator-matrix.md"
}
finally {
  if (Test-Path -LiteralPath $scratchRoot) {
    Assert-NoReparseAncestor $scratchRoot $artifactsBoundary
    Remove-Item -LiteralPath $scratchRoot -Recurse -Force
  }
}
