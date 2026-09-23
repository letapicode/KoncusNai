[CmdletBinding()]
param(
  [string]$PolicyPath = "docs/documentation-policy.json",
  [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Get-RepositoryRelativePath {
  param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$Path
  )

  $rootUri = [Uri]::new(([IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar))
  $pathUri = [Uri]::new([IO.Path]::GetFullPath($Path))
  return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function Resolve-RepositoryPath {
  param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$RelativePath
  )

  if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) {
    throw "Repository path must be a non-empty relative path: '$RelativePath'."
  }

  $normalized = $RelativePath.Replace('\', '/')
  while ($normalized.StartsWith('./', [StringComparison]::Ordinal)) {
    $normalized = $normalized.Substring(2)
  }
  if ($normalized.Split('/') | Where-Object { $_ -in @('', '.', '..') }) {
    throw "Repository path escapes its owner: '$RelativePath'."
  }

  $absolute = [IO.Path]::GetFullPath((Join-Path $Root $normalized))
  $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  if (-not $absolute.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Repository path escapes its owner: '$RelativePath'."
  }
  return $absolute
}

function Get-ProseLines {
  param([Parameter(Mandatory)][string]$Path)

  $insideFence = $false
  $lineNumber = 0
  foreach ($line in Get-Content -LiteralPath $Path) {
    $lineNumber++
    if ($line -match '^\s*(```|~~~)') {
      $insideFence = -not $insideFence
      continue
    }
    if (-not $insideFence) {
      [pscustomobject]@{ Number = $lineNumber; Text = [string]$line }
    }
  }
}

function ConvertTo-MarkdownAnchor {
  param([Parameter(Mandatory)][string]$Heading)

  $value = $Heading.ToLowerInvariant()
  $value = [regex]::Replace($value, '<[^>]+>', '')
  $value = $value.Replace('`', '').Replace('*', '').Replace('_', '')
  $value = [regex]::Replace($value, '[^\p{L}\p{Nd}\s-]', '')
  $value = [regex]::Replace($value.Trim(), '\s+', '-')
  return [regex]::Replace($value, '-+', '-')
}

function Get-MarkdownAnchors {
  param([Parameter(Mandatory)][string]$Path)

  $anchors = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $counts = @{}
  foreach ($line in Get-ProseLines -Path $Path) {
    if ($line.Text -notmatch '^\s{0,3}#{1,6}\s+(?<heading>.+?)\s*#*\s*$') { continue }
    $base = ConvertTo-MarkdownAnchor $Matches.heading
    if ([string]::IsNullOrWhiteSpace($base)) { continue }
    $anchor = $base
    if ($counts.ContainsKey($base)) {
      $counts[$base]++
      $anchor = "$base-$($counts[$base])"
    }
    else { $counts[$base] = 0 }
    [void]$anchors.Add($anchor)
  }
  return $anchors
}

function Get-MarkdownLinkDestinations {
  param([Parameter(Mandatory)][string]$Path)

  $definitions = @{}
  $prose = @(Get-ProseLines -Path $Path)
  foreach ($line in $prose) {
    if ($line.Text -match '^\s*\[(?<id>[^\]]+)\]:\s*(?<target><[^>]+>|\S+)') {
      $definitions[$Matches.id.ToLowerInvariant()] = [pscustomobject]@{ Target = $Matches.target; Number = $line.Number }
    }
  }

  foreach ($line in $prose) {
    foreach ($match in [regex]::Matches($line.Text, '(?<!!)\[[^\]]*\]\((?<target><[^>]+>|(?:\\.|[^)])*)\)')) {
      [pscustomobject]@{ Target = $match.Groups['target'].Value; Number = $line.Number }
    }
    foreach ($match in [regex]::Matches($line.Text, '(?<!!)\[[^\]]+\]\[(?<id>[^\]]+)\]')) {
      $id = $match.Groups['id'].Value.ToLowerInvariant()
      if (-not $definitions.ContainsKey($id)) {
        [pscustomobject]@{ Target = $null; Number = $line.Number; MissingReference = $id }
      }
      else {
        [pscustomobject]@{ Target = $definitions[$id].Target; Number = $line.Number }
      }
    }
  }
}

function Test-MarkdownLinks {
  param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$DocumentPath,
    [Parameter(Mandatory)][AllowEmptyCollection()][Collections.Generic.List[string]]$Errors
  )

  $relativeDocument = Get-RepositoryRelativePath -Root $Root -Path $DocumentPath
  foreach ($link in Get-MarkdownLinkDestinations -Path $DocumentPath) {
    if ($link.PSObject.Properties['MissingReference']) {
      $Errors.Add("$relativeDocument`:$($link.Number): missing Markdown reference '$($link.MissingReference)'.")
      continue
    }

    $target = ([string]$link.Target).Trim()
    if ($target.StartsWith('<') -and $target.EndsWith('>')) { $target = $target.Substring(1, $target.Length - 2) }
    $target = $target.Replace('\)', ')').Replace('\(', '(').Replace('\ ', ' ')
    if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:' -or $target.StartsWith('//')) { continue }

    $fragmentIndex = $target.IndexOf('#')
    $rawPath = if ($fragmentIndex -ge 0) { $target.Substring(0, $fragmentIndex) } else { $target }
    $pathPart = [Uri]::UnescapeDataString($rawPath).Replace('\', [IO.Path]::DirectorySeparatorChar)
    $anchor = if ($fragmentIndex -ge 0) { [Uri]::UnescapeDataString($target.Substring($fragmentIndex + 1)) } else { '' }
    $targetPath = if ([string]::IsNullOrWhiteSpace($pathPart)) {
      $DocumentPath
    }
    else {
      [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $DocumentPath) $pathPart))
    }
    $rootPrefix = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $targetPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
      $Errors.Add("$relativeDocument`:$($link.Number): broken relative link '$target'.")
      continue
    }
    if (-not [string]::IsNullOrWhiteSpace($anchor) -and [IO.Path]::GetExtension($targetPath) -ieq '.md') {
      $anchors = Get-MarkdownAnchors -Path $targetPath
      if (-not $anchors.Contains($anchor)) {
        $Errors.Add("$relativeDocument`:$($link.Number): missing anchor '#$anchor' in '$(Get-RepositoryRelativePath -Root $Root -Path $targetPath)'.")
      }
    }
  }
}

function Read-DocumentationPolicy {
  param([Parameter(Mandatory)][string]$Path)
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Documentation policy is missing: $Path" }
  try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
  catch { throw "Documentation policy is malformed: $Path" }
}

function Invoke-DocumentationValidation {
  param(
    [Parameter(Mandatory)][string]$Root,
    [Parameter(Mandatory)][string]$PolicyFile
  )

  $policy = Read-DocumentationPolicy -Path $PolicyFile
  if ([int]$policy.schemaVersion -ne 1) { throw 'Unsupported documentation policy schema.' }
  $documents = @($policy.canonicalDocuments)
  if ($documents.Count -eq 0) { throw 'Documentation policy contains zero canonical documents.' }

  $errors = [Collections.Generic.List[string]]::new()
  $documentPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $documentIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $ownedFacts = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($document in $documents) {
    $id = [string]$document.id
    $path = [string]$document.path
    if ([string]::IsNullOrWhiteSpace($id) -or -not $documentIds.Add($id)) {
      $errors.Add("docs/documentation-policy.json: duplicate or empty canonical document id '$id'.")
    }
    if ([string]::IsNullOrWhiteSpace($path) -or -not $documentPaths.Add($path.Replace('\', '/'))) {
      $errors.Add("docs/documentation-policy.json: duplicate, case-colliding, or empty canonical path '$path'.")
      continue
    }
    foreach ($fact in @($document.owns)) {
      if ([string]::IsNullOrWhiteSpace([string]$fact) -or -not $ownedFacts.Add([string]$fact)) {
        $errors.Add("docs/documentation-policy.json: canonical fact '$fact' has zero or multiple owners.")
      }
    }
    try { $absolute = Resolve-RepositoryPath -Root $Root -RelativePath $path }
    catch { $errors.Add("docs/documentation-policy.json: $($_.Exception.Message)"); continue }
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf) -or (Get-Item -LiteralPath $absolute).Length -eq 0) {
      $errors.Add("docs/documentation-policy.json: canonical document is missing or empty: $path")
      continue
    }
    $requiredHeadingsProperty = $document.PSObject.Properties['requiredHeadings']
    if ($null -ne $requiredHeadingsProperty) {
      $anchors = Get-MarkdownAnchors -Path $absolute
      foreach ($heading in @($requiredHeadingsProperty.Value)) {
        $anchor = ConvertTo-MarkdownAnchor ([string]$heading)
        if (-not $anchors.Contains($anchor)) { $errors.Add("$path`: missing required section '$heading'.") }
      }
    }
    if ([IO.Path]::GetExtension($absolute) -ieq '.md') {
      Test-MarkdownLinks -Root $Root -DocumentPath $absolute -Errors $errors
    }
  }

  foreach ($requiredPath in @($policy.requiredPaths)) {
    try { $absolute = Resolve-RepositoryPath -Root $Root -RelativePath ([string]$requiredPath) }
    catch { $errors.Add("docs/documentation-policy.json: $($_.Exception.Message)"); continue }
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
      $errors.Add("docs/documentation-policy.json: required current path is missing: $requiredPath")
    }
  }

  foreach ($claim in @($policy.requiredClaims)) {
    try { $absolute = Resolve-RepositoryPath -Root $Root -RelativePath ([string]$claim.path) }
    catch { $errors.Add("docs/documentation-policy.json: $($_.Exception.Message)"); continue }
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) {
      $errors.Add("docs/documentation-policy.json: required-claim document is missing: $($claim.path)")
      continue
    }
    $content = Get-Content -LiteralPath $absolute -Raw
    if ($content -notmatch [string]$claim.pattern) { $errors.Add("$($claim.path): $($claim.message)") }
  }

  $historical = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($historicalPath in @($policy.historicalDocuments)) {
    $normalizedHistoricalPath = ([string]$historicalPath).Replace('\', '/')
    if (-not $historical.Add($normalizedHistoricalPath)) {
      $errors.Add("docs/documentation-policy.json: duplicate or case-colliding historical path '$historicalPath'.")
      continue
    }
    if ($documentPaths.Contains($normalizedHistoricalPath)) {
      $errors.Add("docs/documentation-policy.json: '$historicalPath' cannot be both canonical and historical.")
      continue
    }
    try { $absoluteHistoricalPath = Resolve-RepositoryPath -Root $Root -RelativePath ([string]$historicalPath) }
    catch { $errors.Add("docs/documentation-policy.json: $($_.Exception.Message)"); continue }
    if (-not (Test-Path -LiteralPath $absoluteHistoricalPath -PathType Leaf) -or
        (Get-Item -LiteralPath $absoluteHistoricalPath).Length -eq 0) {
      $errors.Add("docs/documentation-policy.json: historical document is missing or empty: $historicalPath")
    }
  }
  $scanFiles = @()
  $readmePath = Join-Path $Root 'README.md'
  if (Test-Path -LiteralPath $readmePath -PathType Leaf) { $scanFiles += Get-Item -LiteralPath $readmePath }
  foreach ($scanRoot in @('docs', 'planning')) {
    $absoluteScanRoot = Join-Path $Root $scanRoot
    if (Test-Path -LiteralPath $absoluteScanRoot -PathType Container) {
      $scanFiles += Get-ChildItem -LiteralPath $absoluteScanRoot -Recurse -File -Filter '*.md' | Where-Object {
        $relativeScanPath = Get-RepositoryRelativePath -Root $Root -Path $_.FullName
        $relativeScanPath -notmatch '(^|/)(bin|obj|TestResults|artifacts|coverage)(/|$)' -and $_.Name -notlike '*_wpftmp*'
      }
    }
  }
  foreach ($file in $scanFiles) {
    $relative = Get-RepositoryRelativePath -Root $Root -Path $file.FullName
    if ($historical.Contains($relative)) { continue }
    foreach ($line in Get-ProseLines -Path $file.FullName) {
      foreach ($retired in @($policy.retiredClaims)) {
        if ($line.Text -match [string]$retired.pattern) {
          $errors.Add("$relative`:$($line.Number): retired claim '$($retired.id)': $($retired.message)")
        }
      }
    }
  }

  foreach ($manualPath in @($policy.manualEvidenceDocuments)) {
    try { $absolute = Resolve-RepositoryPath -Root $Root -RelativePath ([string]$manualPath) }
    catch { $errors.Add("docs/documentation-policy.json: $($_.Exception.Message)"); continue }
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { continue }
    $lines = @(Get-Content -LiteralPath $absolute)
    for ($index = 0; $index -lt $lines.Count; $index++) {
      if ($lines[$index] -notmatch '(?i)Manual Status:\s*\**\s*(PASS|FAIL)\**(?:\s|$)') { continue }
      $sectionStart = $index
      while ($sectionStart -gt 0 -and $lines[$sectionStart] -notmatch '^###\s+') { $sectionStart-- }
      $sectionEnd = $index + 1
      while ($sectionEnd -lt $lines.Count -and $lines[$sectionEnd] -notmatch '^###\s+') { $sectionEnd++ }
      $section = ($lines[$sectionStart..($sectionEnd - 1)] -join "`n")
      $requiredEvidence = @('Commit:', 'Build:', 'Date:', 'Environment:', 'Action:', 'Expected Result:', 'Actual Result:', 'Evidence:')
      $missing = @($requiredEvidence | Where-Object { $section.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -lt 0 })
      if ($missing.Count -gt 0) {
        $errors.Add("$manualPath`:$($index + 1): manual PASS/FAIL lacks an actual observation record ($($missing -join ', ')).")
      }
    }
  }

  if ($errors.Count -gt 0) { throw ($errors -join [Environment]::NewLine) }
  return [pscustomobject]@{
    canonicalDocuments = $documents.Count
    scannedCurrentDocuments = @($scanFiles | Where-Object { -not $historical.Contains((Get-RepositoryRelativePath -Root $Root -Path $_.FullName)) }).Count
    historicalDocuments = $historical.Count
    retiredRules = @($policy.retiredClaims).Count
    requiredPaths = @($policy.requiredPaths).Count
  }
}

function Invoke-NegativeSelfTests {
  $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
  $fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempBase ("notype docs validator " + [Guid]::NewGuid().ToString('N'))))
  if (-not $fixtureRoot.StartsWith($tempBase + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Documentation self-test scratch path escaped the system temporary directory.'
  }
  $passed = [Collections.Generic.List[string]]::new()
  try {
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'docs') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'planning') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot 'artifacts') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'README.md') -Value "# Fixture`n`n## Start`n`n[Guide](<docs/Guide (one).md#target-section>)`n[Escaped](docs/Guide\ \(one\).md#target-section)" -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'docs/Guide (one).md') -Value "# Guide`n`n## Target section`n`nHistory is not encrypted and is not optional.`n`n~~~text`nsrc/DictateAnywhere.ModelBenchmark`n~~~" -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'planning/historical.md') -Value "# Historical`n`nHistory is optional and local.`n`nsrc/DictateAnywhere.ModelBenchmark/Program.cs" -Encoding UTF8
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'artifacts/generated.md') -Value "History is optional." -Encoding UTF8

    $basePolicy = [ordered]@{
      schemaVersion = 1
      canonicalDocuments = @(
        [ordered]@{ id='navigation'; path='README.md'; owns=@('entry'); requiredHeadings=@('Start') },
        [ordered]@{ id='guide'; path='docs/Guide (one).md'; owns=@('guide'); requiredHeadings=@('Target section') }
      )
      historicalDocuments = @('planning/historical.md')
      manualEvidenceDocuments = @()
      requiredPaths = @('README.md')
      requiredClaims = @()
      retiredClaims = @(
        [ordered]@{ id='optional-history'; pattern='(?i)\bhistory\s+is\s+(?:an?\s+)?optional\b'; message='retired' },
        [ordered]@{ id='encrypted-history'; pattern='(?i)\bhistory\s+(?:stores?|uses|is)\s+(?:locally\s+)?encrypted\b'; message='retired' },
        [ordered]@{ id='old-tool-path'; pattern='(?i)\bsrc[\\/]+DictateAnywhere\.ModelBenchmark(?:[\\/]|\b)'; message='retired' },
        [ordered]@{ id='mutable-llama'; pattern='(?i)\b(?:download|resolve|fetch)(?:s|ed|ing)?\b[^\r\n]{0,80}\bllama(?:\.cpp)?\b[^\r\n]{0,80}\b(?:latest|main|master)\b'; message='mutable' },
        [ordered]@{ id='tag-action'; pattern='(?i)\bactions/checkout@v\d+\b'; message='mutable' }
      )
    }
    $policyFile = Join-Path $fixtureRoot 'docs/policy.json'
    $basePolicy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $policyFile -Encoding UTF8
    [void](Invoke-DocumentationValidation -Root $fixtureRoot -PolicyFile $policyFile)
    [void]$passed.Add('baseline')

    function Assert-FixtureFails {
      param([Parameter(Mandatory)][scriptblock]$Arrange)
      $caseName = "negative case $($passed.Count)"
      & $Arrange
      try {
        [void](Invoke-DocumentationValidation -Root $fixtureRoot -PolicyFile $policyFile)
        throw "Expected documentation fixture to fail: $caseName."
      }
      catch {
        if ($_.Exception.Message.StartsWith('Expected documentation fixture to fail:', [StringComparison]::Ordinal)) { throw }
        [void]$passed.Add('negative')
      }
    }

    Assert-FixtureFails { Add-Content -LiteralPath (Join-Path $fixtureRoot 'docs/Guide (one).md') -Value "`nHistory is optional." }
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'docs/Guide (one).md') -Value "# Guide`n`n## Target section`n`nHistory is not encrypted and is not optional." -Encoding UTF8
    Assert-FixtureFails { Set-Content -LiteralPath (Join-Path $fixtureRoot 'README.md') -Value "# Fixture`n`n## Start`n`n[Missing](docs/missing.md)" -Encoding UTF8 }
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'README.md') -Value "# Fixture`n`n## Start`n`n[Guide](<docs/Guide (one).md#target-section>)`n[Mixed slash](docs\Guide%20%28one%29.md#target-section)" -Encoding UTF8
    Assert-FixtureFails { Set-Content -LiteralPath (Join-Path $fixtureRoot 'README.md') -Value "# Fixture`n`n## Start`n`n[Guide](<docs/Guide (one).md#missing-anchor>)" -Encoding UTF8 }
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'README.md') -Value "# Fixture`n`n## Start`n`n[Guide](<docs/Guide (one).md#target-section>)" -Encoding UTF8
    Assert-FixtureFails { $basePolicy.canonicalDocuments[1].path = 'README.MD'; $basePolicy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $policyFile -Encoding UTF8 }
    $basePolicy.canonicalDocuments[1].path = 'docs/Guide (one).md'
    Assert-FixtureFails { $basePolicy.canonicalDocuments[1].owns = @('entry'); $basePolicy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $policyFile -Encoding UTF8 }
    $basePolicy.canonicalDocuments[1].owns = @('guide')
    Assert-FixtureFails { $basePolicy.canonicalDocuments = @(); $basePolicy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $policyFile -Encoding UTF8 }
    $basePolicy.canonicalDocuments = @(
      [ordered]@{ id='navigation'; path='README.md'; owns=@('entry'); requiredHeadings=@('Start') },
      [ordered]@{ id='guide'; path='docs/Guide (one).md'; owns=@('guide'); requiredHeadings=@('Target section') }
    )
    Assert-FixtureFails { Set-Content -LiteralPath $policyFile -Value '{ malformed' -Encoding UTF8 }
    $basePolicy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $policyFile -Encoding UTF8
    $manualPath = Join-Path $fixtureRoot 'docs/manual.md'
    Set-Content -LiteralPath $manualPath -Value "# Manual`n`n### Case`n- **Manual Status:** PASS" -Encoding UTF8
    $basePolicy.manualEvidenceDocuments = @('docs/manual.md')
    Assert-FixtureFails { $basePolicy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $policyFile -Encoding UTF8 }
    $basePolicy.manualEvidenceDocuments = @()
    Assert-FixtureFails { Add-Content -LiteralPath (Join-Path $fixtureRoot 'docs/Guide (one).md') -Value "`nactions/checkout@v4"; $basePolicy | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $policyFile -Encoding UTF8 }
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'docs/Guide (one).md') -Value "# Guide`n`n## Target section`n`nHistory is not encrypted and is not optional." -Encoding UTF8
    Assert-FixtureFails { Add-Content -LiteralPath (Join-Path $fixtureRoot 'docs/Guide (one).md') -Value "`nHistory stores encrypted text." }
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'docs/Guide (one).md') -Value "# Guide`n`n## Target section`n`nHistory is not encrypted and is not optional." -Encoding UTF8
    Assert-FixtureFails { Add-Content -LiteralPath (Join-Path $fixtureRoot 'docs/Guide (one).md') -Value "`nsrc\DictateAnywhere.ModelBenchmark\Program.cs" }
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'docs/Guide (one).md') -Value "# Guide`n`n## Target section`n`nHistory is not encrypted and is not optional." -Encoding UTF8
    Assert-FixtureFails { Add-Content -LiteralPath (Join-Path $fixtureRoot 'docs/Guide (one).md') -Value "`nDownload llama.cpp from latest." }
  }
  finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
  }
  Write-Host "Documentation validator self-test passed: $($passed.Count) cases." -ForegroundColor Green
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolvedPolicy = if ([IO.Path]::IsPathRooted($PolicyPath)) { [IO.Path]::GetFullPath($PolicyPath) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $PolicyPath)) }
if ($SelfTest) { Invoke-NegativeSelfTests }
$result = Invoke-DocumentationValidation -Root $repoRoot -PolicyFile $resolvedPolicy
Write-Host ("Documentation validation passed: {0} canonical, {1} current scanned, {2} historical, {3} retired rules, {4} required paths." -f $result.canonicalDocuments, $result.scannedCurrentDocuments, $result.historicalDocuments, $result.retiredRules, $result.requiredPaths) -ForegroundColor Green
