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

  Assert-Condition -Condition ($ProductVersion -match "^\d+\.\d+\.\d+$") -Message "defaultVersion must be in Major.Minor.Build format."
  foreach ($segment in $ProductVersion.Split(".")) {
    $numericSegment = [int]$segment
    Assert-Condition -Condition ($numericSegment -ge 0 -and $numericSegment -le 255) -Message "defaultVersion segment '$numericSegment' must be between 0 and 255."
  }
}

function Resolve-RepoRoot {
  $scriptsRoot = $script:PSScriptRoot
  if ([string]::IsNullOrWhiteSpace($scriptsRoot)) {
    throw "Failed to resolve scripts directory."
  }

  return [IO.Path]::GetFullPath((Join-Path -Path $scriptsRoot -ChildPath ".."))
}

function Get-SingleWixNode {
  param(
    [Parameter(Mandatory = $true)][xml]$WixXml,
    [Parameter(Mandatory = $true)][System.Xml.XmlNamespaceManager]$NamespaceManager,
    [Parameter(Mandatory = $true)][string]$XPath,
    [Parameter(Mandatory = $true)][string]$FailureMessage
  )

  $node = $WixXml.SelectSingleNode($XPath, $NamespaceManager)
  if ($null -eq $node) {
    throw $FailureMessage
  }

  return $node
}

$repoRoot = Resolve-RepoRoot
Set-Location $repoRoot

Write-Step "Checking required packaging files"
$requiredPaths = @(
  "installer/wix/DictateAnywhere.wxs",
  "installer/wix/installer-config.json",
  "installer/bundle/distribution-config.json",
  "installer/bundle/DictateAnywhere.Small.Bundle.wxs",
  "src/DictateAnywhere.App/DictateAnywhere.App.csproj",
  "src/DictateAnywhere.UiAccessHelper/DictateAnywhere.UiAccessHelper.csproj",
  "src/DictateAnywhere.UiAccessHelper/app.manifest",
  "installer/msix",
  "docs/release",
  "scripts/build-installer.ps1",
  "scripts/validate-installer-staging-path.ps1",
  "scripts/validate-installer-payload.ps1"
)

foreach ($path in $requiredPaths) {
  Assert-Condition -Condition (Test-Path -Path $path) -Message "Missing required packaging path/file: $path"
}

Write-Step "Validating installer config"
$configPath = "installer/wix/installer-config.json"
$config = Get-Content -Path $configPath -Raw | ConvertFrom-Json

Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$config.productName)) -Message "installer-config.json must define productName."
Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$config.manufacturer)) -Message "installer-config.json must define manufacturer."
Assert-Condition -Condition ([string]$config.upgradeCode -match "^\{[0-9A-Fa-f-]{36}\}$") -Message "installer-config.json must define a GUID-formatted upgradeCode."
Assert-Condition -Condition ([string]$config.architecture -eq "x64") -Message "MVP installer architecture must remain x64."
Assert-Condition -Condition ([string]$config.runtimeIdentifier -eq "win-x64") -Message "MVP installer runtimeIdentifier must remain win-x64."
Assert-Condition -Condition ([bool]$config.selfContained) -Message "MVP installer must publish self-contained runtime dependencies."
Assert-Condition -Condition ([bool]$config.preserveDataOnUninstall) -Message "MVP installer must preserve data directories on uninstall."
Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$config.uiAccessHelperExecutable)) -Message "installer-config.json must define uiAccessHelperExecutable."
Assert-Condition -Condition ([bool]$config.requireSignedBinariesForUiAccess) -Message "requireSignedBinariesForUiAccess must remain true."
Assert-MsiVersion -ProductVersion ([string]$config.defaultVersion)

$distributionConfigPath = "installer/bundle/distribution-config.json"
$distributionConfig = Get-Content -Path $distributionConfigPath -Raw | ConvertFrom-Json
Assert-Condition -Condition ([int]$distributionConfig.schemaVersion -ge 1) -Message "distribution-config.json schemaVersion must be >= 1."
Assert-Condition -Condition ([string]$distributionConfig.bootstrapperStrategy -eq "wix-burn") -Message "bootstrapperStrategy must remain wix-burn."

Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$distributionConfig.artifacts.msiPattern)) -Message "distribution-config.json artifacts.msiPattern is required."
Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$distributionConfig.artifacts.smallSetupExePattern)) -Message "distribution-config.json artifacts.smallSetupExePattern is required."
Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$distributionConfig.artifacts.artifactManifestPattern)) -Message "distribution-config.json artifacts.artifactManifestPattern is required."

Assert-Condition -Condition ([string]$distributionConfig.bundle.upgradeCode -match "^\{[0-9A-Fa-f-]{36}\}$") -Message "distribution-config.json bundle.upgradeCode must be GUID-formatted."

Write-Step "Validating UIAccess helper manifest and build wiring"
[xml]$helperManifestXml = Get-Content -Path "src/DictateAnywhere.UiAccessHelper/app.manifest" -Raw
$helperManifestNs = New-Object System.Xml.XmlNamespaceManager($helperManifestXml.NameTable)
[void]$helperManifestNs.AddNamespace("asm", "urn:schemas-microsoft-com:asm.v1")
[void]$helperManifestNs.AddNamespace("asmv3", "urn:schemas-microsoft-com:asm.v3")

$requestedExecutionLevel = $helperManifestXml.SelectSingleNode(
  "/asm:assembly/asmv3:trustInfo/asmv3:security/asmv3:requestedPrivileges/asmv3:requestedExecutionLevel",
  $helperManifestNs)
Assert-Condition -Condition ($null -ne $requestedExecutionLevel) -Message "UIAccess helper manifest must define requestedExecutionLevel."
Assert-Condition -Condition ($requestedExecutionLevel.GetAttribute("level") -eq "asInvoker") -Message "UIAccess helper requestedExecutionLevel must be asInvoker."
Assert-Condition -Condition ($requestedExecutionLevel.GetAttribute("uiAccess") -eq "true") -Message "UIAccess helper manifest must set uiAccess=true."

$buildInstallerScript = Get-Content -Path "scripts/build-installer.ps1" -Raw
Assert-Condition -Condition ($buildInstallerScript.Contains("DictateAnywhere.UiAccessHelper/DictateAnywhere.UiAccessHelper.csproj")) -Message "build-installer.ps1 must publish the UIAccess helper project."
Assert-Condition -Condition ($buildInstallerScript.Contains("Publishing DictateAnywhere.UiAccessHelper")) -Message "build-installer.ps1 must include a helper publish step."
Assert-Condition -Condition ($buildInstallerScript.Contains("DistributionMode")) -Message "build-installer.ps1 must expose DistributionMode for MSI and setup outputs."
Assert-Condition -Condition ($buildInstallerScript.Contains("WixToolset.BootstrapperApplications.wixext")) -Message "build-installer.ps1 must build Burn bundles with WixToolset.BootstrapperApplications.wixext."
Assert-Condition -Condition ($buildInstallerScript.Contains("artifactManifest")) -Message "build-installer.ps1 must emit an artifact manifest."
Assert-Condition -Condition ($buildInstallerScript.Contains("SignInstallerArtifacts")) -Message "build-installer.ps1 must expose SignInstallerArtifacts switch."
Assert-Condition -Condition ($buildInstallerScript.Contains("SigningCertificateThumbprint")) -Message "build-installer.ps1 must support signing certificate thumbprint input."
Assert-Condition -Condition ($buildInstallerScript.Contains("Invoke-SignArtifact")) -Message "build-installer.ps1 must implement installer artifact signing flow."
Assert-Condition -Condition ($buildInstallerScript.Contains("Assert-AuthenticodeGate")) -Message "build-installer.ps1 must enforce signature gate checks."
Assert-Condition -Condition ($buildInstallerScript.Contains("signatureTimestamped")) -Message "artifact manifest must include timestamped signature metadata."
Assert-Condition -Condition ($buildInstallerScript.Contains("validate-installer-staging-path.ps1")) -Message "build-installer.ps1 must validate its exact publish cleanup target and ancestor chain."
Assert-Condition -Condition ($buildInstallerScript.Contains("validate-installer-payload.ps1")) -Message "build-installer.ps1 must enforce the production payload boundary."

Write-Step "Exercising installer staging-path safety"
$stagingValidatorPath = Join-Path -Path $repoRoot -ChildPath "scripts/validate-installer-staging-path.ps1"
$stagingSmokeParent = [IO.Path]::GetFullPath((Join-Path -Path $repoRoot -ChildPath "artifacts/packaging-smoke"))
$stagingSmokeRoot = [IO.Path]::GetFullPath((Join-Path -Path $stagingSmokeParent -ChildPath ("staging safety " + [Guid]::NewGuid().ToString("N"))))
$stagingOutputRoot = Join-Path -Path $stagingSmokeRoot -ChildPath "output"
$stagingPublishPath = Join-Path -Path $stagingOutputRoot -ChildPath "publish/DictateAnywhere.App"
Assert-Condition `
  -Condition ($stagingSmokeRoot.StartsWith($stagingSmokeParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) `
  -Message "Staging safety scratch path must remain beneath artifacts/packaging-smoke."
$stagingValidatorScript = Get-Content -LiteralPath $stagingValidatorPath -Raw
Assert-Condition -Condition ($stagingValidatorScript.Contains("[IO.FileAttributes]::ReparsePoint")) -Message "Installer staging validation must reject reparse points."
Assert-Condition -Condition ($stagingValidatorScript.Contains('$currentPath = $currentPath.Parent')) -Message "Installer staging validation must inspect the complete existing ancestor chain."

try {
  New-Item -ItemType Directory -Path $stagingPublishPath -Force | Out-Null
  & $stagingValidatorPath `
    -OutputRoot ($stagingOutputRoot.Replace("\", "/")) `
    -PublishDirectory $stagingPublishPath `
    -RepoRoot $repoRoot

  $broadRootRejected = $false
  try {
    & $stagingValidatorPath `
      -OutputRoot $repoRoot `
      -PublishDirectory (Join-Path $repoRoot "publish/DictateAnywhere.App") `
      -RepoRoot $repoRoot
  }
  catch {
    $broadRootRejected = $true
  }
  Assert-Condition -Condition $broadRootRejected -Message "Installer staging validator accepted the repository root as OutputRoot."
}
finally {
  if (Test-Path -LiteralPath $stagingSmokeRoot) {
    Remove-Item -LiteralPath $stagingSmokeRoot -Recurse -Force
  }
}

Write-Step "Validating production executable publish boundary"
$parseErrors = $null
$tokens = $null
$installerAst = [System.Management.Automation.Language.Parser]::ParseFile(
  (Join-Path -Path $repoRoot -ChildPath "scripts/build-installer.ps1"),
  [ref]$tokens,
  [ref]$parseErrors)
Assert-Condition -Condition ($parseErrors.Count -eq 0) -Message "build-installer.ps1 must parse without PowerShell syntax errors."

$publishProjectPaths = @(
  $installerAst.FindAll(
    {
      param($node)
      $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
        $node.Value -match '(?i)\.csproj$'
    },
    $true) |
    ForEach-Object { ([string]$_.Value).Replace("\", "/").TrimStart("./").ToLowerInvariant() } |
    Sort-Object -Unique
)
$expectedPublishProjects = @(
  "src/dictateanywhere.app/dictateanywhere.app.csproj",
  "src/dictateanywhere.uiaccesshelper/dictateanywhere.uiaccesshelper.csproj"
)
Assert-Condition -Condition ($publishProjectPaths.Count -eq $expectedPublishProjects.Count) -Message "Installer build must identify exactly the App and UIAccess project files; found: $($publishProjectPaths -join ', ')."
foreach ($expectedProject in $expectedPublishProjects) {
  Assert-Condition -Condition ($publishProjectPaths -contains $expectedProject) -Message "Installer build is missing required publish project '$expectedProject'."
}

[xml]$appProjectXml = Get-Content -Path "src/DictateAnywhere.App/DictateAnywhere.App.csproj" -Raw
$developerExecutableNames = @(
  "DictateAnywhere.ModelBenchmark",
  "DictateAnywhere.TtsCli",
  "DictateAnywhere.Spikes",
  "DictateAnywhere.VoicePreviewGenerator"
)
$appProjectReferences = @($appProjectXml.SelectNodes("//ProjectReference") | ForEach-Object { [string]$_.Include })
foreach ($developerExecutableName in $developerExecutableNames) {
  $hasToolReference = @($appProjectReferences | Where-Object {
      [IO.Path]::GetFileNameWithoutExtension(([string]$_).Replace("/", "\")) -ieq $developerExecutableName
    }).Count -gt 0
  Assert-Condition -Condition (-not $hasToolReference) -Message "The production App must not reference developer executable project '$developerExecutableName'."
}

Write-Step "Exercising installer payload rejection cases"
$payloadValidatorPath = Join-Path -Path $repoRoot -ChildPath "scripts/validate-installer-payload.ps1"
$payloadSmokeParent = [IO.Path]::GetFullPath((Join-Path -Path $repoRoot -ChildPath "artifacts/packaging-smoke"))
$payloadSmokeRoot = [IO.Path]::GetFullPath((Join-Path -Path $payloadSmokeParent -ChildPath ([Guid]::NewGuid().ToString("N"))))
Assert-Condition `
  -Condition ($payloadSmokeRoot.StartsWith($payloadSmokeParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) `
  -Message "Packaging smoke scratch path must remain beneath artifacts/packaging-smoke."

function Reset-PayloadFixture {
  param([Parameter(Mandatory = $true)][string]$FixturePath)

  if (Test-Path -LiteralPath $FixturePath) {
    Remove-Item -LiteralPath $FixturePath -Recurse -Force
  }
  New-Item -ItemType Directory -Path $FixturePath -Force | Out-Null
}

function Assert-PayloadRejected {
  param(
    [Parameter(Mandatory = $true)][string]$FixturePath,
    [Parameter(Mandatory = $true)][string]$CaseName
  )

  $rejected = $false
  try {
    & $payloadValidatorPath -PayloadDirectory $FixturePath
  }
  catch {
    $rejected = $true
  }

  Assert-Condition -Condition $rejected -Message "Installer payload validator accepted invalid case '$CaseName'."
}

try {
  Reset-PayloadFixture -FixturePath $payloadSmokeRoot
  New-Item -ItemType File -Path (Join-Path $payloadSmokeRoot "DictateAnywhere.App.exe") | Out-Null
  New-Item -ItemType File -Path (Join-Path $payloadSmokeRoot "DictateAnywhere.UiAccessHelper.exe") | Out-Null
  & $payloadValidatorPath -PayloadDirectory $payloadSmokeRoot

  Remove-Item -LiteralPath (Join-Path $payloadSmokeRoot "DictateAnywhere.UiAccessHelper.exe")
  Assert-PayloadRejected -FixturePath $payloadSmokeRoot -CaseName "missing UIAccess helper"

  Reset-PayloadFixture -FixturePath $payloadSmokeRoot
  New-Item -ItemType File -Path (Join-Path $payloadSmokeRoot "DictateAnywhere.App.exe") | Out-Null
  New-Item -ItemType File -Path (Join-Path $payloadSmokeRoot "DictateAnywhere.UiAccessHelper.exe") | Out-Null
  New-Item -ItemType File -Path (Join-Path $payloadSmokeRoot "dIcTaTeAnYwHeRe.ModelBenchmark.deps.json") | Out-Null
  Assert-PayloadRejected -FixturePath $payloadSmokeRoot -CaseName "developer tool residue"

  Reset-PayloadFixture -FixturePath $payloadSmokeRoot
  New-Item -ItemType File -Path (Join-Path $payloadSmokeRoot "DictateAnywhere.App.exe") | Out-Null
  New-Item -ItemType File -Path (Join-Path $payloadSmokeRoot "DictateAnywhere.UiAccessHelper.exe") | Out-Null
  New-Item -ItemType File -Path (Join-Path $payloadSmokeRoot "DictateAnywhere.Unexpected.exe") | Out-Null
  Assert-PayloadRejected -FixturePath $payloadSmokeRoot -CaseName "unexpected product executable"

  Reset-PayloadFixture -FixturePath $payloadSmokeRoot
  New-Item -ItemType File -Path (Join-Path $payloadSmokeRoot "DictateAnywhere.App.exe") | Out-Null
  New-Item -ItemType File -Path (Join-Path $payloadSmokeRoot "DictateAnywhere.UiAccessHelper.exe") | Out-Null
  $nestedDirectory = Join-Path $payloadSmokeRoot "stale"
  New-Item -ItemType Directory -Path $nestedDirectory | Out-Null
  New-Item -ItemType File -Path (Join-Path $nestedDirectory "DictateAnywhere.App.exe") | Out-Null
  Assert-PayloadRejected -FixturePath $payloadSmokeRoot -CaseName "ambiguous nested application executable"
}
finally {
  if (Test-Path -LiteralPath $payloadSmokeRoot) {
    Remove-Item -LiteralPath $payloadSmokeRoot -Recurse -Force
  }
}

Write-Step "Validating WiX authoring contract"
[xml]$wixXml = Get-Content -Path "installer/wix/DictateAnywhere.wxs" -Raw
$namespaceManager = New-Object System.Xml.XmlNamespaceManager($wixXml.NameTable)
[void]$namespaceManager.AddNamespace("w", "http://wixtoolset.org/schemas/v4/wxs")

$packageNode = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "/w:Wix/w:Package" -FailureMessage "WiX package root was not found."
Assert-Condition -Condition ($packageNode.GetAttribute("Scope") -eq "perMachine") -Message "MSI scope must be perMachine for Program Files installation."
Assert-Condition -Condition ($packageNode.GetAttribute("Version") -eq '$(var.ProductVersion)') -Message "Package Version must be bound from ProductVersion variable."
Assert-Condition -Condition ($packageNode.GetAttribute("UpgradeCode") -eq '$(var.UpgradeCode)') -Message "Package UpgradeCode must be bound from UpgradeCode variable."

$majorUpgradeNode = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "/w:Wix/w:Package/w:MajorUpgrade" -FailureMessage "MajorUpgrade is required for upgrade compatibility."
Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace($majorUpgradeNode.GetAttribute("DowngradeErrorMessage"))) -Message "MajorUpgrade must define DowngradeErrorMessage."

Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:StandardDirectory[@Id='ProgramFiles64Folder']/w:Directory[@Id='INSTALLFOLDER']" -FailureMessage "INSTALLFOLDER must be located under ProgramFiles64Folder." | Out-Null
Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:StandardDirectory[@Id='CommonAppDataFolder']/w:Directory[@Id='COMMONAPPDATA_DICTATEANYWHERE']" -FailureMessage "CommonAppData directory component is required." | Out-Null
Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:StandardDirectory[@Id='LocalAppDataFolder']/w:Directory[@Id='LOCALAPPDATA_DICTATEANYWHERE']" -FailureMessage "LocalAppData directory component is required." | Out-Null

$featureNode = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "/w:Wix/w:Package/w:Feature[@Id='MainFeature']" -FailureMessage "MainFeature was not found."
Assert-Condition -Condition ($null -ne $featureNode.SelectSingleNode("w:ComponentGroupRef[@Id='AppFiles']", $namespaceManager)) -Message "MainFeature must include AppFiles component group."
Assert-Condition -Condition ($null -ne $featureNode.SelectSingleNode("w:ComponentRef[@Id='CommonDataDirectoryComponent']", $namespaceManager)) -Message "MainFeature must include CommonDataDirectoryComponent."
Assert-Condition -Condition ($null -ne $featureNode.SelectSingleNode("w:ComponentRef[@Id='LocalDataDirectoryComponent']", $namespaceManager)) -Message "MainFeature must include LocalDataDirectoryComponent."
Assert-Condition -Condition ($null -ne $featureNode.SelectSingleNode("w:ComponentRef[@Id='StartupOnLoginComponent']", $namespaceManager)) -Message "MainFeature must include StartupOnLoginComponent."
Assert-Condition -Condition ($null -ne $featureNode.SelectSingleNode("w:ComponentRef[@Id='UiAccessModeComponent']", $namespaceManager)) -Message "MainFeature must include UiAccessModeComponent."
Assert-Condition -Condition ($null -ne $featureNode.SelectSingleNode("w:ComponentRef[@Id='RunKnRegistrationComponent']", $namespaceManager)) -Message "MainFeature must include the run-kn command registration."

$appFilesNode = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:ComponentGroup[@Id='AppFiles']/w:Files" -FailureMessage "AppFiles must harvest publish output via Files include."
Assert-Condition -Condition ($appFilesNode.GetAttribute("Include") -eq '$(var.PublishDir)\**') -Message "AppFiles include pattern must recursively include publish output."

$runKnComponent = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:Component[@Id='RunKnRegistrationComponent']" -FailureMessage "RunKnRegistrationComponent was not found."
$runKnAppPath = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:Component[@Id='RunKnRegistrationComponent']/w:RegistryValue[@Root='HKLM'][@Key='Software\Microsoft\Windows\CurrentVersion\App Paths\run-kn.exe']" -FailureMessage "run-kn must be registered with Windows App Paths."
Assert-Condition -Condition ($runKnAppPath.GetAttribute("Value") -eq '[INSTALLFOLDER]DictateAnywhere.App.exe') -Message "run-kn App Paths registration must launch the installed application."
Assert-Condition -Condition ($runKnAppPath.GetAttribute("KeyPath") -eq 'yes') -Message "run-kn App Paths registration must be the component key path."
$runKnPath = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:Component[@Id='RunKnRegistrationComponent']/w:Environment[@Id='RunKnCommandPath'][@Name='PATH']" -FailureMessage "The installed launcher directory must be added to PATH."
Assert-Condition -Condition ($runKnPath.GetAttribute("Value") -eq '[INSTALLFOLDER]Launchers') -Message "The PATH entry must point at the installed launcher directory."
Assert-Condition -Condition ($runKnPath.GetAttribute("System") -eq 'yes') -Message "A per-machine install must register the launcher on the system PATH."

$runKnSource = Join-Path $repoRoot 'src\DictateAnywhere.App\Launchers\run-kn.cmd'
Assert-Condition -Condition (Test-Path -LiteralPath $runKnSource -PathType Leaf) -Message "The run-kn launcher source file is missing."
$runKnProject = [xml](Get-Content -LiteralPath (Join-Path $repoRoot 'src\DictateAnywhere.App\DictateAnywhere.App.csproj') -Raw)
$runKnContent = @($runKnProject.SelectNodes("//*[local-name()='Content'][@Include='Launchers\run-kn.cmd']"))
Assert-Condition -Condition ($runKnContent.Count -eq 1) -Message "The application project must publish exactly one run-kn launcher."
Assert-Condition -Condition ($runKnContent[0].GetAttribute('CopyToPublishDirectory') -eq 'PreserveNewest') -Message "The run-kn launcher must be copied to publish output."

$startupProperty = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:Property[@Id='STARTUP_ON_LOGIN']" -FailureMessage "STARTUP_ON_LOGIN property is required."
Assert-Condition -Condition ($startupProperty.GetAttribute("Value") -eq "auto") -Message "STARTUP_ON_LOGIN must default to auto: disabled fresh, preserve existing during upgrade/repair."
Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:Property[@Id='NILO_PREVIOUS_STARTUP']/w:RegistrySearch[@Root='HKCU'][@Key='Software\Microsoft\Windows\CurrentVersion\Run'][@Name='DictateAnywhere']" -FailureMessage "Startup preservation must inspect the legacy per-user Run entry." | Out-Null

$uiAccessProperty = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:Property[@Id='ENABLE_UIACCESS']" -FailureMessage "ENABLE_UIACCESS property is required."
Assert-Condition -Condition ($uiAccessProperty.GetAttribute("Value") -eq '$(var.EnableUiAccess)') -Message "ENABLE_UIACCESS must be bound from build variable EnableUiAccess."

$startupComponent = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:Component[@Id='StartupOnLoginComponent']" -FailureMessage "StartupOnLoginComponent was not found."
$startupConditionValue = $startupComponent.GetAttribute("Condition")
if ([string]::IsNullOrWhiteSpace($startupConditionValue)) {
  $legacyStartupCondition = $startupComponent.SelectSingleNode("w:Condition", $namespaceManager)
  if ($null -ne $legacyStartupCondition) {
    $startupConditionValue = ($legacyStartupCondition.InnerText).Trim()
  }
}

Assert-Condition -Condition ($startupConditionValue -eq 'STARTUP_ON_LOGIN=1 OR (STARTUP_ON_LOGIN="auto" AND (WIX_UPGRADE_DETECTED OR Installed) AND NILO_PREVIOUS_STARTUP)') -Message "Startup condition must support explicit enable/disable and preserve existing registration only on upgrade/repair."
Assert-Condition -Condition ($startupComponent.GetAttribute('Transitive') -eq 'yes') -Message "Startup condition must be reevaluated on repair."
Assert-Condition -Condition ($null -ne $startupComponent.SelectSingleNode("w:RegistryValue[@Root='HKCU'][@Key='Software\Microsoft\Windows\CurrentVersion\Run'][@Name='DictateAnywhere']", $namespaceManager)) -Message "StartupOnLoginComponent must register HKCU Run entry."

$commonDataComponent = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:Component[@Id='CommonDataDirectoryComponent']" -FailureMessage "CommonDataDirectoryComponent was not found."
$localDataComponent = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:Component[@Id='LocalDataDirectoryComponent']" -FailureMessage "LocalDataDirectoryComponent was not found."
Assert-Condition -Condition ($commonDataComponent.GetAttribute("Permanent") -eq "yes") -Message "Common data directory component must be permanent to preserve data on uninstall."
Assert-Condition -Condition ($localDataComponent.GetAttribute("Permanent") -eq "yes") -Message "Local data directory component must be permanent to preserve data on uninstall."

$uiAccessComponent = Get-SingleWixNode -WixXml $wixXml -NamespaceManager $namespaceManager -XPath "//w:Component[@Id='UiAccessModeComponent']" -FailureMessage "UiAccessModeComponent was not found."
$uiAccessConditionValue = $uiAccessComponent.GetAttribute("Condition")
if ([string]::IsNullOrWhiteSpace($uiAccessConditionValue)) {
  $legacyUiAccessCondition = $uiAccessComponent.SelectSingleNode("w:Condition", $namespaceManager)
  if ($null -ne $legacyUiAccessCondition) {
    $uiAccessConditionValue = ($legacyUiAccessCondition.InnerText).Trim()
  }
}

Assert-Condition -Condition ($uiAccessConditionValue -eq "ENABLE_UIACCESS=1") -Message "UiAccessModeComponent condition must be ENABLE_UIACCESS=1."
Assert-Condition -Condition ($null -ne $uiAccessComponent.SelectSingleNode("w:RegistryValue[@Root='HKLM'][@Key='Software\DictateAnywhere'][@Name='UiAccessEnabled']", $namespaceManager)) -Message "UiAccessModeComponent must write UiAccessEnabled registry value."

Write-Step "Validating Burn bundle authoring contract"
[xml]$smallBundleXml = Get-Content -Path "installer/bundle/DictateAnywhere.Small.Bundle.wxs" -Raw
$smallBundleNs = New-Object System.Xml.XmlNamespaceManager($smallBundleXml.NameTable)
[void]$smallBundleNs.AddNamespace("w", "http://wixtoolset.org/schemas/v4/wxs")
[void]$smallBundleNs.AddNamespace("bal", "http://wixtoolset.org/schemas/v4/wxs/bal")

$smallBundleNode = Get-SingleWixNode -WixXml $smallBundleXml -NamespaceManager $smallBundleNs -XPath "/w:Wix/w:Bundle" -FailureMessage "Small bundle root node was not found."
Assert-Condition -Condition ($smallBundleNode.GetAttribute("UpgradeCode") -eq '$(var.BundleUpgradeCode)') -Message "Small bundle UpgradeCode must bind BundleUpgradeCode."
Assert-Condition -Condition ($smallBundleNode.GetAttribute("Version") -eq '$(var.ProductVersion)') -Message "Small bundle Version must bind ProductVersion."
Get-SingleWixNode -WixXml $smallBundleXml -NamespaceManager $smallBundleNs -XPath "/w:Wix/w:Bundle/w:BootstrapperApplication/bal:WixStandardBootstrapperApplication" -FailureMessage "Small bundle must use WixStandardBootstrapperApplication." | Out-Null
$smallMsiPackage = @($smallBundleXml.SelectNodes("/w:Wix/w:Bundle/w:Chain/w:MsiPackage", $smallBundleNs))
Assert-Condition -Condition ($smallMsiPackage.Count -eq 1) -Message "Small bundle must include exactly one MSI package."
Assert-Condition -Condition ($smallMsiPackage[0].GetAttribute("SourceFile") -eq '$(var.MsiPath)') -Message "Small bundle must chain the app MSI path."

Write-Step "Packaging smoke checks passed"
Write-Host "Installer packaging contract is valid." -ForegroundColor Green
