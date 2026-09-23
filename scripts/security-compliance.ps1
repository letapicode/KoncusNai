[CmdletBinding()]
param(
  [string]$OutputPath = "artifacts/supply-chain/compliance.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
  param([Parameter(Mandatory = $true)][string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-NoNetworkApisInDictationModules {
  Write-Step "Checking dictation-session modules for network API usage"

  $modulePaths = @(
    "src/DictateAnywhere.Core",
    "src/DictateAnywhere.Hotkeys",
    "src/DictateAnywhere.Audio",
    "src/DictateAnywhere.Inference/Transcription/Cohere",
    "src/DictateAnywhere.Inference/Transcription/CrisperWhisperTranscriptionService.cs",
    "src/DictateAnywhere.Inference/Transcription/CrisperWhisperTranscriptionOptions.cs",
    "src/DictateAnywhere.Inference/Workers/PersistentPythonWorkerClient.cs",
    "src/DictateAnywhere.Inference/Workers/PersistentPythonWorkerClientFactory.cs",
    "src/DictateAnywhere.Inference/Transcription/TranscriptionService.cs",
    "src/DictateAnywhere.Inference/Transcription/TranscriptionModelRegistry.cs",
    "src/DictateAnywhere.Insertion",
    "src/DictateAnywhere.Overlay",
    "src/DictateAnywhere.Settings",
    "src/DictateAnywhere.Diagnostics",
    "src/DictateAnywhere.Benchmark",
    "src/DictateAnywhere.Platform.Windows",
    "src/DictateAnywhere.App/Runtime/DictationRuntime.cs"
  )

  $pattern = "System\.Net(\.|$)|HttpClient|WebRequest|TcpClient|UdpClient|Socket|HttpRequestMessage"
  $networkMatches = @()

  foreach ($path in $modulePaths) {
    if (-not (Test-Path -Path $path)) {
      continue
    }

    $csFiles = @(Get-ChildItem -Path $path -Recurse -File -Filter "*.cs" | Where-Object {
      $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\bin\\"
    })
    if ($csFiles.Count -eq 0) {
      continue
    }

    $pathMatches = @($csFiles | Select-String -Pattern $pattern)
    if ($pathMatches.Count -gt 0) {
      $networkMatches += $pathMatches
    }
  }

  if ($networkMatches.Count -gt 0) {
    $formatted = $networkMatches | ForEach-Object {
      $entryPath = if ($_.PSObject.Properties["Path"]) { $_.Path } elseif ($_.PSObject.Properties["Filename"]) { $_.Filename } else { "<unknown>" }
      $lineNumber = if ($_.PSObject.Properties["LineNumber"]) { $_.LineNumber } else { 0 }
      $lineText = if ($_.PSObject.Properties["Line"]) { [string]$_.Line } else { [string]$_ }
      "{0}:{1}: {2}" -f $entryPath, $lineNumber, $lineText.Trim()
    }

    throw "Network APIs are forbidden in dictation-session modules.`n$($formatted -join [Environment]::NewLine)"
  }
}

function Assert-DictationRuntimeDoesNotDependOnModelDownload {
  Write-Step "Checking runtime composition for model-download dependency boundaries"

  $restrictedPaths = @(
    "src/DictateAnywhere.App/Runtime/DictationRuntime.cs"
  )
  $pattern = "IModelDownloader|HttpModelDownloader|HttpClient|System\.Net\.Http"
  $downloadBoundaryMatches = @()

  foreach ($path in $restrictedPaths) {
    if (-not (Test-Path -Path $path)) {
      continue
    }

    $csFiles = @(Get-ChildItem -Path $path -Recurse -File -Filter "*.cs" | Where-Object {
      $_.FullName -notmatch "\\obj\\" -and $_.FullName -notmatch "\\bin\\"
    })
    if ($csFiles.Count -eq 0) {
      continue
    }

    $pathMatches = @($csFiles | Select-String -Pattern $pattern)
    if ($pathMatches.Count -gt 0) {
      $downloadBoundaryMatches += $pathMatches
    }
  }

  if ($downloadBoundaryMatches.Count -gt 0) {
    $formatted = $downloadBoundaryMatches | ForEach-Object {
      $entryPath = if ($_.PSObject.Properties["Path"]) { $_.Path } elseif ($_.PSObject.Properties["Filename"]) { $_.Filename } else { "<unknown>" }
      $lineNumber = if ($_.PSObject.Properties["LineNumber"]) { $_.LineNumber } else { 0 }
      $lineText = if ($_.PSObject.Properties["Line"]) { [string]$_.Line } else { [string]$_ }
      "{0}:{1}: {2}" -f $entryPath, $lineNumber, $lineText.Trim()
    }

    throw "Dictation runtime modules must not directly depend on model-download infrastructure.`n$($formatted -join [Environment]::NewLine)"
  }
}

function Get-PackageReferences {
  $centralPath = "Directory.Packages.props"
  if (-not (Test-Path -LiteralPath $centralPath -PathType Leaf)) {
    throw "Central package configuration not found: $centralPath"
  }

  [xml]$centralXml = Get-Content -LiteralPath $centralPath -Raw
  $centralVersions = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
  $packageVersions = @($centralXml.SelectNodes("//*[local-name()='PackageVersion']"))
  if ($packageVersions.Count -eq 0) {
    throw "Central package configuration contains no PackageVersion declarations."
  }

  foreach ($packageVersion in $packageVersions) {
    $include = [string]$packageVersion.GetAttribute("Include")
    $version = [string]$packageVersion.GetAttribute("Version")
    if ([string]::IsNullOrWhiteSpace($include) -or [string]::IsNullOrWhiteSpace($version)) {
      throw "Every central PackageVersion must have non-empty Include and Version attributes."
    }
    if ($centralVersions.ContainsKey($include)) {
      throw "Duplicate central PackageVersion declaration: $include"
    }
    $centralVersions.Add($include, $version)
  }

  $projectFiles = @(Get-ChildItem -Path "src", "tools", "tests" -Recurse -File -Filter "*.csproj" | Where-Object {
    $_.FullName -notmatch "[\\/]bin[\\/]" -and
    $_.FullName -notmatch "[\\/]obj[\\/]" -and
    $_.FullName -notmatch "[\\/]TestResults[\\/]" -and
    $_.Name -notlike "*_wpftmp.csproj"
  })
  $configurationFiles = $projectFiles + @(Get-Item -LiteralPath "tests/Directory.Build.props")
  $result = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)

  foreach ($configurationFile in $configurationFiles) {
    [xml]$xml = Get-Content -LiteralPath $configurationFile.FullName -Raw
    foreach ($packageReference in @($xml.SelectNodes("//*[local-name()='PackageReference']"))) {
      $include = [string]$packageReference.GetAttribute("Include")
      if ([string]::IsNullOrWhiteSpace($include)) {
        continue
      }
      if ($packageReference.HasAttribute("Version") -or $packageReference.HasAttribute("VersionOverride")) {
        throw "PackageReference '$include' in '$($configurationFile.FullName)' must use its central version."
      }
      if (-not $centralVersions.ContainsKey($include)) {
        throw "PackageReference '$include' in '$($configurationFile.FullName)' has no central PackageVersion."
      }

      $result[$include] = $centralVersions[$include]
    }
  }

  $unusedVersions = @($centralVersions.Keys | Where-Object { -not $result.ContainsKey($_) })
  if ($unusedVersions.Count -gt 0) {
    throw "Unused central PackageVersion declaration(s): $($unusedVersions -join ', ')"
  }

  return ,$result
}

function Assert-DependencyDocumentationCoverage {
  Write-Step "Checking dependency/license documentation coverage"

  $packages = Get-PackageReferences
  if ($packages.Keys.Count -eq 0) {
    throw "No package references were discovered."
  }

  $dependencyManifest = Get-Content -Path "docs/dependency-manifest.md" -Raw
  $licenseInventory = Get-Content -Path "docs/license-inventory.md" -Raw

  $missingFromDependencyManifest = @()
  $missingFromLicenseInventory = @()

  foreach ($packageName in $packages.Keys) {
    if ($dependencyManifest.IndexOf($packageName, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
      $missingFromDependencyManifest += $packageName
    }

    if ($licenseInventory.IndexOf($packageName, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
      $missingFromLicenseInventory += $packageName
    }
  }

  if ($missingFromDependencyManifest.Count -gt 0) {
    throw "Missing package(s) in docs/dependency-manifest.md: $($missingFromDependencyManifest -join ', ')"
  }

  if ($missingFromLicenseInventory.Count -gt 0) {
    throw "Missing package(s) in docs/license-inventory.md: $($missingFromLicenseInventory -join ', ')"
  }
}

function Assert-BundledBinaryManifestIntegrity {
  Write-Step "Checking bundled binary integrity manifest"

  $repoRoot = (Get-Location).Path
  $manifestPath = "installer/bundled-binary-manifest.json"
  if (-not (Test-Path -Path $manifestPath)) {
    throw "Bundled binary manifest not found: $manifestPath"
  }

  $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
  if ($null -eq $manifest.schemaVersion -or [int]$manifest.schemaVersion -lt 1) {
    throw "Bundled binary manifest schemaVersion must be >= 1."
  }

  $binaries = @($manifest.binaries)
  foreach ($binary in $binaries) {
    $relativePath = [string]$binary.relativePath
    $sha256 = [string]$binary.sha256
    $required = [bool]$binary.required

    if ([string]::IsNullOrWhiteSpace($relativePath)) {
      throw "Bundled binary manifest contains an entry with empty relativePath."
    }

    if ([string]::IsNullOrWhiteSpace($sha256) -or $sha256 -notmatch "^[a-fA-F0-9]{64}$") {
      throw "Bundled binary '$relativePath' has invalid SHA-256."
    }

    $absolutePath = [IO.Path]::GetFullPath((Join-Path -Path $repoRoot -ChildPath $relativePath))
    $rootPrefix = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $absolutePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Bundled binary path escapes repository root: $relativePath"
    }

    if (-not (Test-Path -Path $absolutePath)) {
      if ($required) {
        throw "Required bundled binary is missing: $relativePath"
      }

      continue
    }

    $actualHash = (Get-FileHash -Path $absolutePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not [string]::Equals($actualHash, $sha256.ToLowerInvariant(), [StringComparison]::Ordinal)) {
      throw "Bundled binary hash mismatch for '$relativePath'. Expected '$sha256', got '$actualHash'."
    }
  }
}

Write-Step "Running security/privacy/compliance checks"
Assert-NoNetworkApisInDictationModules
Assert-DictationRuntimeDoesNotDependOnModelDownload
Assert-DependencyDocumentationCoverage
Assert-BundledBinaryManifestIntegrity
Write-Step "Validating supply-chain provenance and integrity"
& (Join-Path $PSScriptRoot "validate-supply-chain.ps1") -OutputPath $OutputPath -SelfTest

Write-Host ""
Write-Host "Security compliance checks passed." -ForegroundColor Green
