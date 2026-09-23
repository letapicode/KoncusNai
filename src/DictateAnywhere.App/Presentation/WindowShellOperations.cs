using System;
using System.Windows;

namespace DictateAnywhere.App.Presentation;

internal static class WindowShellOperations
{
  public static void Minimize(Window window)
  {
    ArgumentNullException.ThrowIfNull(window);
    window.WindowState = WindowState.Minimized;
  }

  public static void ToggleMaximize(Window window)
  {
    ArgumentNullException.ThrowIfNull(window);
    window.WindowState = window.WindowState == WindowState.Maximized
      ? WindowState.Normal
      : WindowState.Maximized;
  }

  public static void Close(Window window)
  {
    ArgumentNullException.ThrowIfNull(window);
    window.Close();
  }
}
