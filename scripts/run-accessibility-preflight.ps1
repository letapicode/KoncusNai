[CmdletBinding()]
param(
  [string]$ExpectedCommit,
  [ValidateSet("Release")]
  [string]$Configuration = "Release",
  [int]$TimeoutSeconds = 180,
  [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$policyPath = Join-Path $PSScriptRoot "accessibility-preflight-scenarios.json"
$validStates = @("enabled", "disabled", "hidden", "conditional")
$validChrome = @("custom-caption", "specialized-custom", "native", "toast")

function Get-CanonicalShellNames {
  param([string]$InventoryPath)

  if (-not (Test-Path -LiteralPath $InventoryPath -PathType Leaf)) {
    throw "The canonical shell inventory is missing."
  }

  $names = [Collections.Generic.List[string]]::new()
  $insideTable = $false
  foreach ($line in Get-Content -LiteralPath $InventoryPath) {
    if ($line -match '^\|\s*Window\s*\|') {
      $insideTable = $true
      continue
    }
    if (-not $insideTable) { continue }
    if ($line -notmatch '^\|') { break }
    if ($line -match '^\|\s*-') { continue }
    $cells = @($line.Trim('|').Split('|') | ForEach-Object { $_.Trim() })
    if ($cells.Count -lt 3 -or [string]::IsNullOrWhiteSpace($cells[0])) {
      throw "The canonical shell inventory contains a malformed table row."
    }
    $names.Add($cells[0])
  }

  if ($names.Count -eq 0) { throw "The canonical shell inventory contains zero windows." }
  return $names.ToArray()
}

function Assert-RequiredText {
  param($Value, [string]$Message)
  if ([string]::IsNullOrWhiteSpace([string]$Value)) { throw $Message }
}

function Normalize-RepositoryRelativePath {
  param([string]$Path)
  $normalized = $Path.Replace('\', '/').Trim()
  if ([string]::IsNullOrWhiteSpace($normalized) -or
      [IO.Path]::IsPathRooted($normalized) -or
      $normalized.Split('/') -contains '..') {
    throw "Shell path '$Path' must be a safe repository-relative path."
  }
  return $normalized
}

function Assert-PreflightPolicy {
  param(
    $Policy,
    [string[]]$ExpectedInventoryNames,
    [string]$RepositoryRoot
  )

  if ([int]$Policy.schemaVersion -ne 2) { throw "Accessibility preflight schemaVersion must be 2." }
  if ([string]$Policy.manualEvidenceStatus -cne "NotTested") {
    throw "The automated preflight must keep manualEvidenceStatus as NotTested."
  }

  Assert-RequiredText $Policy.canonicalShellInventory "Accessibility preflight has no canonicalShellInventory."

  $scenarios = @($Policy.scenarios)
  if ($scenarios.Count -eq 0) { throw "Accessibility preflight contains zero scenarios." }
  $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $controlCount = 0
  foreach ($scenario in $scenarios) {
    $id = [string]$scenario.id
    if ([string]::IsNullOrWhiteSpace($id)) { throw "Accessibility preflight contains an empty scenario id." }
    if (-not $ids.Add($id)) { throw "Accessibility preflight contains duplicate scenario id '$id'." }
    if ([string]::IsNullOrWhiteSpace([string]$scenario.owner)) { throw "Scenario '$id' has no owner." }
    $controls = @($scenario.controls)
    if ($controls.Count -eq 0) { throw "Scenario '$id' contains zero controls." }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($control in $controls) {
      $name = [string]$control.name
      if ([string]::IsNullOrWhiteSpace($name)) { throw "Scenario '$id' contains an unnamed control." }
      if (-not $names.Add($name)) { throw "Scenario '$id' contains duplicate control '$name'." }
      if ($validStates -cnotcontains [string]$control.state) {
        throw "Scenario '$id' control '$name' has invalid state '$($control.state)'."
      }
      $controlCount++
    }
  }

  if ($controlCount -eq 0) { throw "Accessibility preflight contains zero controls." }

  $shells = @($Policy.shells)
  if ($shells.Count -eq 0) { throw "Accessibility preflight contains zero shells." }
  $shellIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $inventoryNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $caseIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $stateCount = 0
  $operatorCaseCount = 0
  foreach ($shell in $shells) {
    $entryId = [string]$shell.id
    Assert-RequiredText $entryId "Accessibility preflight contains an empty shell id."
    if (-not $shellIds.Add($entryId)) { throw "Accessibility preflight contains duplicate shell id '$entryId'." }
    $inventoryName = [string]$shell.inventoryName
    Assert-RequiredText $inventoryName "Shell '$entryId' has no inventoryName."
    if (-not $inventoryNames.Add($inventoryName)) { throw "Accessibility preflight contains duplicate inventory window '$inventoryName'." }
    foreach ($property in @("className", "owner", "invocation", "lifetime", "focusOwner", "themeOwner")) {
      Assert-RequiredText $shell.$property "Shell '$entryId' has no $property."
    }
    if ($validChrome -cnotcontains [string]$shell.chrome) { throw "Shell '$entryId' has invalid chrome '$($shell.chrome)'." }
    if ([int]$shell.defaultSize.width -le 0 -or [int]$shell.defaultSize.height -le 0) {
      throw "Shell '$entryId' has a zero or negative default dimension."
    }
    if ([string]$shell.resizeMode -cne "NoResize" -and
        ([int]$shell.minimumSize.width -le 0 -or [int]$shell.minimumSize.height -le 0)) {
      throw "Resizable shell '$entryId' has a zero or negative minimum dimension."
    }
    $states = @($shell.states)
    if ($states.Count -eq 0) { throw "Shell '$entryId' contains zero states." }
    $stateNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($state in $states) {
      Assert-RequiredText $state "Shell '$entryId' contains an empty state."
      if (-not $stateNames.Add([string]$state)) { throw "Shell '$entryId' contains duplicate state '$state'." }
      $stateCount++
    }
    if (@($shell.capabilities).Count -eq 0) { throw "Shell '$entryId' contains zero capabilities." }
    if (@($shell.manualChecks).Count -eq 0) { throw "Shell '$entryId' contains zero manual checks." }

    $xamlPath = Normalize-RepositoryRelativePath ([string]$shell.xamlPath)
    $codeBehindPath = Normalize-RepositoryRelativePath ([string]$shell.codeBehindPath)
    if (-not $paths.Add($xamlPath)) { throw "Accessibility preflight contains duplicate or case-colliding path '$xamlPath'." }
    if (-not $paths.Add($codeBehindPath)) { throw "Accessibility preflight contains duplicate or case-colliding path '$codeBehindPath'." }

    $operatorCases = @($shell.operatorCases)
    if ($operatorCases.Count -eq 0) { throw "Shell '$entryId' contains zero operator cases." }
    foreach ($case in $operatorCases) {
      $caseId = [string]$case.id
      Assert-RequiredText $caseId "Shell '$entryId' contains an operator case with no id."
      if (-not $caseIds.Add($caseId)) { throw "Accessibility preflight contains duplicate operator case '$caseId'." }
      foreach ($property in @("setup", "action", "expected", "report")) {
        Assert-RequiredText $case.$property "Operator case '$caseId' has no $property."
      }
      if ([string]$case.status -cne "NotTested") { throw "Operator case '$caseId' must remain NotTested before direct observation." }
      $operatorCaseCount++
    }

    if (-not [string]::IsNullOrWhiteSpace($RepositoryRoot)) {
      foreach ($relativePath in @($xamlPath, $codeBehindPath)) {
        $absolutePath = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $relativePath))
        if (-not $absolutePath.StartsWith([IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
          throw "Shell '$entryId' path escapes the repository."
        }
        if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) { throw "Shell '$entryId' has stale path '$relativePath'." }
        $item = Get-Item -LiteralPath $absolutePath -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Shell '$entryId' path is a reparse point."
        }
      }

      [xml]$xaml = Get-Content -LiteralPath (Join-Path $RepositoryRoot $xamlPath) -Raw
      $window = $xaml.DocumentElement
      $xClass = $window.GetAttribute("Class", "http://schemas.microsoft.com/winfx/2006/xaml")
      if ($xClass -cne [string]$shell.className) { throw "Shell '$entryId' class does not match its XAML root." }
      $resizeModeAttribute = $window.GetAttribute("ResizeMode")
      $actualResizeMode = if ([string]::IsNullOrWhiteSpace($resizeModeAttribute)) { "CanResize" } else { $resizeModeAttribute }
      if ([int]$window.GetAttribute("Width") -ne [int]$shell.defaultSize.width -or
          [int]$window.GetAttribute("Height") -ne [int]$shell.defaultSize.height -or
          [int]$window.GetAttribute("MinWidth") -ne [int]$shell.minimumSize.width -or
          [int]$window.GetAttribute("MinHeight") -ne [int]$shell.minimumSize.height -or
          $actualResizeMode -cne [string]$shell.resizeMode) {
        throw "Shell '$entryId' geometry or resize mode does not match its XAML root."
      }
      $xamlText = $window.OuterXml
      if ([string]$shell.chrome -ceq "custom-caption" -and $xamlText -notmatch 'WindowCaptionButtons') {
        throw "Custom-caption shell '$entryId' is missing WindowCaptionButtons."
      }
      if ([string]$shell.chrome -ceq "specialized-custom" -and $xamlText -notmatch 'ClosePublishingButton') {
        throw "Specialized shell '$entryId' is missing its dedicated caption control."
      }
    }
  }

  if (@($ExpectedInventoryNames).Count -gt 0) {
    $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $ExpectedInventoryNames) {
      if (-not $expected.Add($name)) { throw "The canonical shell inventory contains duplicate window '$name'." }
    }
    $missing = @($expected | Where-Object { -not $inventoryNames.Contains($_) })
    $extra = @($inventoryNames | Where-Object { -not $expected.Contains($_) })
    if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
      throw "Shell policy does not match the canonical inventory. Missing: $([string]::Join(', ', $missing)); extra: $([string]::Join(', ', $extra))."
    }
  }

  return [PSCustomObject]@{
    ScenarioCount = $scenarios.Count
    ControlCount = $controlCount
    ShellCount = $shells.Count
    StateCount = $stateCount
    OperatorCaseCount = $operatorCaseCount
  }
}

function New-OperatorMatrixMarkdown {
  param($Policy, [string]$Commit, [string]$Configuration)

  $lines = [Collections.Generic.List[string]]::new()
  $lines.Add("# Window-shell operator matrix")
  $lines.Add("")
  $lines.Add("Generated for commit ``$Commit`` in ``$Configuration``. This file prepares manual work; every result remains **Not tested** until an operator records a direct observation in the canonical evidence document.")
  $lines.Add("")
  $number = 0
  foreach ($shell in $Policy.shells) {
    $lines.Add("## $($shell.inventoryName)")
    $lines.Add("")
    $lines.Add("Open it: $($shell.invocation)")
    $lines.Add("")
    foreach ($case in $shell.operatorCases) {
      $number++
      $lines.Add("### $number. $($case.id)")
      $lines.Add("")
      $lines.Add("1. Setup: $($case.setup)")
      $lines.Add("2. Do: $($case.action)")
      $lines.Add("3. Expect: $($case.expected)")
      $lines.Add("4. Report: $($case.report)")
      $lines.Add("5. Current result: **NOT TESTED**")
      $lines.Add("")
    }
  }
  return [string]::Join([Environment]::NewLine, $lines)
}

function Test-HotkeyConflict {
  param([int]$Modifiers, [int]$VirtualKey, [int]$ProposedModifiers, [int]$ProposedVirtualKey)
  return $Modifiers -eq $ProposedModifiers -and $VirtualKey -eq $ProposedVirtualKey
}

function Format-Hotkey {
  param([int]$Modifiers, [int]$VirtualKey)
  $parts = [Collections.Generic.List[string]]::new()
  if (($Modifiers -band 2) -ne 0) { $parts.Add("Ctrl") }
  if (($Modifiers -band 1) -ne 0) { $parts.Add("Alt") }
  if (($Modifiers -band 4) -ne 0) { $parts.Add("Shift") }
  if (($Modifiers -band 8) -ne 0) { $parts.Add("Win") }
  if ($VirtualKey -eq 0x20) { $parts.Add("Space") }
  elseif ($VirtualKey -ge 0x41 -and $VirtualKey -le 0x5A) { $parts.Add([char]$VirtualKey) }
  else { $parts.Add(("VK 0x{0:X2}" -f $VirtualKey)) }
  return [string]::Join(" + ", $parts)
}

function Convert-ToQuotedArgument {
  param([string]$Value)
  if ($Value -notmatch '[\s"]') { return $Value }
  return '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Invoke-BoundedProcess {
  param([string]$FileName, [string[]]$Arguments, [int]$Timeout, [string]$WorkingDirectory)
  $info = [Diagnostics.ProcessStartInfo]::new()
  $info.FileName = $FileName
  $info.Arguments = [string]::Join(" ", @($Arguments | ForEach-Object { Convert-ToQuotedArgument ([string]$_) }))
  $info.WorkingDirectory = $WorkingDirectory
  $info.UseShellExecute = $false
  $info.CreateNoWindow = $true
  $info.RedirectStandardOutput = $true
  $info.RedirectStandardError = $true
  $process = [Diagnostics.Process]::new()
  $process.StartInfo = $info
  if (-not $process.Start()) { throw "Failed to start '$FileName'." }
  $stdout = $process.StandardOutput.ReadToEndAsync()
  $stderr = $process.StandardError.ReadToEndAsync()
  if (-not $process.WaitForExit($Timeout * 1000)) {
    try { $process.Kill($true) } catch { try { $process.Kill() } catch {} }
    throw "'$FileName' exceeded the bounded $Timeout-second preflight timeout."
  }
  $stdoutText = $stdout.GetAwaiter().GetResult()
  $stderrText = $stderr.GetAwaiter().GetResult()
  return [PSCustomObject]@{ ExitCode = $process.ExitCode; StandardOutput = $stdoutText; StandardError = $stderrText }
}

function Assert-NoReparseAncestor {
  param([string]$Path, [string]$Boundary)
  $boundaryPath = [IO.Path]::GetFullPath($Boundary).TrimEnd([IO.Path]::DirectorySeparatorChar)
  $cursor = [IO.Path]::GetFullPath($Path)
  if (-not $cursor.StartsWith($boundaryPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Accessibility preflight output must remain under the ignored artifacts boundary."
  }
  while ($cursor.StartsWith($boundaryPath, [StringComparison]::OrdinalIgnoreCase)) {
    if (Test-Path -LiteralPath $cursor) {
      $item = Get-Item -LiteralPath $cursor -Force
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Accessibility preflight output traverses a reparse point."
      }
    }
    if ([string]::Equals($cursor, $boundaryPath, [StringComparison]::OrdinalIgnoreCase)) { break }
    $cursor = Split-Path -Parent $cursor
  }
}

function Assert-Fails {
  param([scriptblock]$Action, [string]$Expected)
  try { & $Action; throw "Expected failure containing '$Expected'." }
  catch {
    if ($_.Exception.Message -notlike "*$Expected*") { throw "Unexpected self-test failure for '$Expected': $($_.Exception.Message)" }
  }
}

if ($SelfTest) {
  try { $currentPolicy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json }
  catch { throw "Accessibility preflight policy is malformed: $($_.Exception.Message)" }
  $currentInventory = Normalize-RepositoryRelativePath ([string]$currentPolicy.canonicalShellInventory)
  $currentInventoryNames = Get-CanonicalShellNames (Join-Path $repoRoot $currentInventory)
  $currentCounts = Assert-PreflightPolicy $currentPolicy -ExpectedInventoryNames $currentInventoryNames -RepositoryRoot $repoRoot
  if ($currentCounts.ShellCount -ne 11 -or $currentCounts.OperatorCaseCount -ne 12) {
    throw "The current shell policy did not expose the expected 11 shells and 12 operator cases."
  }

  $validJson = @'
{"schemaVersion":2,"manualEvidenceStatus":"NotTested","canonicalShellInventory":"inventory.md","scenarios":[{"id":"one","owner":"Workbench","controls":[{"name":"New chat","state":"enabled"}]}],"shells":[{"id":"one","inventoryName":"One","className":"Example.OneWindow","xamlPath":"src/One.xaml","codeBehindPath":"src/One.xaml.cs","owner":"owner","invocation":"open it","defaultSize":{"width":100,"height":100},"minimumSize":{"width":50,"height":50},"resizeMode":"CanResize","chrome":"native","lifetime":"modal","focusOwner":"focus","themeOwner":"theme","capabilities":["close"],"states":["normal"],"manualChecks":["focus"],"operatorCases":[{"id":"CASE-01","setup":"setup","action":"act","expected":"expected","report":"report","status":"NotTested"}]}]}
'@
  $valid = $validJson | ConvertFrom-Json
  $result = Assert-PreflightPolicy $valid -ExpectedInventoryNames @("One")
  if ($result.ScenarioCount -ne 1 -or $result.ControlCount -ne 1 -or $result.ShellCount -ne 1 -or $result.StateCount -ne 1 -or $result.OperatorCaseCount -ne 1) {
    throw "Valid policy self-test returned wrong counts."
  }
  Assert-Fails { Assert-PreflightPolicy (($validJson -replace '"schemaVersion":2', '"schemaVersion":0') | ConvertFrom-Json) } "schemaVersion"
  Assert-Fails { Assert-PreflightPolicy (($validJson -replace '"manualEvidenceStatus":"NotTested"', '"manualEvidenceStatus":"Passed"') | ConvertFrom-Json) } "NotTested"
  Assert-Fails { Assert-PreflightPolicy (($validJson -replace '"scenarios":\[\{.*?\}\],"shells"', '"scenarios":[],"shells"') | ConvertFrom-Json) } "zero scenarios"
  Assert-Fails { Assert-PreflightPolicy (($validJson -replace '"controls":\[\{"name":"New chat","state":"enabled"\}\]', '"controls":[]') | ConvertFrom-Json) } "zero controls"
  Assert-Fails { Assert-PreflightPolicy (($validJson -replace '"state":"enabled"', '"state":"passed"') | ConvertFrom-Json) } "invalid state"
  Assert-Fails { Assert-PreflightPolicy (($validJson -replace '"shells":\[\{.*\}\]\}', '"shells":[]}') | ConvertFrom-Json) } "zero shells"
  Assert-Fails { Assert-PreflightPolicy (($validJson -replace '"states":\["normal"\]', '"states":[]') | ConvertFrom-Json) } "zero states"
  Assert-Fails { Assert-PreflightPolicy (($validJson -replace '"minimumSize":\{"width":50', '"minimumSize":{"width":0') | ConvertFrom-Json) } "minimum dimension"
  Assert-Fails { Assert-PreflightPolicy (($validJson -replace '"xamlPath":"src/One.xaml"', '"xamlPath":"../One.xaml"') | ConvertFrom-Json) } "repository-relative"
  Assert-Fails { Assert-PreflightPolicy (($validJson -replace '"status":"NotTested"', '"status":"Passed"') | ConvertFrom-Json) } "must remain NotTested"
  Assert-Fails { Assert-PreflightPolicy $valid -ExpectedInventoryNames @("Different") } "does not match"
  $duplicateShell = $validJson -replace '\]\}\s*$', ',{"id":"ONE","inventoryName":"Two","className":"Example.TwoWindow","xamlPath":"src/Two.xaml","codeBehindPath":"src/Two.xaml.cs","owner":"owner","invocation":"open","defaultSize":{"width":100,"height":100},"minimumSize":{"width":50,"height":50},"resizeMode":"CanResize","chrome":"native","lifetime":"modal","focusOwner":"focus","themeOwner":"theme","capabilities":["close"],"states":["normal"],"manualChecks":["focus"],"operatorCases":[{"id":"CASE-02","setup":"setup","action":"act","expected":"expected","report":"report","status":"NotTested"}]}]}'
  Assert-Fails { Assert-PreflightPolicy ($duplicateShell | ConvertFrom-Json) } "duplicate shell id"
  $duplicatePath = $validJson -replace '"codeBehindPath":"src/One.xaml.cs"', '"codeBehindPath":"SRC\\ONE.XAML"'
  Assert-Fails { Assert-PreflightPolicy ($duplicatePath | ConvertFrom-Json) } "case-colliding path"
  $duplicateCase = $validJson -replace '\]\}\s*$', ',{"id":"two","inventoryName":"Two","className":"Example.TwoWindow","xamlPath":"src/Two.xaml","codeBehindPath":"src/Two.xaml.cs","owner":"owner","invocation":"open","defaultSize":{"width":100,"height":100},"minimumSize":{"width":50,"height":50},"resizeMode":"CanResize","chrome":"native","lifetime":"modal","focusOwner":"focus","themeOwner":"theme","capabilities":["close"],"states":["normal"],"manualChecks":["focus"],"operatorCases":[{"id":"case-01","setup":"setup","action":"act","expected":"expected","report":"report","status":"NotTested"}]}]}'
  Assert-Fails { Assert-PreflightPolicy ($duplicateCase | ConvertFrom-Json) } "duplicate operator case"
  Assert-Fails { Normalize-RepositoryRelativePath "C:\\outside.xaml" } "repository-relative"
  if (-not (Test-HotkeyConflict 1 32 1 32)) { throw "Alt+Space collision was not detected." }
  if (Test-HotkeyConflict 2 32 1 32) { throw "A nonmatching chord was reported as a collision." }
  if ((Format-Hotkey 1 32) -cne "Alt + Space") { throw "Hotkey formatting was not deterministic." }
  $matrix = New-OperatorMatrixMarkdown $valid "0123456789012345678901234567890123456789" "Release"
  if ($matrix -notmatch 'Current result: \*\*NOT TESTED\*\*' -or $matrix -notmatch '1\. CASE-01') {
    throw "Operator-matrix generation did not retain the manual Not tested contract."
  }
  Write-Host "Accessibility preflight self-tests passed: 19/19; current policy has $($currentCounts.ShellCount) shells, $($currentCounts.StateCount) states, and $($currentCounts.OperatorCaseCount) operator cases."
  return
}

if ([string]::IsNullOrWhiteSpace($ExpectedCommit) -or $ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') {
  throw "ExpectedCommit must be the exact 40-character commit under test."
}
$ExpectedCommit = $ExpectedCommit.ToLowerInvariant()
$head = (& git -C $repoRoot rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $head -cne $ExpectedCommit) { throw "HEAD '$head' does not match ExpectedCommit '$ExpectedCommit'." }
$status = @(& git -C $repoRoot status --short)
if ($LASTEXITCODE -ne 0) { throw "git status failed." }
if ($status.Count -ne 0) { throw "Accessibility preflight requires a clean worktree." }

try { $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json }
catch { throw "Accessibility preflight policy is malformed: $($_.Exception.Message)" }
$inventoryRelative = Normalize-RepositoryRelativePath ([string]$policy.canonicalShellInventory)
$inventoryPath = Join-Path $repoRoot $inventoryRelative
$canonicalShellNames = Get-CanonicalShellNames $inventoryPath
$counts = Assert-PreflightPolicy $policy -ExpectedInventoryNames $canonicalShellNames -RepositoryRoot $repoRoot

$appRelative = "src/DictateAnywhere.App/bin/Release/net8.0-windows/DictateAnywhere.App.exe"
$appPath = Join-Path $repoRoot $appRelative
if (-not (Test-Path -LiteralPath $appPath -PathType Leaf)) { throw "The exact Release executable is missing: $appRelative" }
$productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($appPath).ProductVersion
if ($productVersion -notlike "*+$ExpectedCommit*") {
  throw "The Release executable does not identify commit '$ExpectedCommit'; found '$productVersion'."
}

$settingsPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "DictateAnywhere/settings.json"
$hotkeySource = "application-default"
$hotkeyModifiers = 1
$hotkeyVirtualKey = 32
if (Test-Path -LiteralPath $settingsPath -PathType Leaf) {
  try { $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json }
  catch { throw "The configured hotkey cannot be proven because the settings document is malformed." }
  if ($null -eq $settings.PSObject.Properties["hotkeyModifiers"] -or $null -eq $settings.PSObject.Properties["hotkeyVirtualKey"]) {
    throw "The configured hotkey cannot be proven because its fields are missing."
  }
  $hotkeyModifiers = [int]$settings.hotkeyModifiers
  $hotkeyVirtualKey = [int]$settings.hotkeyVirtualKey
  $hotkeySource = "current-settings"
}
$hotkeyDisplay = Format-Hotkey $hotkeyModifiers $hotkeyVirtualKey
$altSpaceConflict = Test-HotkeyConflict $hotkeyModifiers $hotkeyVirtualKey 1 32

$narratorPath = Join-Path $env:WINDIR "System32/Narrator.exe"
$narratorAvailable = Test-Path -LiteralPath $narratorPath -PathType Leaf

$artifactsBoundary = Join-Path $repoRoot "artifacts"
$artifactRoot = Join-Path $artifactsBoundary ("accessibility-preflight/{0}" -f [Guid]::NewGuid().ToString("N"))
if (Test-Path -LiteralPath $artifactRoot) { throw "Generated preflight directory already exists." }
Assert-NoReparseAncestor -Path $artifactRoot -Boundary $artifactsBoundary
New-Item -ItemType Directory -Path $artifactRoot | Out-Null
$testProject = Join-Path $repoRoot "tests/DictateAnywhere.App.Tests/DictateAnywhere.App.Tests.csproj"
$filter = "FullyQualifiedName~WindowShellPrimitivesTests|FullyQualifiedName~WindowShellPreflightTests|FullyQualifiedName~AutomatedAccessibilityVerificationTests|FullyQualifiedName~AccessibilityFocusAutomationTests|FullyQualifiedName~ReaderAccessibilityRegressionTests|FullyQualifiedName~ReaderDocumentLayoutTests|FullyQualifiedName~StaticContentKeyboardScrollingTests|FullyQualifiedName~AppThemeManagerTests|FullyQualifiedName~WorkbenchChatTranscriptViewTests|FullyQualifiedName~WorkbenchPresentationReducerTests|FullyQualifiedName~SettingsPanelTests|FullyQualifiedName~HistoryWindow|FullyQualifiedName~ReaderWindowInitializationTests|FullyQualifiedName~YouTubePublishingTests|FullyQualifiedName~FirstRunWizardGuardTests|FullyQualifiedName~Hotkey"
$common = @("test", $testProject, "--configuration", $Configuration, "--no-build", "--no-restore", "--nologo", "--filter", $filter)
$discovery = Invoke-BoundedProcess "dotnet" ($common + "--list-tests") $TimeoutSeconds $repoRoot
if ($discovery.ExitCode -ne 0) { throw "Accessibility preflight discovery failed with exit code $($discovery.ExitCode)." }
$discovered = @($discovery.StandardOutput -split "`r?`n" | Where-Object { $_ -match '^\s+DictateAnywhere\.App\.Tests\.' }).Count
if ($discovered -eq 0) { throw "Accessibility preflight discovered zero tests." }

$run = Invoke-BoundedProcess "dotnet" ($common + @("--logger", "trx;LogFileName=preflight.trx", "--results-directory", $artifactRoot, "--blame-hang-timeout", "2m")) $TimeoutSeconds $repoRoot
if ($run.ExitCode -ne 0) { throw "Accessibility preflight tests failed with exit code $($run.ExitCode)." }
$trxPath = Join-Path $artifactRoot "preflight.trx"
if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) { throw "Accessibility preflight test evidence is missing." }
[xml]$trx = Get-Content -LiteralPath $trxPath -Raw
$counters = $trx.TestRun.ResultSummary.Counters
$executed = [int]$counters.executed
$passed = [int]$counters.passed
$failed = [int]$counters.failed
if ($executed -le 0 -or $passed -ne $executed -or $failed -ne 0) { throw "Accessibility preflight test evidence is empty or unsuccessful." }
if ($executed -ne $discovered) { throw "Accessibility preflight executed $executed of $discovered discovered tests." }

foreach ($scenario in $policy.scenarios) {
  Write-Host "[$($scenario.id)] $($scenario.owner)"
  foreach ($control in $scenario.controls) { Write-Host "  - $($control.name): $($control.state)" }
}
foreach ($shell in $policy.shells) {
  Write-Host "[shell:$($shell.id)] $($shell.inventoryName) -- $($shell.chrome), $($shell.resizeMode), $(@($shell.states).Count) states"
}
Write-Host "Narrator executable: $(if ($narratorAvailable) { 'available (not started)' } else { 'unavailable; manual screen-reader case remains Blocked' })"
Write-Host "Configured dictation hotkey: $hotkeyDisplay"
if ($altSpaceConflict) { Write-Warning "Alt+Space conflicts with the configured dictation hotkey; do not use it for the Windows system menu." }

$report = [ordered]@{
  schemaVersion = 1
  commit = $head
  configuration = $Configuration
  executable = [ordered]@{ path = $appRelative; productVersion = $productVersion }
  automatedTests = [ordered]@{ discovered = $discovered; executed = $executed; passed = $passed; failed = $failed }
  scenarios = [ordered]@{ count = $counts.ScenarioCount; controlExpectations = $counts.ControlCount }
  shells = [ordered]@{ count = $counts.ShellCount; states = $counts.StateCount; operatorCases = $counts.OperatorCaseCount; inventory = $inventoryRelative }
  narrator = [ordered]@{ executableAvailable = $narratorAvailable; started = $false; manualResult = "NotTested" }
  dictationHotkey = [ordered]@{ source = $hotkeySource; display = $hotkeyDisplay; altSpaceConflict = $altSpaceConflict }
  manualEvidenceStatus = "NotTested"
}
$reportPath = Join-Path $artifactRoot "preflight-report.json"
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
$operatorMatrixPath = Join-Path $artifactRoot "window-shell-operator-matrix.md"
New-OperatorMatrixMarkdown $policy $head $Configuration | Set-Content -LiteralPath $operatorMatrixPath -Encoding UTF8
try { $null = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json }
catch { throw "Generated accessibility preflight report is malformed." }
$matrixText = Get-Content -LiteralPath $operatorMatrixPath -Raw
if ([string]::IsNullOrWhiteSpace($matrixText) -or $matrixText -notmatch 'Current result: \*\*NOT TESTED\*\*') {
  throw "Generated operator matrix is empty or malformed."
}
Write-Host "Accessibility preflight passed: $executed/$executed automated tests; $($counts.ScenarioCount) scenarios; $($counts.ControlCount) control expectations; $($counts.ShellCount) shells; $($counts.StateCount) shell states; $($counts.OperatorCaseCount) operator cases."
Write-Host "Manual accessibility status remains NotTested."
Write-Host "Sanitized report: artifacts/accessibility-preflight/<run>/preflight-report.json"
Write-Host "Operator matrix: artifacts/accessibility-preflight/<run>/window-shell-operator-matrix.md"
