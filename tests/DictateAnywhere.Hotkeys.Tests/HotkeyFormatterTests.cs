using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.Hotkeys.Tests;

public sealed class HotkeyFormatterTests
{
  [Xunit.Fact]
  public void ToDisplayString_FormatsModifierAndNamedKey()
  {
    HotkeyBinding binding = new(
      HotkeyModifiers.Control | HotkeyModifiers.Alt,
      0x20);

    string display = HotkeyFormatter.ToDisplayString(binding);

    Xunit.Assert.Equal("Ctrl + Alt + Space", display);
  }

  [Xunit.Fact]
  public void ToDisplayString_FormatsFunctionKeys()
  {
    HotkeyBinding binding = new(
      HotkeyModifiers.Windows | HotkeyModifiers.Shift,
      0x74);

    string display = HotkeyFormatter.ToDisplayString(binding);

    Xunit.Assert.Equal("Shift + Win + F5", display);
  }

  [Xunit.Fact]
  public void FormatVirtualKey_ReturnsHexForUnknownKey()
  {
    string key = HotkeyFormatter.FormatVirtualKey(0xAA);

    Xunit.Assert.Equal("VK 0xAA", key);
  }
}

