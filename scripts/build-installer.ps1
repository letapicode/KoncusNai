[CmdletBinding()]
param(
  [string]$Configuration = "Release",
  [string]$Version,
  [string]$OutputRoot = "artifacts",
  [ValidateSet("msi", "small")]
  [string]$DistributionMode = "msi",
  [switch]$SkipPublish,
  [switch]$EnableUiAccess,
  [switch]$SignInstallerArtifacts,
  [string]$SigningCertificateThumbprint,
  [string]$SigningTimestampUrl = "http://timestamp.digicert.com",
  [string]$SignToolPath = "signtool",
  [switch]$VerifyArtifactSignatures
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
  param([Parameter(Mandatory = $true)][string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-CheckedCommand {
  param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $false)][string[]]$Arguments = @()
  )

  & $Executable @Arguments
  if ($LASTEXITCODE -ne 0) {
    throw "Command failed ($LASTEXITCODE): $Executable $($Arguments -join ' ')"
  }
}

function Assert-CommandExists {
  param([Parameter(Mandatory = $true)][string]$CommandName)

  if ($null -eq (Get-Command -Name $CommandName -ErrorAction SilentlyContinue)) {
    throw "Required command '$CommandName' was not found. Install prerequisites and retry."
  }
}

function Assert-Condition {
  param(
    [Parameter(Mandatory = $true)][bool]$Condition,
    [Parameter(Mandatory = $true)][string]$Message
  )

  if (-not $Condition) {
    throw $Message
  }
}

function Assert-SignedBinary {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Label
  )

  if (-not (Test-Path -Path $Path)) {
    throw "$Label binary was not found at '$Path'."
  }

  $signature = Get-AuthenticodeSignature -FilePath $Path
  if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    throw "$Label binary must be Authenticode-signed when UIAccess is enabled. Status: $($signature.Status) ($Path)"
  }
}

function Assert-GuidFormat {
  param(
    [Parameter(Mandatory = $true)][string]$Value,
    [Parameter(Mandatory = $true)][string]$Label
  )

  if ([string]::IsNullOrWhiteSpace($Value) -or $Value -notmatch "^\{[0-9A-Fa-f-]{36}\}$") {
    throw "$Label must be a GUID wrapped in braces. Received '$Value'."
  }
}

function Assert-MsiVersion {
  param([Parameter(Mandatory = $true)][string]$ProductVersion)

  if ($ProductVersion -notmatch "^\d+\.\d+\.\d+$") {
    throw "MSI version must be in format Major.Minor.Build (for example: 1.0.0)."
  }

  $parts = $ProductVersion.Split(".")
  foreach ($part in $parts) {
    $numericPart = [int]$part
    if ($numericPart -lt 0 -or $numericPart -gt 255) {
      throw "MSI version segment '$numericPart' must be between 0 and 255."
    }
  }
}

function Resolve-RepoRoot {
  $scriptsRoot = $script:PSScriptRoot
  if ([string]::IsNullOrWhiteSpace($scriptsRoot)) {
    throw "Failed to resolve scripts directory."
  }

  return [IO.Path]::GetFullPath((Join-Path -Path $scriptsRoot -ChildPath ".."))
}

function Resolve-OutputRoot {
  param(
    [Parameter(Mandatory = $true)][string]$PathValue,
    [Parameter(Mandatory = $true)][string]$RepoRoot
  )

  if ([string]::IsNullOrWhiteSpace($PathValue)) {
    throw "OutputRoot must not be empty."
  }

  if ([IO.Path]::IsPathRooted($PathValue)) {
    return [IO.Path]::GetFullPath($PathValue)
  }

  return [IO.Path]::GetFullPath((Join-Path -Path $RepoRoot -ChildPath $PathValue))
}

function Resolve-FileNameFromPattern {
  param(
    [Parameter(Mandatory = $true)][string]$Pattern,
    [Parameter(Mandatory = $true)][string]$Version
  )

  if ([string]::IsNullOrWhiteSpace($Pattern)) {
    throw "Artifact filename pattern must not be empty."
  }

  return $Pattern.Replace("{version}", $Version)
}

function Get-Sha256Lower {
  param([Parameter(Mandatory = $true)][string]$Path)

  return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-AuthenticodeStatus {
  param([Parameter(Mandatory = $true)][string]$Path)

  if (-not $VerifyArtifactSignatures) {
    return "NotChecked"
  }

  try {
    $signature = Get-AuthenticodeSignature -FilePath $Path
    return [string]$signature.Status
  }
  catch {
    return "Unknown"
  }
}

function Get-AuthenticodeMetadata {
  param([Parameter(Mandatory = $true)][string]$Path)

  try {
    $signature = Get-AuthenticodeSignature -FilePath $Path
    return [PSCustomObject]@{
      status = [string]$signature.Status
      statusMessage = [string]$signature.StatusMessage
      hasTimestamp = $null -ne $signature.TimeStamperCertificate
    }
  }
  catch {
    return [PSCustomObject]@{
      status = "Unknown"
      statusMessage = $_.Exception.Message
      hasTimestamp = $false
    }
  }
}

function Assert-AuthenticodeGate {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Label
  )

  if (-not (Test-Path -Path $Path)) {
    throw "$Label artifact not found at '$Path'."
  }

  $metadata = Get-AuthenticodeMetadata -Path $Path
  if ($metadata.status -ne [string][System.Management.Automation.SignatureStatus]::Valid) {
    throw "$Label signature validation failed. Status: $($metadata.status). $($metadata.statusMessage)"
  }

  if (-not [bool]$metadata.hasTimestamp) {
    throw "$Label is missing an Authenticode timestamp."
  }
}

function Normalize-Thumbprint {
  param([Parameter(Mandatory = $true)][string]$Thumbprint)

  return ($Thumbprint -replace "\s", "").ToUpperInvariant()
}

function Resolve-SigningCertificate {
  param([Parameter(Mandatory = $true)][string]$Thumbprint)

  $normalizedThumbprint = Normalize-Thumbprint -Thumbprint $Thumbprint
  if ([string]::IsNullOrWhiteSpace($normalizedThumbprint)) {
    throw "Signing certificate thumbprint must not be empty."
  }

  $certificate = Get-ChildItem -Path "Cert:\CurrentUser\My" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { [string]::Equals($_.Thumbprint, $normalizedThumbprint, [StringComparison]::OrdinalIgnoreCase) } |
    Select-Object -First 1

  if ($null -eq $certificate) {
    throw "Signing certificate with thumbprint '$normalizedThumbprint' was not found in Cert:\\CurrentUser\\My."
  }

  return $normalizedThumbprint
}

function Invoke-SignArtifact {
  param(
    [Parameter(Mandatory = $true)][string]$SignToolExecutable,
    [Parameter(Mandatory = $true)][string]$Thumbprint,
    [Parameter(Mandatory = $true)][string]$TimestampUrl,
    [Parameter(Mandatory = $true)][string]$Path
  )

  if (-not (Test-Path -Path $Path)) {
    throw "Cannot sign missing artifact: $Path"
  }

  Invoke-CheckedCommand -Executable $SignToolExecutable -Arguments @(
    "sign",
    "/fd", "SHA256",
    "/td", "SHA256",
    "/tr", $TimestampUrl,
    "/sha1", $Thumbprint,
    $Path
  )
}

function Ensure-WixExtensionInstalled {
  param([Parameter(Mandatory = $true)][string]$ExtensionId)

  $listOutput = & wix extension list --global
  if ($LASTEXITCODE -ne 0) {
    throw "Failed to query installed WiX extensions."
  }

  $isInstalled = $false
  foreach ($line in $listOutput) {
    if ($null -ne $line -and [string]$line -match [Regex]::Escape($ExtensionId)) {
      $isInstalled = $true
      break
    }
  }

  if ($isInstalled) {
    return
  }

  Write-Step "Installing WiX extension '$ExtensionId'"
  Invoke-CheckedCommand -Executable "wix" -Arguments @("extension", "add", "--global", $ExtensionId)
}

$repoRoot = Resolve-RepoRoot
Set-Location $repoRoot

$configPath = Join-Path -Path $repoRoot -ChildPath "installer/wix/installer-config.json"
$distributionConfigPath = Join-Path -Path $repoRoot -ChildPath "installer/bundle/distribution-config.json"

if (-not (Test-Path -Path $configPath)) {
  throw "Installer config not found at '$configPath'."
}

$requiredPaths = @(
  $distributionConfigPath,
  (Join-Path -Path $repoRoot -ChildPath "installer/bundle/DictateAnywhere.Small.Bundle.wxs")
)

foreach ($requiredPath in $requiredPaths) {
  if (-not (Test-Path -Path $requiredPath)) {
    throw "Required installer file is missing: $requiredPath"
  }
}

$config = Get-Content -Path $configPath -Raw | ConvertFrom-Json
$distributionConfig = Get-Content -Path $distributionConfigPath -Raw | ConvertFrom-Json
$productVersion = if ([string]::IsNullOrWhiteSpace($Version)) { [string]$config.defaultVersion } else { $Version }
Assert-MsiVersion -ProductVersion $productVersion

$productName = [string]$config.productName
$manufacturer = [string]$config.manufacturer
$upgradeCode = [string]$config.upgradeCode
$architecture = [string]$config.architecture
$runtimeIdentifier = [string]$config.runtimeIdentifier
$selfContained = [bool]$config.selfContained
$uiAccessHelperExecutable = [string]$config.uiAccessHelperExecutable
$requireSignedBinariesForUiAccess = [bool]$config.requireSignedBinariesForUiAccess

$msiPattern = [string]$distributionConfig.artifacts.msiPattern
$smallExePattern = [string]$distributionConfig.artifacts.smallSetupExePattern
$artifactManifestPattern = [string]$distributionConfig.artifacts.artifactManifestPattern
$bundleUpgradeCode = [string]$distributionConfig.bundle.upgradeCode

Assert-GuidFormat -Value $bundleUpgradeCode -Label "bundle.upgradeCode"

if ([string]::IsNullOrWhiteSpace($productName) -or [string]::IsNullOrWhiteSpace($manufacturer)) {
  throw "Installer config must define non-empty productName and manufacturer."
}

if ([string]::IsNullOrWhiteSpace($upgradeCode) -or $upgradeCode -notmatch "^\{[0-9A-Fa-f-]{36}\}$") {
  throw "Installer config must define a valid GUID-formatted upgradeCode."
}

if ([string]::IsNullOrWhiteSpace($runtimeIdentifier)) {
  throw "Installer config must define runtimeIdentifier."
}

if ([string]::IsNullOrWhiteSpace($uiAccessHelperExecutable)) {
  throw "Installer config must define uiAccessHelperExecutable."
}

if ($architecture -ne "x64") {
  throw "Unsupported architecture '$architecture'. MVP installer is locked to x64."
}

$shouldBuildSmallBundle = $DistributionMode -eq "small"

$appProjectPath = Join-Path -Path $repoRoot -ChildPath "src/DictateAnywhere.App/DictateAnywhere.App.csproj"
$uiAccessHelperProjectPath = Join-Path -Path $repoRoot -ChildPath "src/DictateAnywhere.UiAccessHelper/DictateAnywhere.UiAccessHelper.csproj"
$wixPath = Join-Path -Path $repoRoot -ChildPath "installer/wix/DictateAnywhere.wxs"
$smallBundleWixPath = Join-Path -Path $repoRoot -ChildPath "installer/bundle/DictateAnywhere.Small.Bundle.wxs"
$resolvedOutputRoot = Resolve-OutputRoot -PathValue $OutputRoot -RepoRoot $repoRoot
$publishDirectory = [IO.Path]::GetFullPath((Join-Path -Path $resolvedOutputRoot -ChildPath "publish/DictateAnywhere.App"))
$installerOutputDirectory = [IO.Path]::GetFullPath((Join-Path -Path $resolvedOutputRoot -ChildPath "installer"))
$stagingPathValidatorPath = Join-Path -Path $repoRoot -ChildPath "scripts/validate-installer-staging-path.ps1"
$payloadValidatorPath = Join-Path -Path $repoRoot -ChildPath "scripts/validate-installer-payload.ps1"

$msiPath = Join-Path -Path $installerOutputDirectory -ChildPath (Resolve-FileNameFromPattern -Pattern $msiPattern -Version $productVersion)
$smallSetupExePath = Join-Path -Path $installerOutputDirectory -ChildPath (Resolve-FileNameFromPattern -Pattern $smallExePattern -Version $productVersion)
$artifactManifestPath = Join-Path -Path $installerOutputDirectory -ChildPath (Resolve-FileNameFromPattern -Pattern $artifactManifestPattern -Version $productVersion)

if (-not (Test-Path -Path $appProjectPath)) {
  throw "App project file not found: $appProjectPath"
}

if (-not (Test-Path -Path $uiAccessHelperProjectPath)) {
  throw "UIAccess helper project file not found: $uiAccessHelperProjectPath"
}

if (-not (Test-Path -Path $wixPath)) {
  throw "WiX source file not found: $wixPath"
}

Assert-CommandExists -CommandName "dotnet"
Assert-CommandExists -CommandName "wix"

if ($shouldBuildSmallBundle) {
  Ensure-WixExtensionInstalled -ExtensionId "WixToolset.BootstrapperApplications.wixext"
}

$enforceSignatureGate = $VerifyArtifactSignatures -or $SignInstallerArtifacts
$signingThumbprint = $null
if ($SignInstallerArtifacts) {
  Assert-CommandExists -CommandName $SignToolPath
  if ([string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
    throw "SigningCertificateThumbprint is required when -SignInstallerArtifacts is used."
  }
  if ([string]::IsNullOrWhiteSpace($SigningTimestampUrl)) {
    throw "SigningTimestampUrl is required when -SignInstallerArtifacts is used."
  }

  $signingThumbprint = Resolve-SigningCertificate -Thumbprint $SigningCertificateThumbprint
}

New-Item -ItemType Directory -Path $installerOutputDirectory -Force | Out-Null

if (-not $SkipPublish) {
  & $stagingPathValidatorPath `
    -OutputRoot $resolvedOutputRoot `
    -PublishDirectory $publishDirectory `
    -RepoRoot $repoRoot
  if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
  }
  New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

  Write-Step "Publishing DictateAnywhere.App ($runtimeIdentifier, self-contained=$selfContained)"
  Invoke-CheckedCommand -Executable "dotnet" -Arguments @(
    "publish",
    $appProjectPath,
    "-c", $Configuration,
    "-r", $runtimeIdentifier,
    "--self-contained", $selfContained.ToString().ToLowerInvariant(),
    "-p:NuGetLockFilePath=packages.win-x64.lock.json",
    "-p:RestoreLockedMode=true",
    "-o", $publishDirectory
  )

  Write-Step "Publishing DictateAnywhere.UiAccessHelper ($runtimeIdentifier, self-contained=false)"
  Invoke-CheckedCommand -Executable "dotnet" -Arguments @(
    "publish",
    $uiAccessHelperProjectPath,
    "-c", $Configuration,
    "-r", $runtimeIdentifier,
    "--self-contained", "false",
    "-p:UseAppHost=true",
    "-p:NuGetLockFilePath=packages.win-x64.lock.json",
    "-p:RestoreLockedMode=true",
    "-o", $publishDirectory
  )
}

if (-not (Test-Path -LiteralPath $publishDirectory -PathType Container)) {
  throw "Installer payload directory not found at '$publishDirectory'. Publish first or provide a valid existing payload with -SkipPublish."
}

Write-Step "Validating production executable payload boundary"
& $payloadValidatorPath `
  -PayloadDirectory $publishDirectory `
  -AppExecutable "DictateAnywhere.App.exe" `
  -UiAccessHelperExecutable $uiAccessHelperExecutable

$publishedExe = Join-Path -Path $publishDirectory -ChildPath "DictateAnywhere.App.exe"
if (-not (Test-Path -Path $publishedExe)) {
  throw "Published executable not found at '$publishedExe'. Publish failed or output path is incorrect."
}

$helperExePath = Join-Path -Path $publishDirectory -ChildPath $uiAccessHelperExecutable
if (-not (Test-Path -Path $helperExePath)) {
  throw "Published UIAccess helper executable not found at '$helperExePath'. Ensure helper publish completed successfully."
}

$uiAccessEnabledValue = if ($EnableUiAccess) { "1" } else { "0" }
if ($EnableUiAccess) {
  Write-Step "Validating UIAccess helper prerequisites"

  if ($requireSignedBinariesForUiAccess) {
    Assert-SignedBinary -Path $publishedExe -Label "Application"
    Assert-SignedBinary -Path $helperExePath -Label "UIAccess helper"
  }
}

Write-Step "Building MSI with WiX"
Invoke-CheckedCommand -Executable "wix" -Arguments @(
  "build",
  $wixPath,
  "-arch", $architecture,
  "-d", "PublishDir=$publishDirectory",
  "-d", "ProductName=$productName",
  "-d", "BrandIcon=$(Join-Path $repoRoot 'src\DictateAnywhere.App\Assets\Brand\KoncusNai.ico')",
  "-d", "Manufacturer=$manufacturer",
  "-d", "ProductVersion=$productVersion",
  "-d", "UpgradeCode=$upgradeCode",
  "-d", "EnableUiAccess=$uiAccessEnabledValue",
  "-d", "UiAccessHelperExecutable=$uiAccessHelperExecutable",
  "-o", $msiPath
)

if (-not (Test-Path -Path $msiPath)) {
  throw "MSI output was not produced at '$msiPath'."
}

$builtArtifacts = New-Object System.Collections.Generic.List[object]

$builtArtifacts.Add([PSCustomObject]@{
    path = $msiPath
    distributionScope = "shared"
  })

if ($SignInstallerArtifacts) {
  Write-Step "Signing app MSI"
  Invoke-SignArtifact -SignToolExecutable $SignToolPath -Thumbprint $signingThumbprint -TimestampUrl $SigningTimestampUrl -Path $msiPath
}

if ($shouldBuildSmallBundle) {
  Write-Step "Building small setup EXE bundle"
  Invoke-CheckedCommand -Executable "wix" -Arguments @(
    "build",
    $smallBundleWixPath,
    "-ext", "WixToolset.BootstrapperApplications.wixext",
    "-arch", $architecture,
    "-d", "BundleName=$productName Setup (Small)",
    "-d", "Manufacturer=$manufacturer",
    "-d", "ProductVersion=$productVersion",
    "-d", "BundleUpgradeCode=$bundleUpgradeCode",
    "-d", "MsiPath=$msiPath",
    "-d", "BrandIcon=$(Join-Path $repoRoot 'src\DictateAnywhere.App\Assets\Brand\KoncusNai.ico')",
    "-d", "BrandLogo=$(Join-Path $repoRoot 'src\DictateAnywhere.App\Assets\Brand\koncus-nai-64.png')",
    "-o", $smallSetupExePath
  )

  if (-not (Test-Path -Path $smallSetupExePath)) {
    throw "Small setup EXE was not produced at '$smallSetupExePath'."
  }

  $builtArtifacts.Add([PSCustomObject]@{
      path = $smallSetupExePath
      distributionScope = "small"
    })

  if ($SignInstallerArtifacts) {
    Write-Step "Signing small setup EXE"
    Invoke-SignArtifact -SignToolExecutable $SignToolPath -Thumbprint $signingThumbprint -TimestampUrl $SigningTimestampUrl -Path $smallSetupExePath
  }
}

if ($enforceSignatureGate) {
  Write-Step "Verifying artifact signature and timestamp gates"
  foreach ($artifact in $builtArtifacts) {
    Assert-AuthenticodeGate -Path ([string]$artifact.path) -Label ([IO.Path]::GetFileName([string]$artifact.path))
  }
}

$artifactManifestRows = New-Object System.Collections.Generic.List[object]
foreach ($artifact in $builtArtifacts) {
  $artifactPath = [string]$artifact.path
  $artifactManifestRows.Add([PSCustomObject]@{
      fileName = [IO.Path]::GetFileName($artifactPath)
      version = $productVersion
      distributionScope = [string]$artifact.distributionScope
      sha256 = Get-Sha256Lower -Path $artifactPath
      sizeBytes = (Get-Item -Path $artifactPath).Length
      signatureStatus = (Get-AuthenticodeMetadata -Path $artifactPath).status
      signatureTimestamped = (Get-AuthenticodeMetadata -Path $artifactPath).hasTimestamp
    })
}

$artifactManifest = [PSCustomObject]@{
  schemaVersion = 1
  generatedAtUtc = [DateTime]::UtcNow.ToString("o")
  productVersion = $productVersion
  distributionMode = $DistributionMode
  artifacts = $artifactManifestRows
}

$artifactManifest | ConvertTo-Json -Depth 8 | Set-Content -Path $artifactManifestPath -Encoding UTF8

Write-Step "Installer build completed"
Write-Host "MSI: $msiPath" -ForegroundColor Green
if ($shouldBuildSmallBundle) {
  Write-Host "Small setup EXE: $smallSetupExePath" -ForegroundColor Green
}
Write-Host "Artifact manifest: $artifactManifestPath" -ForegroundColor Green
