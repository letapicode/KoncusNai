[CmdletBinding()]
param(
  [string]$PolicyPath = "docs/release/size-budget-policy.json",
  [string]$MeasurementReport,
  [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Resolve-RepoRoot {
  return [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
}

function Resolve-RepoPath {
  param([string]$PathValue, [string]$RepoRoot)
  if ([string]::IsNullOrWhiteSpace($PathValue)) { throw "Path must not be empty." }
  if ([IO.Path]::IsPathRooted($PathValue)) { return [IO.Path]::GetFullPath($PathValue) }
  return [IO.Path]::GetFullPath((Join-Path -Path $RepoRoot -ChildPath $PathValue))
}

function Assert-SafeRelativePath {
  param([string]$PathValue, [string]$Label)
  if ([string]::IsNullOrWhiteSpace($PathValue)) { throw "$Label path is empty." }
  if ([IO.Path]::IsPathRooted($PathValue)) { throw "$Label path must be repository-relative: $PathValue" }
  $normalized = $PathValue.Replace("\", "/")
  if ($normalized.Split("/") -contains "..") { throw "$Label path escapes its owner: $PathValue" }
  if ($normalized.StartsWith("/") -or $normalized.Contains(":")) { throw "$Label path is invalid: $PathValue" }
  return $normalized.TrimStart("./")
}

function Convert-ToInt64 {
  param($Value, [string]$Label, [bool]$AllowZero = $false)
  $number = 0L
  if ($null -eq $Value -or -not [long]::TryParse(
      [string]$Value,
      [Globalization.NumberStyles]::Integer,
      [Globalization.CultureInfo]::InvariantCulture,
      [ref]$number)) {
    throw "$Label must be an integer that fits in Int64; received '$Value'."
  }
  if ($number -lt 0 -or (-not $AllowZero -and $number -eq 0)) {
    throw "$Label must be $(if ($AllowZero) { 'non-negative' } else { 'positive' })."
  }
  return $number
}

function Assert-UniqueIds {
  param([object[]]$Items, [string]$CollectionName)
  if ($null -eq $Items -or $Items.Count -eq 0) { throw "$CollectionName must not be empty." }
  $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($item in $Items) {
    $id = [string]$item.id
    if ([string]::IsNullOrWhiteSpace($id)) { throw "$CollectionName contains an empty id." }
    if (-not $seen.Add($id)) { throw "$CollectionName contains duplicate id '$id'." }
  }
}

function Assert-BudgetValue {
  param([long]$Current, $Budget, [string]$Label)
  $minimum = Convert-ToInt64 -Value $Budget.minimumBytes -Label "$Label minimumBytes" -AllowZero $true
  $maximum = Convert-ToInt64 -Value $Budget.maximumBytes -Label "$Label maximumBytes"
  if ($minimum -gt $maximum) { throw "$Label minimum exceeds maximum." }
  if ($Current -lt $minimum) { throw "$Label measured $Current bytes, below the fail-closed minimum $minimum; inputs may be missing." }
  if ($Current -gt $maximum) { throw "$Label measured $Current bytes, above the budget $maximum." }
}

function Get-TrackedInventory {
  param([string]$RepoRoot)
  $indexLines = @(& git -C $RepoRoot -c core.quotepath=false ls-files -s)
  if ($LASTEXITCODE -ne 0) { throw "git ls-files failed with exit code $LASTEXITCODE." }
  if ($indexLines.Count -eq 0) { throw "Tracked inventory is empty." }

  $hashes = @($indexLines | ForEach-Object { ($_ -split "\s+", 4)[1] })
  $startInfo = [Diagnostics.ProcessStartInfo]::new()
  $startInfo.FileName = "git"
  $startInfo.Arguments = "-C `"$RepoRoot`" cat-file `"--batch-check=%(objectname) %(objectsize)`""
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardInput = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $startInfo
  if (-not $process.Start()) { throw "Failed to start git cat-file." }
  $outputTask = $process.StandardOutput.ReadToEndAsync()
  $errorTask = $process.StandardError.ReadToEndAsync()
  foreach ($hash in $hashes) { $process.StandardInput.WriteLine($hash) }
  $process.StandardInput.Close()
  if (-not $process.WaitForExit(30000)) { try { $process.Kill() } catch {}; throw "git cat-file timed out." }
  $standardOutput = $outputTask.Result
  $standardError = $errorTask.Result
  if ($process.ExitCode -ne 0) { throw "git cat-file failed ($($process.ExitCode)): $standardError" }
  $metadata = @($standardOutput -split "\r?\n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($metadata.Count -ne $indexLines.Count) { throw "git cat-file returned incomplete tracked-size metadata." }
  # Windows PowerShell's redirected StandardInput can prefix the first line with
  # a UTF-8 BOM. Re-query that one tracked object explicitly instead of accepting
  # a false "missing" response; a genuinely missing object still fails.
  if ($metadata[0] -match " missing$") {
    $firstSize = (& git -C $RepoRoot cat-file -s $hashes[0])
    if ($LASTEXITCODE -ne 0) { throw "git cat-file could not resolve tracked object '$($hashes[0])'." }
    $metadata[0] = "$($hashes[0]) $firstSize"
  }
  $objectSizes = @{}
  foreach ($line in $metadata) {
    $parts = $line -split " ", 2
    if ($parts.Count -ne 2 -or $parts[1] -eq "missing") { throw "git cat-file could not resolve tracked object '$($parts[0])'." }
    $objectSizes[$parts[0]] = $parts[1]
  }

  $seenPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $rows = [Collections.Generic.List[object]]::new()
  for ($index = 0; $index -lt $indexLines.Count; $index++) {
    $path = (($indexLines[$index] -split "\s+", 4)[3]).Replace("\", "/")
    if (-not $seenPaths.Add($path)) { throw "Tracked paths collide under Windows comparison: $path" }
    if ($path -match "(^|/)(bin|obj|TestResults|artifacts|coverage)(/|$)" -or $path -match "_wpftmp") {
      throw "Generated path is tracked and cannot be size-policy input: $path"
    }
    $hash = ($indexLines[$index] -split "\s+", 4)[1]
    if (-not $objectSizes.ContainsKey($hash)) { throw "No Git blob size was returned for $path ($hash)." }
    $objectSize = Convert-ToInt64 -Value $objectSizes[$hash] -Label "Git blob size for $path" -AllowZero $true
    $rows.Add([PSCustomObject]@{ path = $path; bytes = $objectSize })
  }
  return @($rows)
}

function Select-TrackedCategory {
  param([object[]]$Inventory, $Budget)
  $allTrackedProperty = $Budget.PSObject.Properties["allTracked"]
  if ($null -ne $allTrackedProperty -and $allTrackedProperty.Value -eq $true) { return @($Inventory) }
  $prefixes = @($Budget.includePrefixes | ForEach-Object { (Assert-SafeRelativePath -PathValue ([string]$_) -Label $Budget.id).TrimEnd("/") })
  $files = @($Budget.includeFiles | ForEach-Object { Assert-SafeRelativePath -PathValue ([string]$_) -Label $Budget.id })
  return @($Inventory | Where-Object {
    $candidate = $_.path
    @($files | Where-Object { [string]::Equals($_, $candidate, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0 -or
    @($prefixes | Where-Object { $candidate.StartsWith($_ + "/", [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
  })
}

function Get-Sha256Lower {
  param([string]$Path)
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-NoReparsePoint {
  param([string]$Path, [string]$Boundary)
  $resolvedBoundary = [IO.Path]::GetFullPath($Boundary).TrimEnd([IO.Path]::DirectorySeparatorChar)
  $cursor = [IO.Path]::GetFullPath($Path)
  while ($cursor.StartsWith($resolvedBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    if (Test-Path -LiteralPath $cursor) {
      $item = Get-Item -LiteralPath $cursor -Force
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Reparse point is not valid size evidence: $cursor"
      }
    }
    if ([string]::Equals($cursor, $resolvedBoundary, [StringComparison]::OrdinalIgnoreCase)) { break }
    $parent = Split-Path -Parent $cursor
    if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $cursor) { break }
    $cursor = $parent
  }
}

function Assert-VoicePreviewPack {
  param($Policy, [string]$RepoRoot, [Collections.Generic.Dictionary[string,long]]$TrackedSizes)
  $preview = $Policy.bundledAssets.voicePreviews
  $manifestRelative = Assert-SafeRelativePath -PathValue ([string]$preview.manifestPath) -Label "Voice preview manifest"
  $manifestPath = Resolve-RepoPath -PathValue $manifestRelative -RepoRoot $RepoRoot
  $dispositionProperty = $preview.PSObject.Properties['disposition']
  if ($null -ne $dispositionProperty -and
      [string]::Equals([string]$dispositionProperty.Value, "generated-on-demand", [StringComparison]::Ordinal)) {
    $manifestDirectory = Split-Path -Parent $manifestPath
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) { throw "Generated voice preview manifest must not be present in the source tree: $manifestRelative" }
    $unexpected = @(if (Test-Path -LiteralPath $manifestDirectory -PathType Container) {
      Get-ChildItem -LiteralPath $manifestDirectory -File -Filter "*.wav"
    })
    if ($unexpected.Count -gt 0) { throw "Generated voice preview must not be present in the source tree: $($unexpected[0].Name)" }
    if ($TrackedSizes.ContainsKey($manifestRelative)) { throw "Generated voice preview manifest must not be tracked." }
    if ((Convert-ToInt64 -Value $preview.expectedCount -Label "Voice preview expectedCount" -AllowZero $true) -ne 0) { throw "Generated-on-demand preview expectedCount must be zero." }
    return [PSCustomObject]@{ count=0; wavBytes=0; trackedBytes=0; publishedBytes=0 }
  }
  if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Voice preview manifest is missing: $manifestRelative" }
  Assert-NoReparsePoint -Path $manifestPath -Boundary $RepoRoot
  try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json } catch { throw "Voice preview manifest is malformed: $($_.Exception.Message)" }
  $entries = @($manifest.previews)
  $expectedCount = Convert-ToInt64 -Value $preview.expectedCount -Label "Voice preview expectedCount"
  if ($entries.Count -ne $expectedCount) { throw "Voice preview manifest has $($entries.Count) entries; expected $expectedCount." }

  $manifestDirectory = Split-Path -Parent $manifestPath
  $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $sizes = [Collections.Generic.List[long]]::new()
  foreach ($entry in $entries) {
    $fileName = Assert-SafeRelativePath -PathValue ([string]$entry.fileName) -Label "Voice preview"
    if ($fileName.Contains("/")) { throw "Voice preview files must be direct manifest siblings: $fileName" }
    if (-not $seen.Add($fileName)) { throw "Voice preview manifest contains duplicate/case-colliding file '$fileName'." }
    $hash = ([string]$entry.sha256).Trim().ToLowerInvariant()
    if ($hash -notmatch "^[0-9a-f]{64}$" -or $hash -match "^0{64}$") { throw "Voice preview '$fileName' has an invalid SHA-256." }
    $path = Join-Path -Path $manifestDirectory -ChildPath $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Voice preview is missing: $fileName" }
    Assert-NoReparsePoint -Path $path -Boundary $manifestDirectory
    $length = (Get-Item -LiteralPath $path).Length
    if ($length -le 0) { throw "Voice preview is empty: $fileName" }
    if ((Get-Sha256Lower -Path $path) -ne $hash) { throw "Voice preview hash mismatch: $fileName" }
    $sizes.Add([long]$length)
  }

  $unexpected = @(Get-ChildItem -LiteralPath $manifestDirectory -File -Filter "*.wav" | Where-Object { -not $seen.Contains($_.Name) })
  if ($unexpected.Count -gt 0) { throw "Unmanifested voice preview: $($unexpected[0].Name)" }
  if (-not $TrackedSizes.ContainsKey($manifestRelative)) { throw "Voice preview manifest is not tracked." }
  $wavBytes = [long](($sizes | Measure-Object -Sum).Sum)
  $trackedBytes = $wavBytes + $TrackedSizes[$manifestRelative]
  $publishedBytes = $wavBytes + (Get-Item -LiteralPath $manifestPath).Length
  if ($trackedBytes -ne (Convert-ToInt64 $preview.trackedBaselineBytes "Voice preview trackedBaselineBytes")) {
    throw "Voice preview Git-owned bytes changed: $trackedBytes."
  }
  if ($publishedBytes -ne (Convert-ToInt64 $preview.publishedBaselineBytes "Voice preview publishedBaselineBytes")) {
    throw "Voice preview publish-input bytes changed: $publishedBytes."
  }
  return [PSCustomObject]@{ count=$entries.Count; wavBytes=$wavBytes; trackedBytes=$trackedBytes; publishedBytes=$publishedBytes }
}

function Assert-ReleaseMeasurementObject {
  param($Report, $Policy)
  if ([int]$Report.schemaVersion -ne 1) { throw "Measurement report schemaVersion must be 1." }
  if ($Report.outputComplete -ne $true) { throw "Measurement report is incomplete." }
  if ([string]$Report.configuration -cne "Release") { throw "Measurement report must use Release." }
  if ([string]$Report.runtimeIdentifier -cne "win-x64") { throw "Measurement report must use win-x64." }
  if ($Report.payload.membershipValidated -ne $true) { throw "Installer payload membership was not validated." }
  if ((Convert-ToInt64 $Report.payload.developerExecutableResidue "developerExecutableResidue" $true) -ne 0) { throw "Developer executable residue invalidates the payload." }
  if ((Convert-ToInt64 $Report.payload.duplicatePhysicalPaths "duplicatePhysicalPaths" $true) -ne 0) { throw "Duplicate physical paths invalidate the payload." }
  foreach ($budget in @($Policy.releaseBudgets)) {
    $property = $Report.PSObject.Properties[$budget.reportProperty]
    if ($null -eq $property) { throw "Measurement report is missing '$($budget.reportProperty)'." }
    $current = Convert-ToInt64 $property.Value "$($budget.id) report value"
    Assert-BudgetValue -Current $current -Budget $budget -Label $budget.id
  }
}

function Invoke-SelfTests {
  $script:tests = 0
  function Expect-Failure([string]$Name, [scriptblock]$Action) {
    $script:tests++
    try { & $Action; throw "Self-test '$Name' did not fail." } catch {
      if ($_.Exception.Message -eq "Self-test '$Name' did not fail.") { throw }
    }
  }
  function Expect-Pass([string]$Name, [scriptblock]$Action) { $script:tests++; & $Action }

  Expect-Pass "boundary equal" { Assert-BudgetValue 10 ([pscustomobject]@{minimumBytes=10;maximumBytes=10}) "fixture" }
  Expect-Failure "above boundary" { Assert-BudgetValue 11 ([pscustomobject]@{minimumBytes=0;maximumBytes=10}) "fixture" }
  Expect-Failure "below fail-closed minimum" { Assert-BudgetValue 9 ([pscustomobject]@{minimumBytes=10;maximumBytes=20}) "fixture" }
  Expect-Failure "integer overflow" { Convert-ToInt64 "9223372036854775808" "fixture" }
  Expect-Failure "negative bytes" { Convert-ToInt64 -1 "fixture" $true }
  Expect-Failure "rooted path" { Assert-SafeRelativePath "C:\escape" "fixture" }
  Expect-Failure "mixed-slash traversal" { Assert-SafeRelativePath "safe\..\escape" "fixture" }
  Expect-Failure "duplicate id" { Assert-UniqueIds @([pscustomobject]@{id="A"},[pscustomobject]@{id="a"}) "fixtures" }
  Expect-Failure "empty inventory" { Assert-UniqueIds @() "fixtures" }

  $releaseBudget = [pscustomobject]@{ id="payload"; reportProperty="payloadBytes"; minimumBytes=1; maximumBytes=100 }
  $releasePolicy = [pscustomobject]@{ releaseBudgets=@($releaseBudget) }
  $valid = [pscustomobject]@{schemaVersion=1;outputComplete=$true;configuration="Release";runtimeIdentifier="win-x64";payload=[pscustomobject]@{membershipValidated=$true;developerExecutableResidue=0;duplicatePhysicalPaths=0};payloadBytes=50}
  Expect-Pass "valid measurement" { Assert-ReleaseMeasurementObject $valid $releasePolicy }
  $wrongRid = $valid.PSObject.Copy(); $wrongRid.runtimeIdentifier="win-x86"
  Expect-Failure "wrong RID" { Assert-ReleaseMeasurementObject $wrongRid $releasePolicy }
  $stale = $valid.PSObject.Copy(); $stale.outputComplete=$false
  Expect-Failure "stale incomplete output" { Assert-ReleaseMeasurementObject $stale $releasePolicy }
  $residue = $valid.PSObject.Copy(); $residue.payload=[pscustomobject]@{membershipValidated=$true;developerExecutableResidue=1;duplicatePhysicalPaths=0}
  Expect-Failure "developer executable residue" { Assert-ReleaseMeasurementObject $residue $releasePolicy }
  $duplicate = $valid.PSObject.Copy(); $duplicate.payload=[pscustomobject]@{membershipValidated=$true;developerExecutableResidue=0;duplicatePhysicalPaths=1}
  Expect-Failure "duplicate physical content" { Assert-ReleaseMeasurementObject $duplicate $releasePolicy }
  $missing = $valid.PSObject.Copy(); $missing.PSObject.Properties.Remove("payloadBytes")
  Expect-Failure "missing category" { Assert-ReleaseMeasurementObject $missing $releasePolicy }
  Expect-Failure "malformed JSON" { '{broken' | ConvertFrom-Json }

  $repoRoot = Resolve-RepoRoot
  $scratch = Join-Path $repoRoot ("artifacts/size-selftest-" + [Guid]::NewGuid().ToString("N"))
  $assetDirectory = Join-Path $scratch "previews"
  New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
  try {
    $samplePath = Join-Path $assetDirectory "sample.wav"
    $manifestPath = Join-Path $assetDirectory "manifest.json"
    $generatedManifest = $manifestPath.Substring($repoRoot.Length).TrimStart('\','/').Replace('\','/')
    $generatedPolicy = [pscustomobject]@{bundledAssets=[pscustomobject]@{voicePreviews=[pscustomobject]@{manifestPath=$generatedManifest;expectedCount=0;disposition="generated-on-demand"}}}
    Expect-Pass "absent generated preview pack" { $null=Assert-VoicePreviewPack $generatedPolicy $repoRoot ([Collections.Generic.Dictionary[string,long]]::new([StringComparer]::OrdinalIgnoreCase)) }
    [IO.File]::WriteAllBytes($samplePath, [byte[]](1,2,3))
    Expect-Failure "generated preview WAV rejected" { Assert-VoicePreviewPack $generatedPolicy $repoRoot ([Collections.Generic.Dictionary[string,long]]::new([StringComparer]::OrdinalIgnoreCase)) }
    $hash = Get-Sha256Lower $samplePath
    [ordered]@{schemaVersion=1;previews=@([ordered]@{fileName="sample.wav";sha256=$hash})} |
      ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $manifestRelative = $manifestPath.Substring($repoRoot.Length).TrimStart('\','/').Replace('\','/')
    $tracked = [Collections.Generic.Dictionary[string,long]]::new([StringComparer]::OrdinalIgnoreCase)
    $tracked[$manifestRelative] = (Get-Item $manifestPath).Length
    $fixturePolicy = [pscustomobject]@{bundledAssets=[pscustomobject]@{voicePreviews=[pscustomobject]@{manifestPath=$manifestRelative;expectedCount=1;trackedBaselineBytes=(Get-Item $manifestPath).Length+3;publishedBaselineBytes=(Get-Item $manifestPath).Length+3}}}
    Expect-Pass "valid preview fixture" { $null=Assert-VoicePreviewPack $fixturePolicy $repoRoot $tracked }
    $missingEntryPolicy = [pscustomobject]@{bundledAssets=[pscustomobject]@{voicePreviews=[pscustomobject]@{manifestPath=$manifestRelative;expectedCount=2;trackedBaselineBytes=1;publishedBaselineBytes=1}}}
    Expect-Failure "missing preview entry" { Assert-VoicePreviewPack $missingEntryPolicy $repoRoot $tracked }

    [ordered]@{schemaVersion=1;previews=@([ordered]@{fileName="sample.wav";sha256=$hash},[ordered]@{fileName="SAMPLE.wav";sha256=$hash})} |
      ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $tracked[$manifestRelative]=(Get-Item $manifestPath).Length
    $duplicatePolicy=[pscustomobject]@{bundledAssets=[pscustomobject]@{voicePreviews=[pscustomobject]@{manifestPath=$manifestRelative;expectedCount=2;trackedBaselineBytes=1;publishedBaselineBytes=1}}}
    Expect-Failure "case-colliding preview" { Assert-VoicePreviewPack $duplicatePolicy $repoRoot $tracked }

    [IO.File]::WriteAllBytes($samplePath, [byte[]]@())
    [ordered]@{schemaVersion=1;previews=@([ordered]@{fileName="sample.wav";sha256=(Get-Sha256Lower $samplePath)})} |
      ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $tracked[$manifestRelative]=(Get-Item $manifestPath).Length
    $emptyPolicy=[pscustomobject]@{bundledAssets=[pscustomobject]@{voicePreviews=[pscustomobject]@{manifestPath=$manifestRelative;expectedCount=1;trackedBaselineBytes=1;publishedBaselineBytes=1}}}
    Expect-Failure "zero-length preview" { Assert-VoicePreviewPack $emptyPolicy $repoRoot $tracked }

    [IO.File]::WriteAllBytes($samplePath, [byte[]](1,2,3))
    [ordered]@{schemaVersion=1;previews=@([ordered]@{fileName="sample.wav";sha256=('0'*64)})} |
      ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $tracked[$manifestRelative]=(Get-Item $manifestPath).Length
    Expect-Failure "all-zero preview hash" { Assert-VoicePreviewPack $emptyPolicy $repoRoot $tracked }

    [IO.File]::WriteAllBytes((Join-Path $assetDirectory "unexpected.wav"), [byte[]](4))
    [ordered]@{schemaVersion=1;previews=@([ordered]@{fileName="sample.wav";sha256=(Get-Sha256Lower $samplePath)})} |
      ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
    $tracked[$manifestRelative]=(Get-Item $manifestPath).Length
    $unexpectedPolicy=[pscustomobject]@{bundledAssets=[pscustomobject]@{voicePreviews=[pscustomobject]@{manifestPath=$manifestRelative;expectedCount=1;trackedBaselineBytes=(Get-Item $manifestPath).Length+3;publishedBaselineBytes=(Get-Item $manifestPath).Length+3}}}
    Expect-Failure "unmanifested preview" { Assert-VoicePreviewPack $unexpectedPolicy $repoRoot $tracked }
  }
  finally {
    $resolvedScratch=[IO.Path]::GetFullPath($scratch)
    $artifactsBoundary=[IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts")).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    if (-not $resolvedScratch.StartsWith($artifactsBoundary,[StringComparison]::OrdinalIgnoreCase)) { throw "Refusing unsafe self-test cleanup." }
    if (Test-Path -LiteralPath $resolvedScratch) { Remove-Item -LiteralPath $resolvedScratch -Recurse -Force }
  }

  Write-Host "Size-budget self-test passed: $tests cases."
}

if ($SelfTest) { Invoke-SelfTests; exit 0 }

$repoRoot = Resolve-RepoRoot
$resolvedPolicyPath = Resolve-RepoPath -PathValue $PolicyPath -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedPolicyPath -PathType Leaf)) { throw "Size policy is missing: $resolvedPolicyPath" }
try { $policy = Get-Content -LiteralPath $resolvedPolicyPath -Raw | ConvertFrom-Json } catch { throw "Size policy is malformed: $($_.Exception.Message)" }
if ([int]$policy.schemaVersion -ne 1) { throw "Size policy schemaVersion must be 1." }
Assert-UniqueIds @($policy.trackedBudgets) "trackedBudgets"
Assert-UniqueIds @($policy.releaseBudgets) "releaseBudgets"
Assert-UniqueIds @($policy.externalFootprints) "externalFootprints"
Assert-UniqueIds @($policy.exceptions) "exceptions"

$inventory = Get-TrackedInventory -RepoRoot $repoRoot
$trackedSizes = [Collections.Generic.Dictionary[string,long]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($row in $inventory) { $trackedSizes.Add($row.path, $row.bytes) }

foreach ($budget in @($policy.trackedBudgets)) {
  if ([string]::IsNullOrWhiteSpace([string]$budget.owner) -or [string]::IsNullOrWhiteSpace([string]$budget.rationale)) {
    throw "Tracked budget '$($budget.id)' needs an owner and rationale."
  }
  $selected = @(Select-TrackedCategory -Inventory $inventory -Budget $budget)
  $minimumFiles = Convert-ToInt64 $budget.minimumFiles "$($budget.id) minimumFiles"
  if ($selected.Count -lt $minimumFiles) { throw "Tracked budget '$($budget.id)' found $($selected.Count) files; expected at least $minimumFiles." }
  $bytes = [long](($selected | Measure-Object bytes -Sum).Sum)
  Assert-BudgetValue -Current $bytes -Budget $budget -Label $budget.id
  Write-Host ("{0}: {1} files, {2} bytes (budget {3})" -f $budget.id,$selected.Count,$bytes,$budget.maximumBytes)
}

$previewResult = Assert-VoicePreviewPack -Policy $policy -RepoRoot $repoRoot -TrackedSizes $trackedSizes
Write-Host ("voice-preview-pack: {0} previews, {1} WAV bytes, {2} publish-input bytes" -f $previewResult.count,$previewResult.wavBytes,$previewResult.publishedBytes)

$fonts = @($inventory | Where-Object { $_.path.StartsWith("src/DictateAnywhere.App/Assets/Fonts/",[StringComparison]::OrdinalIgnoreCase) })
$icons = @($inventory | Where-Object { [string]::Equals($_.path,"src/DictateAnywhere.App/Assets/Brand/KoncusNai.ico",[StringComparison]::OrdinalIgnoreCase) })
if ($fonts.Count -ne [int]$policy.bundledAssets.fonts.count -or [long](($fonts|Measure-Object bytes -Sum).Sum) -ne [long]$policy.bundledAssets.fonts.baselineBytes) { throw "Bundled font count or bytes changed." }
if ($icons.Count -ne [int]$policy.bundledAssets.icons.count -or [long](($icons|Measure-Object bytes -Sum).Sum) -ne [long]$policy.bundledAssets.icons.baselineBytes) { throw "Bundled icon count or bytes changed." }

foreach ($budget in @($policy.releaseBudgets)) {
  if ([string]::IsNullOrWhiteSpace([string]$budget.owner) -or [string]::IsNullOrWhiteSpace([string]$budget.rationale)) { throw "Release budget '$($budget.id)' needs an owner and rationale." }
  $baseline = Convert-ToInt64 $budget.baselineBytes "$($budget.id) baselineBytes"
  Assert-BudgetValue -Current $baseline -Budget $budget -Label "$($budget.id) canonical baseline"
}

foreach ($footprint in @($policy.externalFootprints)) {
  if ([string]::IsNullOrWhiteSpace([string]$footprint.owner) -or [string]::IsNullOrWhiteSpace([string]$footprint.provenanceId)) { throw "External footprint '$($footprint.id)' has no owner or provenanceId." }
  if ($footprint.status -eq "measured") {
    $hasMeasuredSize = $false
    foreach ($field in @("downloadBytes","archiveBytes","extractedBytes","payloadBytes")) {
      $property = $footprint.PSObject.Properties[$field]
      if ($null -ne $property -and $null -ne $property.Value) { $null = Convert-ToInt64 $property.Value "$($footprint.id) $field"; $hasMeasuredSize = $true }
    }
    if (-not $hasMeasuredSize) { throw "Measured external footprint '$($footprint.id)' has no measured bytes." }
  } elseif ($footprint.status -eq "blocked") {
    if ([string]::IsNullOrWhiteSpace([string]$footprint.blocker)) { throw "Blocked external footprint '$($footprint.id)' needs a precise blocker." }
  } else { throw "External footprint '$($footprint.id)' has invalid status '$($footprint.status)'." }
}

$provenancePath = Join-Path $repoRoot "docs/security/supply-chain-provenance.json"
try { $provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json } catch { throw "Supply-chain provenance is malformed: $($_.Exception.Message)" }
$expectedProvenanceIds = @(@($provenance.models).id + @($provenance.runtimes).id + @($provenance.python).id)
$declaredProvenanceIds = @(@($policy.externalFootprints).provenanceId)
foreach ($id in $expectedProvenanceIds) {
  if (@($declaredProvenanceIds | Where-Object { [string]::Equals($_,$id,[StringComparison]::OrdinalIgnoreCase) }).Count -ne 1) { throw "External size policy must contain exactly one disposition for provenance id '$id'." }
}
foreach ($id in $declaredProvenanceIds) {
  if (@($expectedProvenanceIds | Where-Object { [string]::Equals($_,$id,[StringComparison]::OrdinalIgnoreCase) }).Count -ne 1) { throw "External size disposition '$id' has no canonical provenance consumer." }
}

foreach ($exception in @($policy.exceptions)) {
  if ([string]::IsNullOrWhiteSpace([string]$exception.owner) -or [string]::IsNullOrWhiteSpace([string]$exception.reason) -or [string]::IsNullOrWhiteSpace([string]$exception.scope)) {
    throw "Exception '$($exception.id)' needs owner, reason, and bounded scope."
  }
}

if (-not [string]::IsNullOrWhiteSpace($MeasurementReport)) {
  $resolvedReport = Resolve-RepoPath -PathValue $MeasurementReport -RepoRoot $repoRoot
  if (-not (Test-Path -LiteralPath $resolvedReport -PathType Leaf)) { throw "Measurement report is missing: $resolvedReport" }
  try { $report = Get-Content -LiteralPath $resolvedReport -Raw | ConvertFrom-Json } catch { throw "Measurement report is malformed: $($_.Exception.Message)" }
  Assert-ReleaseMeasurementObject -Report $report -Policy $policy
  Write-Host "Release measurement report passed all size budgets."
}

Write-Host ("Size budgets passed: {0} tracked files, {1} tracked budgets, {2} release budgets, {3} external dispositions." -f $inventory.Count,@($policy.trackedBudgets).Count,@($policy.releaseBudgets).Count,@($policy.externalFootprints).Count)
