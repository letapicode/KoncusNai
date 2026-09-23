[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step {
  param([Parameter(Mandatory = $true)][string]$Message)
  Write-Host ""
  Write-Host "==> $Message" -ForegroundColor Cyan
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

function Assert-MsiVersion {
  param([Parameter(Mandatory = $true)][string]$ProductVersion)

  Assert-Condition -Condition ($ProductVersion -match "^\d+\.\d+\.\d+$") -Message "Version '$ProductVersion' must be in Major.Minor.Build format."
  foreach ($segment in $ProductVersion.Split(".")) {
    $numericSegment = [int]$segment
    Assert-Condition -Condition ($numericSegment -ge 0 -and $numericSegment -le 255) -Message "Version segment '$numericSegment' must be between 0 and 255."
  }
}

function Resolve-RepoRoot {
  $scriptsRoot = $script:PSScriptRoot
  if ([string]::IsNullOrWhiteSpace($scriptsRoot)) {
    throw "Failed to resolve scripts directory."
  }

  return [IO.Path]::GetFullPath((Join-Path -Path $scriptsRoot -ChildPath ".."))
}

$repoRoot = Resolve-RepoRoot
Set-Location $repoRoot

Write-Step "Loading installer compatibility artifacts"
$requiredPaths = @(
  "installer/wix/installer-config.json",
  "installer/wix/upgrade-policy.json",
  "installer/wix/DictateAnywhere.wxs"
)

foreach ($path in $requiredPaths) {
  Assert-Condition -Condition (Test-Path -Path $path) -Message "Missing required file: $path"
}

$config = Get-Content -Path "installer/wix/installer-config.json" -Raw | ConvertFrom-Json
$policy = Get-Content -Path "installer/wix/upgrade-policy.json" -Raw | ConvertFrom-Json
[xml]$wixXml = Get-Content -Path "installer/wix/DictateAnywhere.wxs" -Raw
$namespaceManager = New-Object System.Xml.XmlNamespaceManager($wixXml.NameTable)
[void]$namespaceManager.AddNamespace("w", "http://wixtoolset.org/schemas/v4/wxs")

Write-Step "Checking static upgrade policy"
Assert-Condition -Condition ([int]$policy.schemaVersion -ge 1) -Message "upgrade-policy.json schemaVersion must be >= 1."
Assert-Condition -Condition ([string]$policy.upgradeCode -match "^\{[0-9A-Fa-f-]{36}\}$") -Message "upgrade-policy.json must contain a GUID-formatted upgradeCode."
Assert-MsiVersion -ProductVersion ([string]$policy.minimumSupportedVersion)

Write-Step "Checking installer config against upgrade policy"
Assert-MsiVersion -ProductVersion ([string]$config.defaultVersion)
Assert-Condition -Condition ([string]::Equals([string]$config.upgradeCode, [string]$policy.upgradeCode, [StringComparison]::OrdinalIgnoreCase)) -Message "installer-config.json upgradeCode must remain identical to upgrade-policy.json upgradeCode."
Assert-Condition -Condition (-not [bool]$policy.allowDowngrades) -Message "MVP policy must disallow downgrades."

$currentVersion = [version]([string]$config.defaultVersion)
$minimumVersion = [version]([string]$policy.minimumSupportedVersion)
Assert-Condition -Condition ($currentVersion -ge $minimumVersion) -Message "installer-config.json defaultVersion must be >= policy minimumSupportedVersion."

Write-Step "Checking WiX upgrade authoring"
$packageNode = $wixXml.SelectSingleNode("/w:Wix/w:Package", $namespaceManager)
Assert-Condition -Condition ($null -ne $packageNode) -Message "WiX package root was not found."
Assert-Condition -Condition ($packageNode.GetAttribute("UpgradeCode") -eq '$(var.UpgradeCode)') -Message "WiX Package UpgradeCode must come from variable binding."
Assert-Condition -Condition ($packageNode.GetAttribute("Version") -eq '$(var.ProductVersion)') -Message "WiX Package Version must come from variable binding."

$majorUpgradeNode = $wixXml.SelectSingleNode("/w:Wix/w:Package/w:MajorUpgrade", $namespaceManager)
Assert-Condition -Condition ($null -ne $majorUpgradeNode) -Message "MajorUpgrade element is required for upgrade compatibility."

$allowDowngradesValue = $majorUpgradeNode.GetAttribute("AllowDowngrades")
Assert-Condition -Condition ([string]::IsNullOrWhiteSpace($allowDowngradesValue) -or $allowDowngradesValue -eq "no") -Message "MajorUpgrade must not allow downgrades."

Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace($majorUpgradeNode.GetAttribute("DowngradeErrorMessage"))) -Message "MajorUpgrade must define DowngradeErrorMessage."

Write-Step "Installer upgrade compatibility checks passed"
Write-Host "Upgrade code stability and versioning policy are valid." -ForegroundColor Green
