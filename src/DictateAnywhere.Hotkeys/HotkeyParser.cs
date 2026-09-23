using System;
using System.Collections.Generic;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Hotkeys;

public static class HotkeyParser
{
  private static readonly IReadOnlyDictionary<string, int> NamedKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
  {
    ["Backspace"] = 0x08,
    ["Tab"] = 0x09,
    ["Enter"] = 0x0D,
    ["Esc"] = 0x1B,
    ["Space"] = 0x20,
    ["Delete"] = 0x2E,
    ["Left"] = 0x25,
    ["Up"] = 0x26,
    ["Right"] = 0x27,
    ["Down"] = 0x28,
    ["Home"] = 0x24,
    ["End"] = 0x23,
    ["Page Up"] = 0x21,
    ["PageDown"] = 0x22,
    ["Page Down"] = 0x22,
    ["Insert"] = 0x2D,
  };

  public static bool TryParse(string input, out HotkeyBinding binding, out string? errorMessage)
  {
    binding = new HotkeyBinding(HotkeyModifiers.None, 0);
    errorMessage = null;

    if (string.IsNullOrWhiteSpace(input))
    {
      errorMessage = "Hotkey text is empty.";
      return false;
    }

    string[] tokens = input.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (tokens.Length < 2)
    {
      errorMessage = "Hotkey requires at least one modifier and one key.";
      return false;
    }

    HotkeyModifiers modifiers = HotkeyModifiers.None;
    for (int i = 0; i < tokens.Length - 1; i++)
    {
      string token = tokens[i];
      if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
          token.Equals("Control", StringComparison.OrdinalIgnoreCase))
      {
        modifiers |= HotkeyModifiers.Control;
        continue;
      }

      if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
      {
        modifiers |= HotkeyModifiers.Alt;
        continue;
      }

      if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
      {
        modifiers |= HotkeyModifiers.Shift;
        continue;
      }

      if (token.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
          token.Equals("Windows", StringComparison.OrdinalIgnoreCase))
      {
        modifiers |= HotkeyModifiers.Windows;
        continue;
      }

      errorMessage = $"Unknown modifier token '{token}'.";
      return false;
    }

    if (modifiers == HotkeyModifiers.None)
    {
      errorMessage = "Hotkey must include at least one modifier.";
      return false;
    }

    if (!TryParseVirtualKey(tokens[^1], out int virtualKey))
    {
      errorMessage = $"Unknown key token '{tokens[^1]}'.";
      return false;
    }

    binding = new HotkeyBinding(modifiers, virtualKey);
    return true;
  }

  private static bool TryParseVirtualKey(string keyToken, out int virtualKey)
  {
    virtualKey = 0;

    if (keyToken.Length == 1)
    {
      char c = char.ToUpperInvariant(keyToken[0]);
      if (c is >= 'A' and <= 'Z')
      {
        virtualKey = c;
        return true;
      }

      if (c is >= '0' and <= '9')
      {
        virtualKey = c;
        return true;
      }
    }

    if (keyToken.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(keyToken.AsSpan(1), out int fNumber) &&
        fNumber is >= 1 and <= 24)
    {
      virtualKey = 0x6F + fNumber;
      return true;
    }

    if (NamedKeys.TryGetValue(keyToken, out int namedKey))
    {
      virtualKey = namedKey;
      return true;
    }

    return false;
  }
}
