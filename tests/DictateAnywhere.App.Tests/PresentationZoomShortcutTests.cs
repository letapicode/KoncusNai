using System.Windows.Input;
using DictateAnywhere.App.Workbench;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class PresentationZoomShortcutTests
{
  [Theory]
  [InlineData(Key.OemPlus, ModifierKeys.Control, 10)]
  [InlineData(Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift, 10)]
  [InlineData(Key.Add, ModifierKeys.Control, 10)]
  [InlineData(Key.OemMinus, ModifierKeys.Control, -10)]
  [InlineData(Key.Subtract, ModifierKeys.Control, -10)]
  [InlineData(Key.D0, ModifierKeys.Control, 0)]
  [InlineData(Key.NumPad0, ModifierKeys.Control, 0)]
  [InlineData(Key.OemPlus, ModifierKeys.None, null)]
  [InlineData(Key.A, ModifierKeys.Control, null)]
  public void Delta_MapsWorkbenchZoomKeys(Key key, ModifierKeys modifiers, int? expected) =>
    Assert.Equal(expected, WorkbenchZoomShortcut.Delta(key, modifiers));
}
