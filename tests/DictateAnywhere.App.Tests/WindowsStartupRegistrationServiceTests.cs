using System;
using System.Collections.Generic;
using DictateAnywhere.App.Startup;

namespace DictateAnywhere.App.Tests;

public sealed class WindowsStartupRegistrationServiceTests
{
  [Xunit.Fact]
  public void SetEnabled_PersistsAcrossInstances_UsingSharedStore()
  {
    InMemoryStartupStore store = new();
    WindowsStartupRegistrationService first = new(
      store,
      valueName: "DictateAnywhere",
      executablePath: @"C:\Program Files\DictateAnywhere\DictateAnywhere.App.exe");

    first.SetEnabled(true);
    Xunit.Assert.True(first.IsEnabled());
    Xunit.Assert.Equal(
      "\"C:\\Program Files\\DictateAnywhere\\DictateAnywhere.App.exe\" --background",
      store.ReadValue("DictateAnywhere"));

    WindowsStartupRegistrationService second = new(
      store,
      valueName: "DictateAnywhere",
      executablePath: @"C:\Program Files\DictateAnywhere\DictateAnywhere.App.exe");
    Xunit.Assert.True(second.IsEnabled());

    second.SetEnabled(false);
    WindowsStartupRegistrationService third = new(
      store,
      valueName: "DictateAnywhere",
      executablePath: @"C:\Program Files\DictateAnywhere\DictateAnywhere.App.exe");
    Xunit.Assert.False(third.IsEnabled());
  }

  [Xunit.Theory]
  [Xunit.InlineData("--background", true)]
  [Xunit.InlineData("--BACKGROUND", true)]
  [Xunit.InlineData("--help", false)]
  public void LaunchOptions_DistinguishInteractiveAndBackgroundStarts(string argument, bool expected)
  {
    Xunit.Assert.Equal(expected, AppLaunchOptions.IsBackgroundLaunch([argument]));
  }

  private sealed class InMemoryStartupStore : IStartupRegistrationStore
  {
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public string? ReadValue(string valueName)
    {
      values.TryGetValue(valueName, out string? value);
      return value;
    }

    public void WriteValue(string valueName, string value)
    {
      values[valueName] = value;
    }

    public void DeleteValue(string valueName)
    {
      _ = values.Remove(valueName);
    }
  }
}
