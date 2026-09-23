[CmdletBinding()]
param(
  [switch]$SelfTest,
  [switch]$NoBuild,
  [string]$GenerateOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$testProject = Join-Path $repoRoot "tests/DictateAnywhere.Core.Tests/DictateAnywhere.Core.Tests.csproj"
$filter = if ($SelfTest) { "FullyQualifiedName~PublicApiContractSelfTests" } else { "FullyQualifiedName~PublicApiBaselineTests" }

function Assert-NoReparseAncestor([string]$Path, [string]$Boundary) {
  $cursor = [IO.Path]::GetFullPath($Path)
  $boundaryPath = [IO.Path]::GetFullPath($Boundary).TrimEnd([IO.Path]::DirectorySeparatorChar)
  while ($cursor.StartsWith($boundaryPath, [StringComparison]::OrdinalIgnoreCase)) {
    if (Test-Path -LiteralPath $cursor) {
      $item = Get-Item -LiteralPath $cursor -Force
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "API output path traverses a reparse point: $cursor" }
    }
    if ([string]::Equals($cursor, $boundaryPath, [StringComparison]::OrdinalIgnoreCase)) { break }
    $cursor = Split-Path -Parent $cursor
  }
}

if (-not [string]::IsNullOrWhiteSpace($GenerateOutput)) {
  if ($SelfTest) { throw "GenerateOutput cannot be combined with SelfTest." }
  $resolved = if ([IO.Path]::IsPathRooted($GenerateOutput)) { [IO.Path]::GetFullPath($GenerateOutput) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $GenerateOutput)) }
  $artifacts = [IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts")).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
  if (-not $resolved.StartsWith($artifacts, [StringComparison]::OrdinalIgnoreCase)) { throw "GenerateOutput must be a new file under ignored artifacts." }
  if (Test-Path -LiteralPath $resolved) { throw "GenerateOutput already exists: $resolved" }
  Assert-NoReparseAncestor -Path (Split-Path -Parent $resolved) -Boundary ($artifacts.TrimEnd([IO.Path]::DirectorySeparatorChar))
  $env:NOTYPE_API_BASELINE_OUTPUT = $resolved
}

try {
  $arguments = @("test", $testProject, "--configuration", "Release", "--nologo", "--filter", $filter)
  if ($NoBuild) { $arguments += @("--no-build", "--no-restore") }
  $discovery = @(& dotnet @arguments --list-tests 2>&1)
  $discovery | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "Public API test discovery failed with exit code $LASTEXITCODE." }
  $expectedName = if ($SelfTest) { "PublicApiContractSelfTests" } else { "PublicApiBaselineTests" }
  if (@($discovery | Where-Object { [string]$_ -match [regex]::Escape($expectedName) }).Count -eq 0) { throw "Public API validation discovered zero '$expectedName' tests." }
  & dotnet @arguments --blame-hang-timeout 2m
  if ($LASTEXITCODE -ne 0) { throw "Public API validation failed with exit code $LASTEXITCODE." }
}
finally {
  Remove-Item Env:NOTYPE_API_BASELINE_OUTPUT -ErrorAction SilentlyContinue
}
