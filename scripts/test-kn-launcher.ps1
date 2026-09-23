[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratch = Join-Path $repoRoot ('artifacts\kn-launcher-tests\' + [Guid]::NewGuid().ToString('N'))
$launcher = Join-Path $scratch 'bin'
$menu = Join-Path $scratch 'menu'
New-Item -ItemType Directory -Path $launcher, $menu -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
function Assert-True([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Save-Link([string]$Path, [string]$Target, [string]$WorkingDirectory, [string]$Arguments = '') {
  $link = $shell.CreateShortcut($Path)
  try { $link.TargetPath = $Target; $link.WorkingDirectory = $WorkingDirectory; $link.Arguments = $Arguments; $link.Save() }
  finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
}
try {
  $legacy = Join-Path $launcher 'run-notype.cmd'
  $legacyScript = Join-Path $repoRoot 'scripts\run-notype.ps1'
  $legacyBody = "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$legacyScript`" %*`r`n"
  [IO.File]::WriteAllText($legacy, $legacyBody, [Text.UTF8Encoding]::new($false))
  Save-Link (Join-Path $menu 'Notype.lnk') $legacy $repoRoot

  & (Join-Path $PSScriptRoot 'install-kn-launcher.ps1') -LauncherDirectory $launcher -StartMenuDirectory $menu -SkipPathUpdate
  Assert-True (Test-Path (Join-Path $launcher 'run-kn.cmd')) 'run-kn was not installed.'
  Assert-True (-not (Test-Path $legacy)) 'Owned legacy launcher was not removed.'
  Assert-True (-not (Test-Path (Join-Path $menu 'Notype.lnk'))) 'Owned legacy shortcut was not removed.'
  $newLink = Join-Path $menu 'Koncus Nai.lnk'
  Assert-True (Test-Path $newLink) 'Koncus Nai shortcut was not created.'

  $custom = Join-Path $launcher 'run-nilo.cmd'
  [IO.File]::WriteAllText($custom, '@echo customized', [Text.UTF8Encoding]::new($false))
  Save-Link (Join-Path $menu 'Nilo.lnk') $custom $repoRoot '--custom'
  & (Join-Path $PSScriptRoot 'install-kn-launcher.ps1') -LauncherDirectory $launcher -StartMenuDirectory $menu -SkipPathUpdate
  Assert-True (Test-Path $custom) 'Customized legacy launcher was removed.'
  Assert-True (Test-Path (Join-Path $menu 'Nilo.lnk')) 'Customized shortcut was removed.'

  # An exact owned launcher/shortcut from a different checkout may migrate only
  # when that previous checkout is explicitly supplied.
  $previousRoot = Join-Path $scratch 'previous checkout'
  $previousScript = Join-Path $previousRoot 'scripts\run-kn.ps1'
  $previousBody = "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$previousScript`" %*`r`n"
  $currentLauncher = Join-Path $launcher 'run-kn.cmd'
  [IO.File]::WriteAllText($currentLauncher, $previousBody, [Text.UTF8Encoding]::new($false))
  $link = $shell.CreateShortcut($newLink)
  try {
    $link.WorkingDirectory = $previousRoot
    $link.IconLocation = (Join-Path $previousRoot 'src\DictateAnywhere.App\Assets\Brand\KoncusNai.ico') + ',0'
    $link.Save()
  } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
  $rejected = $false
  try { & (Join-Path $PSScriptRoot 'install-kn-launcher.ps1') -LauncherDirectory $launcher -StartMenuDirectory $menu -SkipPathUpdate }
  catch { $rejected = $_.Exception.Message -like '*foreign or customized*' }
  Assert-True $rejected 'A different checkout was accepted without explicit migration.'
  # Custom shortcut metadata must fail before modifying the launcher.
  $link = $shell.CreateShortcut($newLink)
  try { $link.Arguments = '--custom'; $link.Save() }
  finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
  $rejected = $false
  try { & (Join-Path $PSScriptRoot 'install-kn-launcher.ps1') -LauncherDirectory $launcher -StartMenuDirectory $menu -SkipPathUpdate -PreviousRepoRoot $previousRoot }
  catch { $rejected = $_.Exception.Message -like '*foreign or customized*' }
  Assert-True $rejected 'A customized move shortcut was accepted.'
  Assert-True ([IO.File]::ReadAllText($currentLauncher) -ceq $previousBody) 'Rejected migration changed launcher bytes.'
  $link = $shell.CreateShortcut($newLink)
  try { $link.Arguments = ''; $link.Save() }
  finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
  & (Join-Path $PSScriptRoot 'install-kn-launcher.ps1') -LauncherDirectory $launcher -StartMenuDirectory $menu -SkipPathUpdate -PreviousRepoRoot $previousRoot
  Assert-True ([IO.File]::ReadAllText($currentLauncher).Contains((Join-Path $repoRoot 'scripts\run-kn.ps1'))) 'Launcher did not move to current checkout.'
  $link = $shell.CreateShortcut($newLink)
  try { Assert-True ($link.WorkingDirectory -eq $repoRoot) 'Shortcut working directory did not migrate.' }
  finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }

  [IO.File]::WriteAllText((Join-Path $launcher 'run-kn.cmd'), '@echo foreign', [Text.UTF8Encoding]::new($false))
  $rejected = $false
  try { & (Join-Path $PSScriptRoot 'install-kn-launcher.ps1') -LauncherDirectory $launcher -StartMenuDirectory $menu -SkipPathUpdate }
  catch { $rejected = $_.Exception.Message -like '*foreign or customized*' }
  Assert-True $rejected 'A foreign run-kn collision was not rejected.'
}
finally {
  [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
  if (Test-Path $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
Write-Host 'run-kn launcher migration checks passed.'
