[CmdletBinding()]
param(
  [string]$OutputPath = "artifacts/supply-chain/compliance.json",
  [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$manifestPath = Join-Path $repoRoot "docs\security\supply-chain-provenance.json"
$shaPattern = "^[a-fA-F0-9]{64}$"
$revisionPattern = "^[a-fA-F0-9]{40}$"
$allowedRemoteHosts = @(
  "api.nuget.org", "www.nuget.org", "pypi.org", "files.pythonhosted.org",
  "github.com", "codeload.github.com", "raw.githubusercontent.com", "release-assets.githubusercontent.com",
  "download-r2.pytorch.org", "download.pytorch.org", "huggingface.co",
  "www.gyan.dev", "www.python.org", "ollama.com", "registry.ollama.ai"
)

function Read-JsonFile {
  param([Parameter(Mandatory)][string]$Path)
  if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required JSON file is missing: $Path" }
  try { return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
  catch { throw "Required JSON file is malformed: $Path" }
}

function Assert-Sha256 {
  param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string]$Identity)
  if ($Value -notmatch $shaPattern -or $Value -match "^0{64}$") { throw "'$Identity' has an invalid SHA-256." }
}

function Assert-ImmutableRevision {
  param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string]$Identity)
  if ($Value -notmatch $revisionPattern) { throw "'$Identity' does not use a full immutable revision." }
}

function Assert-RemoteUri {
  param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string]$Identity)
  $uri = $null
  if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne "https") {
    throw "'$Identity' does not use an absolute HTTPS URI."
  }
  if (-not [string]::IsNullOrEmpty($uri.UserInfo)) { throw "'$Identity' URI contains user information." }
  if ($allowedRemoteHosts -notcontains $uri.IdnHost.ToLowerInvariant()) { throw "'$Identity' uses unapproved host '$($uri.IdnHost)'." }
  if ($uri.AbsolutePath -match "(?i)(^|/)(latest|main|master)(/|$)") { throw "'$Identity' uses a mutable remote path." }
}

function Resolve-OwnedPath {
  param([Parameter(Mandatory)][string]$RelativePath)
  if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) {
    throw "Repository path must be a non-empty relative path: '$RelativePath'."
  }
  $segments = $RelativePath.Replace('\', '/').Split('/')
  if ($segments | Where-Object { $_ -in @("", ".", "..") }) { throw "Repository path escapes its owner: '$RelativePath'." }
  $absolute = [IO.Path]::GetFullPath((Join-Path $repoRoot $RelativePath))
  $rootPrefix = $repoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  if (-not $absolute.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Repository path escapes its owner: '$RelativePath'." }
  return $absolute
}

function Get-RepositoryRelativePath {
  param([Parameter(Mandatory)][string]$Path)
  $absolute = [IO.Path]::GetFullPath($Path)
  $rootPrefix = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
  if (-not $absolute.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Path is outside the repository boundary: $absolute"
  }
  return $absolute.Substring($rootPrefix.Length).Replace('\', '/')
}

function Assert-NoReparsePoint {
  param([Parameter(Mandatory)][string]$Path)
  $item = Get-Item -LiteralPath $Path -Force
  if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse points are forbidden in provenance inputs: $($item.FullName)" }
  $current = if ($item.PSIsContainer) { $item } else { $item.Directory }
  while ($null -ne $current -and $current.FullName.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
    if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse points are forbidden in provenance inputs: $($current.FullName)" }
    $current = $current.Parent
  }
}

function Assert-UniqueIds {
  param([Parameter(Mandatory)][object[]]$Items, [Parameter(Mandatory)][string]$Category)
  if ($Items.Count -eq 0) { throw "Supply-chain category '$Category' contains zero entries." }
  $ids = @($Items | ForEach-Object { [string]$_.id })
  if ($ids | Where-Object { [string]::IsNullOrWhiteSpace($_) }) { throw "Supply-chain category '$Category' contains an empty id." }
  if (($ids | Sort-Object -Unique).Count -ne ($ids | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object -Unique).Count) {
    throw "Supply-chain category '$Category' contains case-colliding ids."
  }
  if (($ids | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object -Unique).Count -ne $ids.Count) {
    throw "Supply-chain category '$Category' contains duplicate ids."
  }
}

function Assert-Consumers {
  param([Parameter(Mandatory)][object]$Item)
  $consumers = @()
  $consumerProperty = $Item.PSObject.Properties["consumer"]
  $consumersProperty = $Item.PSObject.Properties["consumers"]
  if ($null -ne $consumerProperty) { $consumers += [string]$consumerProperty.Value }
  if ($null -ne $consumersProperty) { $consumers += @($consumersProperty.Value | ForEach-Object { [string]$_ }) }
  $consumers = @($consumers | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
  if ($consumers.Count -eq 0) { throw "'$($Item.id)' has no real consumer." }
  if (@($consumers | ForEach-Object { $_.ToLowerInvariant() } | Sort-Object -Unique).Count -ne $consumers.Count) {
    throw "'$($Item.id)' has duplicate or case-colliding consumers."
  }
  foreach ($consumer in $consumers) {
    $path = Resolve-OwnedPath $consumer
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "'$($Item.id)' consumer is missing: $consumer" }
  }
}

function Assert-PythonLock {
  param([Parameter(Mandatory)][string]$RelativePath)
  $path = Resolve-OwnedPath $RelativePath
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Python lock is missing: $RelativePath" }
  Assert-NoReparsePoint $path
  $raw = Get-Content -LiteralPath $path -Raw
  if ($raw -match "(?m)^\s*-e\s|(?i)git\+|(?m)^[^#\r\n]*(>=|<=|~=|!=|===|(?<![=])>(?![=])|(?<![=])<(?![=]))") {
    throw "Python lock '$RelativePath' contains a mutable or editable requirement."
  }
  $logical = $raw -replace "\\\r?\n\s*", " "
  $requirements = @($logical -split "\r?\n" | Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith("#") })
  if ($requirements.Count -eq 0) { throw "Python lock '$RelativePath' contains zero requirements." }
  foreach ($requirement in $requirements) {
    if ($requirement -notmatch "(?i)--hash=sha256:[a-f0-9]{64}") { throw "Python lock '$RelativePath' contains an unhashed requirement: $requirement" }
    if ($requirement -notmatch "==|\s@\shttps://") { throw "Python lock '$RelativePath' contains an unpinned requirement: $requirement" }
    foreach ($url in [regex]::Matches($requirement, "https://[^\s]+") | ForEach-Object Value) {
      Assert-RemoteUri $url $RelativePath
    }
  }
  return $requirements.Count
}

function ConvertTo-NormalizedPythonPackageName {
  param([Parameter(Mandatory)][string]$Name)
  return ([regex]::Replace($Name.Trim(), "[-_.]+", "-")).ToLowerInvariant()
}

function Get-PythonLockRequirements {
  param([Parameter(Mandatory)][string]$RelativePath)
  $path = Resolve-OwnedPath $RelativePath
  $raw = Get-Content -LiteralPath $path -Raw
  $logical = $raw -replace "\\\r?\n\s*", " "
  return @($logical -split "\r?\n" | Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith("#") })
}

function Assert-PythonInventoryMatchesLocks {
  param(
    [Parameter(Mandatory)][string[]]$LockPaths,
    [Parameter(Mandatory)][object[]]$InventoryPackages
  )

  $inventoryByIdentity = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $inventoryByName = @{}
  foreach ($package in $InventoryPackages) {
    $normalizedName = ConvertTo-NormalizedPythonPackageName ([string]$package.name)
    $identity = "$normalizedName/$($package.version)"
    [void]$inventoryByIdentity.Add($identity)
    if (-not $inventoryByName.ContainsKey($normalizedName)) { $inventoryByName[$normalizedName] = @() }
    $inventoryByName[$normalizedName] += $package
  }

  $lockedIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($lockPath in $LockPaths) {
    foreach ($requirement in Get-PythonLockRequirements $lockPath) {
      $trimmed = $requirement.Trim()
      $pinned = [regex]::Match($trimmed, "^(?<name>[A-Za-z0-9_.-]+)(?:\[[^\]]+\])?==(?<version>[^\s;\\]+)")
      if ($pinned.Success) {
        $name = ConvertTo-NormalizedPythonPackageName $pinned.Groups["name"].Value
        [void]$lockedIdentities.Add("$name/$($pinned.Groups["version"].Value)")
        continue
      }

      $direct = [regex]::Match($trimmed, "^(?<name>[A-Za-z0-9_.-]+)(?:\[[^\]]+\])?\s+@\s+(?<url>https://[^\s]+)")
      if (-not $direct.Success) { throw "Python lock '$lockPath' contains an unrecognized requirement identity: $trimmed" }
      $name = ConvertTo-NormalizedPythonPackageName $direct.Groups["name"].Value
      $url = $direct.Groups["url"].Value
      $candidates = @($inventoryByName[$name])
      if ($candidates.Count -eq 0) { throw "Python lock '$lockPath' contains a direct package absent from provenance: $name" }

      $sourceMatches = @($candidates | Where-Object { [string]$_.source -eq $url })
      if ($sourceMatches.Count -eq 1) {
        [void]$lockedIdentities.Add("$name/$($sourceMatches[0].version)")
        continue
      }

      $uri = [Uri]$url
      $fileName = [Uri]::UnescapeDataString([IO.Path]::GetFileName($uri.AbsolutePath))
      $wheelMatches = @($candidates | Where-Object {
        $version = [regex]::Escape([string]$_.version)
        $fileName -match "(?i)^.+-$version(?:\+[^-]+)?-.*\.whl$"
      })
      if ($wheelMatches.Count -ne 1) {
        throw "Python lock '$lockPath' direct URL cannot be mapped uniquely to provenance: $name"
      }
      [void]$lockedIdentities.Add("$name/$($wheelMatches[0].version)")
    }
  }

  $missing = @($lockedIdentities | Where-Object { -not $inventoryByIdentity.Contains($_) } | Sort-Object)
  $extra = @($inventoryByIdentity | Where-Object { -not $lockedIdentities.Contains($_) } | Sort-Object)
  if ($missing.Count -gt 0 -or $extra.Count -gt 0) {
    throw "Python package provenance does not match the exact locked graph. Missing: $($missing -join ', '); Extra: $($extra -join ', ')"
  }
}

function Invoke-SupplyChainValidation {
  $manifest = Read-JsonFile $manifestPath
  if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported supply-chain provenance schema." }

  $models = @($manifest.models); $runtimes = @($manifest.runtimes); $assets = @($manifest.assets)
  $actions = @($manifest.ciActions); $python = @($manifest.python); $credentials = @($manifest.credentials)
  Assert-UniqueIds $models "models"; Assert-UniqueIds $runtimes "runtimes"; Assert-UniqueIds $assets "assets"
  Assert-UniqueIds $actions "ciActions"; Assert-UniqueIds $python "python"; Assert-UniqueIds $credentials "credentials"

  $allIds = @($models + $runtimes + $assets + $actions + $python + $credentials | ForEach-Object { ([string]$_.id).ToLowerInvariant() })
  if (($allIds | Sort-Object -Unique).Count -ne $allIds.Count) { throw "Supply-chain artifact ids must be globally unique." }

  foreach ($item in @($models + $runtimes + $python)) { Assert-Consumers $item }
  foreach ($item in @($models + $runtimes)) {
    if ([string]::IsNullOrWhiteSpace([string]$item.license)) { throw "'$($item.id)' has no license disposition." }
    if ([string]::IsNullOrWhiteSpace([string]$item.integrity)) { throw "'$($item.id)' has no integrity mechanism." }
    $sourceProperty = $item.PSObject.Properties["source"]
    $repositoryProperty = $item.PSObject.Properties["repository"]
    $shaProperty = $item.PSObject.Properties["sha256"]
    $revisionProperty = $item.PSObject.Properties["revision"]
    if ($null -ne $sourceProperty -and -not [string]::IsNullOrWhiteSpace([string]$sourceProperty.Value)) { Assert-RemoteUri ([string]$sourceProperty.Value) ([string]$item.id) }
    if (($null -eq $sourceProperty -or [string]::IsNullOrWhiteSpace([string]$sourceProperty.Value)) -and
        ($null -eq $repositoryProperty -or [string]::IsNullOrWhiteSpace([string]$repositoryProperty.Value))) {
      throw "'$($item.id)' has no source or repository identity."
    }
    if ($null -ne $shaProperty -and -not [string]::IsNullOrWhiteSpace([string]$shaProperty.Value)) { Assert-Sha256 ([string]$shaProperty.Value) ([string]$item.id) }
    if ($null -ne $revisionProperty -and ([string]$revisionProperty.Value) -notmatch "^sha256:") { Assert-ImmutableRevision ([string]$revisionProperty.Value) ([string]$item.id) }
    $redirectHostsProperty = $item.PSObject.Properties["allowedRedirectHosts"]
    if ($null -ne $redirectHostsProperty) {
      $redirectHosts = @($redirectHostsProperty.Value | ForEach-Object { ([string]$_).ToLowerInvariant() })
      if ($redirectHosts.Count -eq 0 -or @($redirectHosts | Sort-Object -Unique).Count -ne $redirectHosts.Count) { throw "'$($item.id)' has an empty or duplicate redirect-host policy." }
      foreach ($hostName in $redirectHosts) {
        if ($allowedRemoteHosts -notcontains $hostName) { throw "'$($item.id)' permits an unapproved redirect host '$hostName'." }
      }
    }
  }

  $lockCount = 0
  $pythonLockPaths = @()
  foreach ($runtime in $python) {
    $pythonLockPaths += [string]$runtime.lock
    $lockCount += Assert-PythonLock ([string]$runtime.lock)
    $backendLocksProperty = $runtime.PSObject.Properties["backendLocks"]
    if ($null -ne $backendLocksProperty) {
      foreach ($backendLock in @($backendLocksProperty.Value)) {
        $pythonLockPaths += [string]$backendLock
        $lockCount += Assert-PythonLock ([string]$backendLock)
      }
    }
  }
  $pythonInventory = Read-JsonFile (Resolve-OwnedPath ([string]$manifest.pythonPackageInventory.path))
  $pythonPackages = @($pythonInventory.packages)
  if ($pythonPackages.Count -ne [int]$manifest.pythonPackageInventory.expectedPackages) { throw "Python package inventory count changed." }
  $pythonIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($package in $pythonPackages) {
    $pythonLicense = [string]$package.license
    if ([string]::IsNullOrWhiteSpace([string]$package.name) -or
        [string]::IsNullOrWhiteSpace([string]$package.version) -or
        [string]::IsNullOrWhiteSpace($pythonLicense) -or
        $pythonLicense -match "^(System\.|NOASSERTION|UNKNOWN)$") {
      throw "Python package provenance contains a missing identity, version, or license."
    }
    $pythonIdentity = "$(ConvertTo-NormalizedPythonPackageName ([string]$package.name))/$($package.version)"
    if (-not $pythonIdentities.Add($pythonIdentity)) { throw "Python package provenance contains a duplicate normalized identity." }
    Assert-RemoteUri ([string]$package.source) ("python:" + [string]$package.name)
  }
  Assert-PythonInventoryMatchesLocks $pythonLockPaths $pythonPackages

  $projects = @(Get-ChildItem (Join-Path $repoRoot "src"), (Join-Path $repoRoot "tools"), (Join-Path $repoRoot "tests") -Recurse -File -Filter "*.csproj" | Where-Object {
    (Get-RepositoryRelativePath $_.FullName) -notmatch "(^|/)(bin|obj|TestResults|artifacts|coverage)(/|$)" -and $_.Name -notlike "*_wpftmp.csproj"
  })
  if ($projects.Count -ne [int]$manifest.nuget.expectedProjectLocks) { throw "Tracked project count changed without provenance reconciliation." }
  $locks = @($projects | ForEach-Object {
    $path = Join-Path $_.DirectoryName "packages.lock.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "NuGet lock is missing for $($_.FullName)." }
    $path
  })
  $sourceProjects = @($projects | Where-Object { $_.FullName.StartsWith((Join-Path $repoRoot "src") + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) })
  $publishRidLocks = @($sourceProjects | ForEach-Object {
    $path = Join-Path $_.DirectoryName "packages.win-x64.lock.json"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "win-x64 publish lock is missing for $($_.FullName)." }
    $path
  })
  if ($publishRidLocks.Count -ne [int]$manifest.nuget.expectedPublishRidLocks) { throw "win-x64 publish lock count changed without provenance reconciliation." }
  $locks += $publishRidLocks
  $nugetPackages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($lock in $locks) {
    $document = Read-JsonFile $lock
    if ([int]$document.version -lt 1) { throw "NuGet lock has an invalid schema: $lock" }
    foreach ($framework in $document.dependencies.PSObject.Properties) {
      foreach ($dependency in $framework.Value.PSObject.Properties) {
        if ([string]$dependency.Value.type -ne "Project") {
          if ([string]::IsNullOrWhiteSpace([string]$dependency.Value.resolved) -or [string]::IsNullOrWhiteSpace([string]$dependency.Value.contentHash)) {
            throw "NuGet lock contains an unverified package: $($dependency.Name)"
          }
          [void]$nugetPackages.Add("$($dependency.Name)/$($dependency.Value.resolved)")
        }
      }
    }
  }
  foreach ($lock in $publishRidLocks) {
    $document = Read-JsonFile $lock
    if (@($document.dependencies.PSObject.Properties | Where-Object { $_.Name -like "*/win-x64" }).Count -ne 1) {
      throw "win-x64 publish lock has no unique RID graph: $lock"
    }
  }
  if ($nugetPackages.Count -ne [int]$manifest.nuget.expectedResolvedPackages) { throw "Resolved NuGet package graph changed." }
  $nugetInventory = Read-JsonFile (Resolve-OwnedPath ([string]$manifest.nuget.resolvedInventory))
  if (@($nugetInventory.packages).Count -ne $nugetPackages.Count) { throw "NuGet license provenance does not cover the resolved graph." }
  $nugetInventoryIdentities = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($package in @($nugetInventory.packages)) {
    $license = [string]$package.license
    $identity = "$($package.id)/$($package.version)"
    if ((-not $nugetPackages.Contains($identity)) -or
        [string]::IsNullOrWhiteSpace($license) -or
        $license -match "^(System\.|NOASSERTION|UNKNOWN)$") {
      throw "NuGet provenance contains a missing or unowned package."
    }
    if (-not $nugetInventoryIdentities.Add($identity)) { throw "NuGet provenance contains a duplicate normalized identity." }
    Assert-RemoteUri ([string]$package.source) ("nuget:" + $identity)
    if ([string]::IsNullOrWhiteSpace([string]$package.repository)) { throw "NuGet provenance has no repository/source record: $identity" }
  }

  $snapshot = Read-JsonFile (Resolve-OwnedPath "scripts/local-models/model-snapshot-provenance.json")
  foreach ($artifact in @($snapshot.artifacts)) {
    Assert-ImmutableRevision ([string]$artifact.revision) ([string]$artifact.id)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in @($artifact.files)) {
      $relative = [string]$file.path
      if ([IO.Path]::IsPathRooted($relative) -or $relative.Replace('\', '/').Split('/') | Where-Object { $_ -in @("", ".", "..") }) { throw "Snapshot provenance contains an unsafe path." }
      if (-not $seen.Add($relative)) { throw "Snapshot provenance contains case-colliding paths." }
      Assert-Sha256 ([string]$file.sha256) ("$($artifact.id):$relative")
      if ([long]$file.size -le 0) { throw "Snapshot provenance contains an empty file." }
    }
    if ($seen.Count -eq 0) { throw "Snapshot provenance contains zero files." }
  }

  foreach ($asset in $assets) {
    $path = Resolve-OwnedPath ([string]$asset.path)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Tracked supply-chain asset is missing: $($asset.path)" }
    if ([string]::IsNullOrWhiteSpace([string]$asset.license)) { throw "Tracked supply-chain asset has no license disposition: $($asset.id)" }
    Assert-NoReparsePoint $path; Assert-Sha256 ([string]$asset.sha256) ([string]$asset.id)
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($actual -ne [string]$asset.sha256) { throw "Tracked supply-chain asset hash mismatch: $($asset.id)" }
    $expectedEntriesProperty = $asset.PSObject.Properties["expectedEntries"]
    if ($null -ne $expectedEntriesProperty) {
      $voiceManifest = Read-JsonFile $path
      $previews = @($voiceManifest.previews)
      if ($previews.Count -ne [int]$expectedEntriesProperty.Value) { throw "Voice-preview manifest membership changed." }
      $previewDirectory = Split-Path -Parent $path
      $previewNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
      foreach ($preview in $previews) {
        $fileName = [string]$preview.fileName
        if ([string]::IsNullOrWhiteSpace($fileName) -or
            [IO.Path]::IsPathRooted($fileName) -or
            $fileName -ne [IO.Path]::GetFileName($fileName) -or
            [IO.Path]::GetExtension($fileName) -ne ".wav" -or
            -not $previewNames.Add($fileName)) {
          throw "Voice-preview manifest contains an unsafe, duplicate, or case-colliding filename."
        }
        Assert-Sha256 ([string]$preview.sha256) ("voice-preview:" + $fileName)
        $previewPath = Join-Path $previewDirectory $fileName
        if (-not (Test-Path -LiteralPath $previewPath -PathType Leaf)) { throw "Voice-preview asset is missing: $fileName" }
        Assert-NoReparsePoint $previewPath
        $previewHash = (Get-FileHash -LiteralPath $previewPath -Algorithm SHA256).Hash
        if ($previewHash -ne [string]$preview.sha256) { throw "Voice-preview asset hash mismatch: $fileName" }
      }
      $actualPreviewNames = @(Get-ChildItem -LiteralPath $previewDirectory -File -Filter "*.wav" | ForEach-Object Name)
      if ($actualPreviewNames.Count -ne $previewNames.Count -or @($actualPreviewNames | Where-Object { -not $previewNames.Contains($_) }).Count -ne 0) {
        throw "Voice-preview directory contains files outside the exact manifest membership."
      }
    }
  }

  $workflowPath = Join-Path $repoRoot ".github\workflows\ci.yml"
  $workflow = Get-Content -LiteralPath $workflowPath -Raw
  foreach ($action in $actions) {
    Assert-ImmutableRevision ([string]$action.revision) ([string]$action.id)
    if ([string]::IsNullOrWhiteSpace([string]$action.license) -or [string]::IsNullOrWhiteSpace([string]$action.versionLabel)) { throw "CI action provenance is incomplete: $($action.id)" }
    $expected = "$($action.uses)@$($action.revision)"
    if ($workflow.IndexOf($expected, [StringComparison]::Ordinal) -lt 0) { throw "CI action is missing its pinned identity: $expected" }
  }
  if ($workflow -match "(?m)^\s*uses:\s*[^@\s]+@(v\d+|main|master|latest)\s*(#.*)?$") { throw "CI contains a mutable action identity." }

  $bundledManifestPath = Resolve-OwnedPath ([string]$manifest.bundledBinaries.manifest)
  $bundled = Read-JsonFile $bundledManifestPath
  $bundledBinaries = @($bundled.binaries)
  if ($bundledBinaries.Count -ne [int]$manifest.bundledBinaries.expectedEntries) { throw "Bundled-binary inventory count changed without reconciliation." }
  foreach ($binary in $bundledBinaries) {
    Assert-Sha256 ([string]$binary.sha256) ([string]$binary.relativePath)
    $binaryPath = Resolve-OwnedPath ([string]$binary.relativePath)
    if (-not (Test-Path -LiteralPath $binaryPath -PathType Leaf)) {
      if ([bool]$binary.required) { throw "Required bundled binary is missing: $($binary.relativePath)" }
      continue
    }
    Assert-NoReparsePoint $binaryPath
    if ((Get-FileHash -LiteralPath $binaryPath -Algorithm SHA256).Hash -ne [string]$binary.sha256) { throw "Bundled binary hash mismatch: $($binary.relativePath)" }
  }

  $sensitiveText = Get-Content -LiteralPath $manifestPath -Raw
  if ($sensitiveText -match "(?i)(ghp_[a-z0-9]{20,}|hf_[a-z0-9]{20,}|bearer\s+[a-z0-9._-]{20,}|password\s*[:=]\s*[^,}\s]+)") {
    throw "Supply-chain provenance appears to contain a literal credential."
  }
  foreach ($credential in $credentials) {
    if ([string]::IsNullOrWhiteSpace([string]$credential.purpose) -or
        [string]::IsNullOrWhiteSpace([string]$credential.allowedStorage) -or
        [string]::IsNullOrWhiteSpace([string]$credential.forbidden)) {
      throw "Credential-boundary provenance is incomplete: $($credential.id)"
    }
  }

  $outputAbsolute = Resolve-OwnedPath $OutputPath
  $outputDirectory = Split-Path -Parent $outputAbsolute
  New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
  [pscustomobject]@{
    schemaVersion = 1
    status = "passed"
    nugetProjectLocks = $locks.Count
    nugetResolvedPackages = $nugetPackages.Count
    pythonLockedRequirements = $lockCount
    pythonPackageLicenses = $pythonPackages.Count
    models = $models.Count
    runtimes = $runtimes.Count
    assets = $assets.Count
    bundledBinaries = $bundledBinaries.Count
    ciActions = $actions.Count
    credentials = $credentials.Count
  } | ConvertTo-Json | Set-Content -LiteralPath $outputAbsolute -Encoding UTF8
  return $outputAbsolute
}

function Invoke-NegativeSelfTests {
  $failures = 0
  foreach ($case in @(
    { Assert-Sha256 ("0" * 64) "zero-hash" },
    { Assert-ImmutableRevision "main" "branch-revision" },
    { Assert-RemoteUri "https://user:secret@github.com/org/repo/latest/file" "credential-uri" },
    { Resolve-OwnedPath "..\outside.txt" },
    { Assert-UniqueIds @([pscustomobject]@{id="Tool"}, [pscustomobject]@{id="tool"}) "case-collision" },
    { Assert-PythonInventoryMatchesLocks @("scripts/local-models/requirements-indic-parler-torch-cpu-lock.txt") @([pscustomobject]@{name="torch";version="0.0.1";source="https://pypi.org/project/torch/0.0.1/"}) }
  )) {
    try { & $case; $failures++ } catch { }
  }
  if ($failures -ne 0) { throw "$failures supply-chain negative fixture(s) did not fail closed." }
}

Push-Location $repoRoot
try {
  if ($SelfTest) { Invoke-NegativeSelfTests }
  $report = Invoke-SupplyChainValidation
  Write-Host "Supply-chain validation passed: $report" -ForegroundColor Green
}
finally {
  Pop-Location
}
