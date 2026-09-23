using System;
using Microsoft.Win32;

namespace DictateAnywhere.App.Startup;

public sealed class RegistryRunKeyStartupStore : IStartupRegistrationStore
{
  private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

  public string? ReadValue(string valueName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(valueName);

    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
    return key?.GetValue(valueName) as string;
  }

  public void WriteValue(string valueName, string value)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
    ArgumentException.ThrowIfNullOrWhiteSpace(value);

    using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
      ?? throw new InvalidOperationException("Unable to open startup run key for writing.");
    key.SetValue(valueName, value, RegistryValueKind.String);
  }

  public void DeleteValue(string valueName)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(valueName);

    using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
    key?.DeleteValue(valueName, throwOnMissingValue: false);
  }
}
