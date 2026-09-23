[CmdletBinding()]
param([string]$LauncherDirectory = '', [string]$StartMenuDirectory = '', [switch]$SkipPathUpdate, [string]$PreviousRepoRoot = '')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $LauncherDirectory) { $LauncherDirectory = Join-Path $env:LOCALAPPDATA 'Notype\bin' }
if (-not $StartMenuDirectory) { $StartMenuDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs' }
$LauncherDirectory = [IO.Path]::GetFullPath($LauncherDirectory)
$StartMenuDirectory = [IO.Path]::GetFullPath($StartMenuDirectory)
$launcherPath = Join-Path $LauncherDirectory 'run-kn.cmd'
$shortcutPath = Join-Path $StartMenuDirectory 'Koncus Nai.lnk'
$target = Join-Path $repoRoot 'scripts\run-kn.ps1'
$icon = Join-Path $repoRoot 'src\DictateAnywhere.App\Assets\Brand\KoncusNai.ico'
if (-not (Test-Path -LiteralPath $target) -or -not (Test-Path -LiteralPath $icon)) { throw 'Launcher source or brand icon is missing.' }
function Get-LauncherContent([string]$Script) { return "@echo off`r`npowershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$Script`" %*`r`n" }
function Test-SamePath([string]$First, [string]$Second) {
  return $First -and $Second -and [string]::Equals([IO.Path]::GetFullPath($First).TrimEnd('\'), [IO.Path]::GetFullPath($Second).TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}
function Send-EnvironmentChangedNotification {
  if ($null -eq ('KoncusNai.LauncherNativeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace KoncusNai {
  public static class LauncherNativeMethods {
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
      IntPtr window, uint message, IntPtr wordParameter, string parameter,
      uint flags, uint timeoutMilliseconds, out IntPtr result);

    public static void NotifyEnvironmentChanged() {
      IntPtr ignored;
      // HWND_BROADCAST notifies Explorer so subsequent Windows Run launches see PATH.
      SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, "Environment", 0x0002, 5000, out ignored);
    }
  }
}
'@
  }
  [KoncusNai.LauncherNativeMethods]::NotifyEnvironmentChanged()
}
# A checkout move is explicit; never infer ownership from a filename alone.
if ($PreviousRepoRoot) { $PreviousRepoRoot = [IO.Path]::GetFullPath($PreviousRepoRoot) }
$ownedRoots = @($repoRoot)
if ($PreviousRepoRoot) { $ownedRoots += $PreviousRepoRoot }
foreach ($path in @($LauncherDirectory, $StartMenuDirectory, $launcherPath, $shortcutPath)) {
  $cursor = $path
  while ($cursor) {
    if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
      throw "Launcher path traverses a reparse point: $cursor"
    }
    $cursor = Split-Path -Parent $cursor
  }
}
$contents = Get-LauncherContent $target
$ownedCurrentContents = @($contents)
if ($PreviousRepoRoot) {
  $ownedCurrentContents += Get-LauncherContent (Join-Path $PreviousRepoRoot 'scripts\run-kn.ps1')
}
$oldPaths = @('run-notype.cmd', 'run-nilo.cmd', 'run-koncus-nai.cmd') | ForEach-Object { Join-Path $LauncherDirectory $_ }
$knownContents = @('run-notype.ps1', 'run-nilo.ps1', 'run-koncus-nai.ps1', 'run-kn.ps1') | ForEach-Object {
  $body = Get-LauncherContent (Join-Path $repoRoot "scripts\$_")
  $body
  $body.Replace('powershell.exe ', 'powershell ')
}
$shell = New-Object -ComObject WScript.Shell
try {
  if ((Test-Path -LiteralPath $launcherPath) -and $ownedCurrentContents -cnotcontains [IO.File]::ReadAllText($launcherPath)) {
    throw "Existing run-kn command is foreign or customized: $launcherPath"
  }
  if (Test-Path -LiteralPath $shortcutPath) {
    $link = $shell.CreateShortcut($shortcutPath)
    try {
      $validTarget = @($oldPaths + $launcherPath | Where-Object { Test-SamePath $_ $link.TargetPath }).Count -gt 0
      $validRoot = @($ownedRoots | Where-Object { Test-SamePath $_ $link.WorkingDirectory }).Count -gt 0
      $validIcon = @($ownedRoots | Where-Object {
        [string]::Equals($link.IconLocation, ((Join-Path $_ 'src\DictateAnywhere.App\Assets\Brand\KoncusNai.ico') + ',0'), [StringComparison]::OrdinalIgnoreCase)
      }).Count -gt 0
      if (-not $validTarget -or -not $validRoot -or -not $validIcon -or $link.Arguments -or
          $link.Description -cne 'Start Koncus Nai' -or $link.Hotkey -or $link.WindowStyle -ne 1) {
        throw "Existing Koncus Nai shortcut is foreign or customized: $shortcutPath"
      }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
  }
  New-Item -ItemType Directory -Path $LauncherDirectory, $StartMenuDirectory -Force | Out-Null
  [IO.File]::WriteAllText($launcherPath, $contents, [Text.UTF8Encoding]::new($false))
  $link = $shell.CreateShortcut($shortcutPath)
  try {
    $link.TargetPath = $launcherPath; $link.WorkingDirectory = $repoRoot
    $link.IconLocation = "$icon,0"; $link.Description = 'Start Koncus Nai'; $link.Save()
  } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
  $protectedTargets = @()
  foreach ($name in @('Notype.lnk', 'Nilo.lnk')) {
    $path = Join-Path $StartMenuDirectory $name
    if (-not (Test-Path -LiteralPath $path)) { continue }
    $link = $shell.CreateShortcut($path)
    try {
      $owned = (Test-SamePath $link.WorkingDirectory $repoRoot) -and -not $link.Arguments -and
        @($oldPaths | Where-Object { Test-SamePath $_ $link.TargetPath }).Count -gt 0
      if (-not $owned) { $protectedTargets += $link.TargetPath }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link) }
    if ($owned) { Remove-Item -LiteralPath $path }
  }
  foreach ($oldPath in $oldPaths) {
    if (-not (Test-Path -LiteralPath $oldPath)) { continue }
    $protected = @($protectedTargets | Where-Object { Test-SamePath $_ $oldPath }).Count -gt 0
    if (-not $protected -and $knownContents -ccontains [IO.File]::ReadAllText($oldPath)) { Remove-Item -LiteralPath $oldPath }
    else { Write-Warning "Preserved customized launcher or shortcut dependency: $oldPath" }
  }
  if (-not $SkipPathUpdate) {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($null -eq $userPath) { $userPath = '' }
    $present = @($userPath -split ';' | Where-Object { $_ -and (Test-SamePath $_ $LauncherDirectory) }).Count -gt 0
    if (-not $present) {
      [Environment]::SetEnvironmentVariable('Path', ($userPath.TrimEnd(';') + ';' + $LauncherDirectory).TrimStart(';'), 'User')
    }
    Send-EnvironmentChangedNotification
  }
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
Write-Host 'Installed run-kn and the Koncus Nai shortcut. Open a new terminal for PATH changes.'
