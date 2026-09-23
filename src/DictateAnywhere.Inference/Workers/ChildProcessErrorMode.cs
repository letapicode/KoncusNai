using System;
using System.Runtime.InteropServices;

namespace DictateAnywhere.Inference;

internal static class ChildProcessErrorMode
{
  private const uint SEM_FAILCRITICALERRORS = 0x0001;
  private const uint SEM_NOGPFAULTERRORBOX = 0x0002;
  private const uint SEM_NOOPENFILEERRORBOX = 0x8000;
  private const uint SuppressedErrorMode =
    SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX;

  private static readonly object Sync = new();

  public static T RunWithSuppressedCrashDialogs<T>(Func<T> action)
  {
    ArgumentNullException.ThrowIfNull(action);

    lock (Sync)
    {
      uint previous = SetErrorMode(SuppressedErrorMode);
      try
      {
        return action();
      }
      finally
      {
        _ = SetErrorMode(previous);
      }
    }
  }

  [DllImport("kernel32.dll", SetLastError = true)]
  private static extern uint SetErrorMode(uint uMode);
}
