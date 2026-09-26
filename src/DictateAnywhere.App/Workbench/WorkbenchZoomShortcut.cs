using System.Windows.Input;

namespace DictateAnywhere.App.Workbench;

internal static class WorkbenchZoomShortcut
{
  internal static int? Delta(Key key, ModifierKeys modifiers)
  {
    if (modifiers != ModifierKeys.Control && modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
      return null;

    return key switch
    {
      Key.Add or Key.OemPlus => 10,
      Key.Subtract or Key.OemMinus when modifiers == ModifierKeys.Control => -10,
      Key.D0 or Key.NumPad0 when modifiers == ModifierKeys.Control => 0,
      _ => null,
    };
  }
}
