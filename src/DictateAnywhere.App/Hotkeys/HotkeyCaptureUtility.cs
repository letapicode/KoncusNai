using System.Windows.Input;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Hotkeys;

public static class HotkeyCaptureUtility
{
  public static bool TryCreateBinding(
    Key key,
    ModifierKeys modifiers,
    out HotkeyBinding binding,
    out string? errorMessage)
  {
    binding = new HotkeyBinding(HotkeyModifiers.None, 0);
    errorMessage = null;

    if (IsModifierKey(key))
    {
      errorMessage = "Press a non-modifier key with your modifiers.";
      return false;
    }

    HotkeyModifiers mappedModifiers = MapModifiers(modifiers);
    if (mappedModifiers == HotkeyModifiers.None)
    {
      errorMessage = "Hotkey must include at least one modifier key.";
      return false;
    }

    int virtualKey = KeyInterop.VirtualKeyFromKey(key);
    if (virtualKey <= 0)
    {
      errorMessage = "Unable to resolve key to Windows virtual-key code.";
      return false;
    }

    binding = new HotkeyBinding(mappedModifiers, virtualKey);
    return true;
  }

  private static HotkeyModifiers MapModifiers(ModifierKeys modifiers)
  {
    HotkeyModifiers mapped = HotkeyModifiers.None;

    if ((modifiers & ModifierKeys.Control) != 0)
    {
      mapped |= HotkeyModifiers.Control;
    }

    if ((modifiers & ModifierKeys.Alt) != 0)
    {
      mapped |= HotkeyModifiers.Alt;
    }

    if ((modifiers & ModifierKeys.Shift) != 0)
    {
      mapped |= HotkeyModifiers.Shift;
    }

    if ((modifiers & ModifierKeys.Windows) != 0)
    {
      mapped |= HotkeyModifiers.Windows;
    }

    return mapped;
  }

  private static bool IsModifierKey(Key key)
  {
    return key is Key.LeftAlt
      or Key.RightAlt
      or Key.LeftCtrl
      or Key.RightCtrl
      or Key.LeftShift
      or Key.RightShift
      or Key.LWin
      or Key.RWin;
  }
}
