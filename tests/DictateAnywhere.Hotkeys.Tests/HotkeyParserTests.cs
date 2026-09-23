using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.Hotkeys.Tests;

public sealed class HotkeyParserTests
{
  [Xunit.Fact]
  public void TryParse_ParsesModifiersAndNamedKey()
  {
    bool success = HotkeyParser.TryParse("Ctrl + Alt + Space", out HotkeyBinding binding, out string? error);

    Xunit.Assert.True(success);
    Xunit.Assert.Null(error);
    Xunit.Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, binding.Modifiers);
    Xunit.Assert.Equal(0x20, binding.VirtualKey);
  }

  [Xunit.Fact]
  public void TryParse_FailsWhenModifierMissing()
  {
    bool success = HotkeyParser.TryParse("Space", out _, out string? error);

    Xunit.Assert.False(success);
    Xunit.Assert.NotNull(error);
  }

  [Xunit.Fact]
  public void TryParse_FailsOnUnknownToken()
  {
    bool success = HotkeyParser.TryParse("Ctrl + Hyper", out _, out string? error);

    Xunit.Assert.False(success);
    Xunit.Assert.NotNull(error);
  }
}

