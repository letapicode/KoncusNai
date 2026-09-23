[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$OutputRoot,
  [string]$Version = "1.0.0",
  [string]$ExpectedWixVersion = "5.0.2"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Invoke-Checked {
  param([string]$Executable, [string[]]$Arguments)
  & $Executable @Arguments | Out-Host
  if ($LASTEXITCODE -ne 0) { throw "Command failed ($LASTEXITCODE): $Executable $($Arguments -join ' ')" }
}

function Assert-NoReparseAncestor {
  param([string]$Path, [string]$Boundary)
  $boundaryFull = [IO.Path]::GetFullPath($Boundary).TrimEnd([IO.Path]::DirectorySeparatorChar)
  $cursor = [IO.Path]::GetFullPath($Path)
  while ($cursor.StartsWith($boundaryFull, [StringComparison]::OrdinalIgnoreCase)) {
    if (Test-Path -LiteralPath $cursor) {
      $item = Get-Item -LiteralPath $cursor -Force
      if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Measurement path traverses a reparse point: $cursor" }
    }
    if ([string]::Equals($cursor, $boundaryFull, [StringComparison]::OrdinalIgnoreCase)) { break }
    $cursor = Split-Path -Parent $cursor
  }
}

function Get-DirectorySummary {
  param([string]$Path)
  if (-not (Test-Path -LiteralPath $Path -PathType Container)) { throw "Measured directory is missing: $Path" }
  $files = @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName)
  if ($files.Count -eq 0) { throw "Measured directory is empty: $Path" }
  foreach ($file in $files) {
    if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Measured output contains a reparse point: $($file.FullName)" }
    if (($file.Attributes -band [IO.FileAttributes]::SparseFile) -ne 0) { throw "Measured output contains a sparse file whose logical and allocated sizes differ: $($file.FullName)" }
    $linkType = $file.PSObject.Properties["LinkType"]
    if ($null -ne $linkType -and [string]$linkType.Value -eq "HardLink") { throw "Measured output contains a hard link: $($file.FullName)" }
  }
  return [PSCustomObject]@{ files=$files.Count; bytes=[long](($files | Measure-Object Length -Sum).Sum); items=$files }
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path -Path $PSScriptRoot -ChildPath ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path -Path $repoRoot -ChildPath "artifacts"))
$resolvedOutputRoot = if ([IO.Path]::IsPathRooted($OutputRoot)) { [IO.Path]::GetFullPath($OutputRoot) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputRoot)) }
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutputRoot.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "OutputRoot must be a unique child of the repository artifacts directory." }
if (Test-Path -LiteralPath $resolvedOutputRoot) { throw "OutputRoot already exists; size measurements never reuse or clean prior evidence: $resolvedOutputRoot" }
Assert-NoReparseAncestor -Path (Split-Path -Parent $resolvedOutputRoot) -Boundary $repoRoot

$wixCommand = Get-Command wix -ErrorAction SilentlyContinue
if ($null -eq $wixCommand) { throw "WiX CLI is required on PATH." }
$wixVersionText = (& wix --version | Select-Object -First 1).Trim()
if ($LASTEXITCODE -ne 0 -or -not $wixVersionText.StartsWith($ExpectedWixVersion, [StringComparison]::Ordinal)) {
  throw "WiX $ExpectedWixVersion is required for comparable evidence; found '$wixVersionText'."
}

New-Item -ItemType Directory -Path $resolvedOutputRoot | Out-Null
$appOutput = Join-Path $resolvedOutputRoot "standalone/DictateAnywhere.App"
$helperOutput = Join-Path $resolvedOutputRoot "standalone/DictateAnywhere.UiAccessHelper"
$installerOutput = Join-Path $resolvedOutputRoot "installer-build"
New-Item -ItemType Directory -Path $appOutput -Force | Out-Null
New-Item -ItemType Directory -Path $helperOutput -Force | Out-Null

$stopwatch = [Diagnostics.Stopwatch]::StartNew()
Invoke-Checked "dotnet" @("publish","src/DictateAnywhere.App/DictateAnywhere.App.csproj","-c","Release","-r","win-x64","--self-contained","true","-p:NuGetLockFilePath=packages.win-x64.lock.json","-p:RestoreLockedMode=true","-o",$appOutput,"--nologo")
Invoke-Checked "dotnet" @("publish","src/DictateAnywhere.UiAccessHelper/DictateAnywhere.UiAccessHelper.csproj","-c","Release","-r","win-x64","--self-contained","false","-p:UseAppHost=true","-p:NuGetLockFilePath=packages.win-x64.lock.json","-p:RestoreLockedMode=true","-o",$helperOutput,"--nologo")
Invoke-Checked "powershell" @("-NoProfile","-ExecutionPolicy","Bypass","-File","scripts/build-installer.ps1","-Version",$Version,"-DistributionMode","small","-OutputRoot",$installerOutput)
$stopwatch.Stop()

$payloadPath = Join-Path $installerOutput "publish/DictateAnywhere.App"
Invoke-Checked "powershell" @("-NoProfile","-ExecutionPolicy","Bypass","-File","scripts/validate-installer-payload.ps1","-PayloadDirectory",$payloadPath,"-AppExecutable","DictateAnywhere.App.exe","-UiAccessHelperExecutable","DictateAnywhere.UiAccessHelper.exe")

$app = Get-DirectorySummary $appOutput
$helper = Get-DirectorySummary $helperOutput
$payload = Get-DirectorySummary $payloadPath
$appMap = @{}
foreach ($file in $app.items) { $relative=$file.FullName.Substring($appOutput.Length).TrimStart('\','/').Replace('\','/'); $appMap[$relative]=[pscustomobject]@{length=$file.Length;hash=(Get-FileHash $file.FullName -Algorithm SHA256).Hash} }
$contributionFiles = [Collections.Generic.List[object]]::new()
foreach ($file in $payload.items) {
  $relative=$file.FullName.Substring($payloadPath.Length).TrimStart('\','/').Replace('\','/')
  if ($appMap.ContainsKey($relative)) {
    if ($appMap[$relative].length -ne $file.Length -or $appMap[$relative].hash -ne (Get-FileHash $file.FullName -Algorithm SHA256).Hash) { throw "Combined payload changed App-owned file '$relative'." }
  } else { $contributionFiles.Add($file) }
}

$installerDirectory = Join-Path $installerOutput "installer"
$msi = @(Get-ChildItem -LiteralPath $installerDirectory -File -Filter "*.msi")
$burn = @(Get-ChildItem -LiteralPath $installerDirectory -File -Filter "*.exe")
if ($msi.Count -ne 1 -or $burn.Count -ne 1) { throw "Expected exactly one MSI and one Burn executable." }
$developerPrefixes=@('DictateAnywhere.ModelBenchmark','DictateAnywhere.TtsCli','DictateAnywhere.Spikes','DictateAnywhere.VoicePreviewGenerator')
$residue=@($payload.items|Where-Object{$name=$_.Name;@($developerPrefixes|Where-Object{$name.StartsWith($_,[StringComparison]::OrdinalIgnoreCase)}).Count -gt 0})
if ($residue.Count -ne 0) { throw "Developer executable residue invalidates measurement." }

$report = [ordered]@{
  schemaVersion=1
  outputComplete=$true
  baselineCommit=(git rev-parse HEAD).Trim()
  measuredAtUtc=[DateTime]::UtcNow.ToString("O",[Globalization.CultureInfo]::InvariantCulture)
  configuration="Release"
  runtimeIdentifier="win-x64"
  version=$Version
  unsigned=$true
  toolchain=[ordered]@{ dotnetSdk=(dotnet --version).Trim(); wix=$wixVersionText }
  elapsedSeconds=[Math]::Round($stopwatch.Elapsed.TotalSeconds,3)
  appPublishFiles=$app.files
  appPublishBytes=$app.bytes
  uiAccessStandaloneFiles=$helper.files
  uiAccessStandaloneBytes=$helper.bytes
  uiAccessContributionFiles=$contributionFiles.Count
  uiAccessContributionBytes=[long](($contributionFiles|Measure-Object Length -Sum).Sum)
  payloadBytes=$payload.bytes
  msiBytes=[long]$msi[0].Length
  burnBytes=[long]$burn[0].Length
  projectedInstalledBytes=$payload.bytes
  payload=[ordered]@{files=$payload.files;membershipValidated=$true;developerExecutableResidue=$residue.Count;duplicatePhysicalPaths=0}
  limitations=@("Projected installed bytes are logical validated payload bytes; filesystem allocation, registry metadata, and signed-artifact overhead were not measured.")
}
$reportPath = Join-Path $resolvedOutputRoot "size-measurement.json"
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
Invoke-Checked "powershell" @("-NoProfile","-ExecutionPolicy","Bypass","-File","scripts/validate-size-budgets.ps1","-MeasurementReport",$reportPath)
Write-Host "Size measurement complete: $reportPath"
$report | ConvertTo-Json -Depth 8
