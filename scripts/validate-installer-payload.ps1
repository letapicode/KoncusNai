[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$PayloadDirectory,
  [string]$AppExecutable = "DictateAnywhere.App.exe",
  [string]$UiAccessHelperExecutable = "DictateAnywhere.UiAccessHelper.exe"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if ([string]::IsNullOrWhiteSpace($PayloadDirectory)) {
  throw "Installer payload directory must not be empty."
}

$resolvedPayloadDirectory = [IO.Path]::GetFullPath($PayloadDirectory)
if (-not (Test-Path -LiteralPath $resolvedPayloadDirectory -PathType Container)) {
  throw "Installer payload directory does not exist: $resolvedPayloadDirectory"
}

$payloadRootItem = Get-Item -LiteralPath $resolvedPayloadDirectory -Force
if (($payloadRootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
  throw "Installer payload directory must not be a reparse point: $resolvedPayloadDirectory"
}

$payloadBoundary = $resolvedPayloadDirectory.TrimEnd(
  [IO.Path]::DirectorySeparatorChar,
  [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

function Get-PayloadRelativePath {
  param([Parameter(Mandatory = $true)][string]$FilePath)

  $resolvedFilePath = [IO.Path]::GetFullPath($FilePath)
  if (-not $resolvedFilePath.StartsWith($payloadBoundary, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Payload entry resolved outside the installer boundary: $resolvedFilePath"
  }

  return $resolvedFilePath.Substring($payloadBoundary.Length).Replace('\', '/')
}

$reparsePoint = Get-ChildItem -LiteralPath $resolvedPayloadDirectory -Recurse -Force |
  Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 } |
  Select-Object -First 1
if ($null -ne $reparsePoint) {
  throw "Installer payload must not contain reparse points: $($reparsePoint.FullName)"
}

$payloadFiles = @(Get-ChildItem -LiteralPath $resolvedPayloadDirectory -Recurse -File -Force)
if ([string]::Equals($AppExecutable, $UiAccessHelperExecutable, [StringComparison]::OrdinalIgnoreCase)) {
  throw "App and UIAccess helper executable names must be distinct."
}

foreach ($requiredExecutable in @($AppExecutable, $UiAccessHelperExecutable)) {
  if ([string]::IsNullOrWhiteSpace($requiredExecutable) -or
      [IO.Path]::GetFileName($requiredExecutable) -ne $requiredExecutable -or
      [IO.Path]::GetExtension($requiredExecutable) -ine ".exe") {
    throw "Required payload executable must be a filename without a directory: '$requiredExecutable'."
  }

  $matches = @($payloadFiles | Where-Object { $_.Name -ieq $requiredExecutable })
  if ($matches.Count -ne 1) {
    throw "Installer payload must contain exactly one '$requiredExecutable'; found $($matches.Count)."
  }

  if (-not [string]::Equals($matches[0].DirectoryName, $resolvedPayloadDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Required executable '$requiredExecutable' must be at the installer payload root."
  }
}

$developerExecutablePrefixes = @(
  "DictateAnywhere.ModelBenchmark",
  "DictateAnywhere.TtsCli",
  "DictateAnywhere.Spikes",
  "DictateAnywhere.VoicePreviewGenerator"
)
foreach ($payloadFile in $payloadFiles) {
  $relativePath = Get-PayloadRelativePath -FilePath $payloadFile.FullName
  if ($relativePath -match '(^|/)(__pycache__|\.git|\.venv)(/|$)' -or
      $payloadFile.Extension -in @('.pyc', '.pyo', '.pfx', '.p12', '.pem', '.key', '.log') -or
      $payloadFile.Name -match '^\.env($|\.)|\.(secrets|local)\.json$' -or
      $relativePath -match '^local-models/test_.*\.py$') {
    throw "Private, generated or test content is forbidden in the installer payload: $relativePath"
  }
  if ($payloadFile.Extension -ieq ".exe" -and
      $payloadFile.Name.StartsWith("DictateAnywhere.", [StringComparison]::OrdinalIgnoreCase) -and
      $payloadFile.Name -ine $AppExecutable -and
      $payloadFile.Name -ine $UiAccessHelperExecutable) {
    throw "Unexpected DictateAnywhere executable is forbidden in the installer payload: $($payloadFile.Name)"
  }

  foreach ($developerExecutablePrefix in $developerExecutablePrefixes) {
    if ($payloadFile.Name.StartsWith($developerExecutablePrefix, [StringComparison]::OrdinalIgnoreCase)) {
      throw "Developer tool output is forbidden in the installer payload: $($payloadFile.Name)"
    }
  }
}

if ($payloadFiles | Where-Object { (Get-PayloadRelativePath -FilePath $_.FullName) -match '^local-models/fixtures/.*\.(wav|mp3|ogg|flac)$' }) {
  throw 'Generated or unlicensed audio fixtures must not be included in the installer payload.'
}
Write-Host "Installer payload boundary is valid: App and UIAccess only." -ForegroundColor Green
