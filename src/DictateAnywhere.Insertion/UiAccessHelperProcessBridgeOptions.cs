using System;
using System.IO;

namespace DictateAnywhere.Insertion;

public sealed record UiAccessHelperProcessBridgeOptions(
  string HelperExecutablePath,
  TimeSpan InvocationTimeout,
  bool RequireSecureInstallLocation,
  bool RequireSignedHostBinary,
  bool RequireSignedHelperBinary)
{
  public static UiAccessHelperProcessBridgeOptions Default { get; } = new(
    HelperExecutablePath: Path.Combine(AppContext.BaseDirectory, "DictateAnywhere.UiAccessHelper.exe"),
    InvocationTimeout: TimeSpan.FromSeconds(12),
    RequireSecureInstallLocation: true,
    RequireSignedHostBinary: true,
    RequireSignedHelperBinary: true);
}
