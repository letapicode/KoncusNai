using System;
using System.Collections.Generic;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Hotkeys;

public static class HotkeyFormatter
{
  private static readonly IReadOnlyDictionary<int, string> NamedKeys = new Dictionary<int, string>
  {
    [0x08] = "Backspace",
    [0x09] = "Tab",
    [0x0D] = "Enter",
    [0x1B] = "Esc",
    [0x20] = "Space",
    [0x2E] = "Delete",
    [0x25] = "Left",
    [0x26] = "Up",
    [0x27] = "Right",
    [0x28] = "Down",
    [0x24] = "Home",
    [0x23] = "End",
    [0x21] = "Page Up",
    [0x22] = "Page Down",
    [0x2D] = "Insert",
  };

  public static string ToDisplayString(HotkeyBinding binding)
  {
    List<string> parts = [];

    if ((binding.Modifiers & HotkeyModifiers.Control) != 0)
    {
      parts.Add("Ctrl");
    }

    if ((binding.Modifiers & HotkeyModifiers.Alt) != 0)
    {
      parts.Add("Alt");
    }

    if ((binding.Modifiers & HotkeyModifiers.Shift) != 0)
    {
      parts.Add("Shift");
    }

    if ((binding.Modifiers & HotkeyModifiers.Windows) != 0)
    {
      parts.Add("Win");
    }

    parts.Add(FormatVirtualKey(binding.VirtualKey));
    return string.Join(" + ", parts);
  }

  public static string FormatVirtualKey(int virtualKey)
  {
    if (virtualKey is >= 0x30 and <= 0x39)
    {
      return ((char)virtualKey).ToString();
    }

    if (virtualKey is >= 0x41 and <= 0x5A)
    {
      return ((char)virtualKey).ToString();
    }

    if (virtualKey is >= 0x70 and <= 0x87)
    {
      int functionNumber = virtualKey - 0x6F;
      return $"F{functionNumber}";
    }

    if (NamedKeys.TryGetValue(virtualKey, out string? namedKey))
    {
      return namedKey;
    }

    return $"VK 0x{virtualKey:X2}";
  }
}
