using System;

namespace DictateAnywhere.Core.Contracts;

[Flags]
public enum HotkeyModifiers
{
  None = 0,
  Alt = 1,
  Control = 2,
  Shift = 4,
  Windows = 8,
}
