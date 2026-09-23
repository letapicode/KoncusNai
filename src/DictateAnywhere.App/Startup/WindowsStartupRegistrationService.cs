using System;
using System.IO;
using System.Reflection;

namespace DictateAnywhere.App.Startup;

public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
  private readonly IStartupRegistrationStore store;
  private readonly string valueName;
  private readonly string startupCommand;

  public WindowsStartupRegistrationService()
    : this(
      new RegistryRunKeyStartupStore(),
      "DictateAnywhere",
      ResolveExecutablePath())
  {
  }

  public WindowsStartupRegistrationService(
    IStartupRegistrationStore store,
    string valueName,
    string executablePath)
  {
    this.store = store ?? throw new ArgumentNullException(nameof(store));
    if (string.IsNullOrWhiteSpace(valueName))
    {
      throw new ArgumentException("Startup value name must not be empty.", nameof(valueName));
    }

    if (string.IsNullOrWhiteSpace(executablePath))
    {
      throw new ArgumentException("Executable path must not be empty.", nameof(executablePath));
    }

    this.valueName = valueName;
    startupCommand = BuildStartupCommand(executablePath);
  }

  public bool IsEnabled()
  {
    string? configured = store.ReadValue(valueName);
    return string.Equals(configured, startupCommand, StringComparison.OrdinalIgnoreCase);
  }

  public void SetEnabled(bool enabled)
  {
    if (enabled)
    {
      store.WriteValue(valueName, startupCommand);
      return;
    }

    store.DeleteValue(valueName);
  }

  private static string ResolveExecutablePath()
  {
    string? processPath = Environment.ProcessPath;
    if (!string.IsNullOrWhiteSpace(processPath))
    {
      return processPath;
    }

    string? assemblyPath = Assembly.GetEntryAssembly()?.Location;
    if (!string.IsNullOrWhiteSpace(assemblyPath))
    {
      return assemblyPath;
    }

    throw new InvalidOperationException("Unable to resolve executable path for startup registration.");
  }

  private static string BuildStartupCommand(string executablePath)
  {
    string fullPath = Path.GetFullPath(executablePath);
    return $"\"{fullPath}\" {AppLaunchOptions.BackgroundArgument}";
  }
}
