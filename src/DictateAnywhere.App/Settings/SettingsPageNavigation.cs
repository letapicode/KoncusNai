using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DictateAnywhere.App.Settings;

internal static class SettingsPageNavigation
{
  internal static bool ShouldScroll(Key key, ModifierKeys modifiers, DependencyObject? source, bool capturing)
  {
    if (capturing || key is not (Key.Home or Key.End)
        || (modifiers & ~ModifierKeys.Control) != 0) return false;
    for (DependencyObject? current = source; current is not null; current = Parent(current))
      if (current is TextBoxBase or PasswordBox or Selector or MenuBase or RangeBase) return false;
    return true;
  }

  private static DependencyObject? Parent(DependencyObject element) =>
    element is Visual or System.Windows.Media.Media3D.Visual3D
      ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
