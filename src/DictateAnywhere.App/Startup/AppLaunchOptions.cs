using System;
using System.Collections.Generic;

namespace DictateAnywhere.App.Startup;

internal static class AppLaunchOptions
{
  internal const string BackgroundArgument = "--background";

  internal static bool IsBackgroundLaunch(IEnumerable<string> arguments)
  {
    ArgumentNullException.ThrowIfNull(arguments);
    return arguments.Any(argument => string.Equals(argument, BackgroundArgument, StringComparison.OrdinalIgnoreCase));
  }
}
