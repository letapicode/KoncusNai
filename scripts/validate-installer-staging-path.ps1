[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$OutputRoot,
  [Parameter(Mandatory = $true)][string]$PublishDirectory,
  [Parameter(Mandatory = $true)][string]$RepoRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Resolve-RequiredDirectoryPath {
  param(
    [Parameter(Mandatory = $true)][string]$PathValue,
    [Parameter(Mandatory = $true)][string]$Label
  )

  if ([string]::IsNullOrWhiteSpace($PathValue)) {
    throw "$Label must not be empty."
  }

  return [IO.Path]::GetFullPath($PathValue)
}

function Test-SameDirectoryPath {
  param(
    [Parameter(Mandatory = $true)][string]$Left,
    [Parameter(Mandatory = $true)][string]$Right
  )

  return [string]::Equals(
    $Left.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
    $Right.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar),
    [StringComparison]::OrdinalIgnoreCase)
}

$resolvedOutputRoot = Resolve-RequiredDirectoryPath -PathValue $OutputRoot -Label "OutputRoot"
$resolvedPublishDirectory = Resolve-RequiredDirectoryPath -PathValue $PublishDirectory -Label "PublishDirectory"
$resolvedRepoRoot = Resolve-RequiredDirectoryPath -PathValue $RepoRoot -Label "RepoRoot"
$profileRoot = Resolve-RequiredDirectoryPath `
  -PathValue ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) `
  -Label "User profile"
$volumeRoot = [IO.Path]::GetPathRoot($resolvedOutputRoot)
$expectedPublishDirectory = [IO.Path]::GetFullPath(
  (Join-Path -Path $resolvedOutputRoot -ChildPath "publish/DictateAnywhere.App"))

foreach ($broadOutputRoot in @($resolvedRepoRoot, $profileRoot, $volumeRoot)) {
  if (Test-SameDirectoryPath -Left $resolvedOutputRoot -Right $broadOutputRoot) {
    throw "OutputRoot must not resolve to a repository, user-profile, or drive root: $resolvedOutputRoot"
  }
}

if (-not (Test-SameDirectoryPath -Left $resolvedPublishDirectory -Right $expectedPublishDirectory)) {
  throw "Publish cleanup target must be exactly '<OutputRoot>/publish/DictateAnywhere.App': $resolvedPublishDirectory"
}

$currentPath = [IO.DirectoryInfo]::new($resolvedPublishDirectory)
while ($null -ne $currentPath) {
  if ($currentPath.Exists -and
      ($currentPath.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Publish cleanup path must not contain a reparse point: $($currentPath.FullName)"
  }

  $currentPath = $currentPath.Parent
}

Write-Host "Installer staging cleanup path is valid." -ForegroundColor Green
