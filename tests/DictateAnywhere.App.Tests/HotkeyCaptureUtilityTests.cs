using System.Windows.Input;
using DictateAnywhere.App.Hotkeys;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class HotkeyCaptureUtilityTests
{
  [Xunit.Fact]
  public void TryCreateBinding_ReturnsBinding_ForValidKeyAndModifiers()
  {
    bool success = HotkeyCaptureUtility.TryCreateBinding(
      key: Key.Space,
      modifiers: ModifierKeys.Control | ModifierKeys.Alt,
      out HotkeyBinding binding,
      out string? error);

    Xunit.Assert.True(success);
    Xunit.Assert.Null(error);
    Xunit.Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, binding.Modifiers);
    Xunit.Assert.Equal(0x20, binding.VirtualKey);
  }

  [Xunit.Fact]
  public void TryCreateBinding_Fails_ForModifierOnlyKey()
  {
    bool success = HotkeyCaptureUtility.TryCreateBinding(
      key: Key.LeftCtrl,
      modifiers: ModifierKeys.Control,
      out _,
      out string? error);

    Xunit.Assert.False(success);
    Xunit.Assert.NotNull(error);
  }

  [Xunit.Fact]
  public void TryCreateBinding_Fails_WithoutModifiers()
  {
    bool success = HotkeyCaptureUtility.TryCreateBinding(
      key: Key.A,
      modifiers: ModifierKeys.None,
      out _,
      out string? error);

    Xunit.Assert.False(success);
    Xunit.Assert.NotNull(error);
  }
}
