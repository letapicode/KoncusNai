[CmdletBinding()]
param([Parameter(Mandatory)][string]$MsiPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$MsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$config = Get-Content -LiteralPath (Join-Path $repoRoot 'installer/wix/installer-config.json') -Raw | ConvertFrom-Json
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $null
$session = $null
function Read-Scalar([string]$Sql) {
  $view = $database.OpenView($Sql)
  try {
    [void]$view.Execute()
    $record = $view.Fetch()
    if ($null -eq $record) { throw "Missing MSI record: $Sql" }
    try { return $record.StringData(1) }
    finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
  }
  finally { [void]$view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
}
function Set-SessionProperty([string]$Name, [string]$Value) {
  [void]$session.GetType().InvokeMember('Property', [Reflection.BindingFlags]::SetProperty, $null, $session, @($Name, $Value))
}
try {
  $database = $installer.OpenDatabase($MsiPath, 0)
  if ((Read-Scalar 'SELECT `Value` FROM `Property` WHERE `Property` = ''ProductName''') -cne 'Koncus Nai') { throw 'MSI product name is incorrect.' }
  if ((Read-Scalar 'SELECT `Value` FROM `Property` WHERE `Property` = ''UpgradeCode''') -ine $config.upgradeCode) { throw 'MSI upgrade family changed.' }
  if ((Read-Scalar 'SELECT `Name` FROM `Shortcut` WHERE `Shortcut` = ''DictateAnywhereStartMenuShortcut''') -notmatch '(^|\|)Koncus Nai$') { throw 'MSI shortcut name is incorrect.' }
  if ((Read-Scalar 'SELECT `Icon_` FROM `Shortcut` WHERE `Shortcut` = ''DictateAnywhereStartMenuShortcut''') -cne 'KoncusNaiProductIcon') { throw 'MSI shortcut icon is incorrect.' }
  if ((Read-Scalar 'SELECT `Value` FROM `Registry` WHERE `Key` = ''Software\Microsoft\Windows\CurrentVersion\App Paths\run-kn.exe''') -cne '[INSTALLFOLDER]DictateAnywhere.App.exe') { throw 'MSI run-kn App Paths registration is incorrect.' }
  if ((Read-Scalar 'SELECT `Value` FROM `Environment` WHERE `Environment` = ''RunKnCommandPath''') -cne '[~];[INSTALLFOLDER]Launchers') { throw 'MSI run-kn PATH registration is incorrect.' }
  $condition = Read-Scalar 'SELECT `Condition` FROM `Component` WHERE `Component` = ''StartupOnLoginComponent'''

  # Flag 1 creates a restricted session incapable of changing machine state.
  # No installation actions are invoked; use the real MSI condition evaluator.
  $session = $installer.OpenPackage($MsiPath, 1)
  $cases = 0
  foreach ($choice in @('auto', '0', '1')) {
    foreach ($phase in @('fresh', 'upgrade', 'repair')) {
      foreach ($previouslyEnabled in @($false, $true)) {
        Set-SessionProperty 'STARTUP_ON_LOGIN' $choice
        Set-SessionProperty 'WIX_UPGRADE_DETECTED' $(if ($phase -eq 'upgrade') { '{00000000-0000-0000-0000-000000000001}' } else { '' })
        Set-SessionProperty 'Installed' $(if ($phase -eq 'repair') { '1' } else { '' })
        Set-SessionProperty 'NILO_PREVIOUS_STARTUP' $(if ($previouslyEnabled) { 'fixture-command' } else { '' })
        $expected = ($choice -eq '1') -or ($choice -eq 'auto' -and $phase -ne 'fresh' -and $previouslyEnabled)
        $actual = $session.EvaluateCondition($condition)
        if ($actual -ne [int]$expected) { throw "Startup condition failed: choice=$choice phase=$phase prior=$previouslyEnabled actual=$actual" }
        $cases++
      }
    }
  }
  Write-Host "PASS: compiled MSI identity, shortcut/icon, run-kn registration, upgrade family, and $cases startup preference cases. No installation performed."
}
finally {
  if ($null -ne $session) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session) }
  if ($null -ne $database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
  [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}
