using System;

namespace DictateAnywhere.Core.Contracts;

public sealed class HotkeyEventArgs : EventArgs
{
  public HotkeyEventArgs(DateTimeOffset observedAtUtc)
  {
    ObservedAtUtc = observedAtUtc;
  }

  public DateTimeOffset ObservedAtUtc { get; }
}
