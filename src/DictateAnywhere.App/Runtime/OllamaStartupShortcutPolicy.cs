using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DictateAnywhere.App.Runtime;

/// <summary>
/// Keeps the KoncusNai-managed Ollama desktop runtime on demand by removing only
/// Ollama's exact per-user Windows Startup shortcut.
/// </summary>
internal static class OllamaStartupShortcutPolicy
{
  private const uint ShellLinkRawPath = 0x0004;

  internal static string StartupShortcutPath { get; } = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Startup),
    "Ollama.lnk");

  public static bool TryDisableAutomaticStartup()
  {
    return TryDisableAutomaticStartup(StartupShortcutPath, ResolveShortcutTarget, File.Delete);
  }

  internal static bool TryDisableAutomaticStartup(
    string shortcutPath,
    Func<string, string?> targetResolver,
    Action<string> deleteShortcut)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
    ArgumentNullException.ThrowIfNull(targetResolver);
    ArgumentNullException.ThrowIfNull(deleteShortcut);

    if (!File.Exists(shortcutPath))
    {
      return false;
    }

    try
    {
      string? targetPath = targetResolver(shortcutPath);
      if (!IsExpectedOllamaTarget(targetPath))
      {
        return false;
      }

      deleteShortcut(shortcutPath);
      return true;
    }
    catch (Exception ex) when (ex is IOException
                               or UnauthorizedAccessException
                               or COMException
                               or InvalidCastException)
    {
      return false;
    }
  }

  internal static bool IsExpectedOllamaTarget(string? targetPath)
  {
    if (string.IsNullOrWhiteSpace(targetPath))
    {
      return false;
    }

    string fileName = Path.GetFileName(targetPath.Trim());
    return fileName.Equals("ollama.exe", StringComparison.OrdinalIgnoreCase)
           || fileName.Equals("ollama app.exe", StringComparison.OrdinalIgnoreCase);
  }

  private static string? ResolveShortcutTarget(string shortcutPath)
  {
    IShellLinkW shellLink = (IShellLinkW)(object)new ShellLink();
    try
    {
      ((IPersistFile)shellLink).Load(shortcutPath, 0);
      StringBuilder targetPath = new(32_768);
      shellLink.GetPath(targetPath, targetPath.Capacity, IntPtr.Zero, ShellLinkRawPath);
      return targetPath.Length == 0 ? null : targetPath.ToString();
    }
    finally
    {
      if (Marshal.IsComObject(shellLink))
      {
        _ = Marshal.FinalReleaseComObject(shellLink);
      }
    }
  }

  [ComImport]
  [Guid("00021401-0000-0000-C000-000000000046")]
  private sealed class ShellLink
  {
  }

  [ComImport]
  [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  [Guid("000214F9-0000-0000-C000-000000000046")]
  private interface IShellLinkW
  {
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder filePath, int maximumPath, IntPtr findData, uint flags);

    void GetIdList(out IntPtr itemIdList);

    void SetIdList(IntPtr itemIdList);

    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int maximumName);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);

    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maximumPath);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maximumPath);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

    void GetHotkey(out short hotkey);

    void SetHotkey(short hotkey);

    void GetShowCommand(out int showCommand);

    void SetShowCommand(int showCommand);

    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maximumPath, out int iconIndex);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

    void Resolve(IntPtr windowHandle, uint flags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
  }
}
