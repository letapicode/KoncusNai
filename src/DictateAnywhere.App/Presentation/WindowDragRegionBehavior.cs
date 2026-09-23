using System;
using System.Windows;
using System.Windows.Input;

namespace DictateAnywhere.App.Presentation;

/// <summary>Provides standard drag and double-click maximize behavior for custom title regions.</summary>
internal static class WindowDragRegionBehavior
{
  public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
    "IsEnabled",
    typeof(bool),
    typeof(WindowDragRegionBehavior),
    new PropertyMetadata(false, OnIsEnabledChanged));

  public static bool GetIsEnabled(DependencyObject element)
  {
    ArgumentNullException.ThrowIfNull(element);
    return (bool)element.GetValue(IsEnabledProperty);
  }

  public static void SetIsEnabled(DependencyObject element, bool value)
  {
    ArgumentNullException.ThrowIfNull(element);
    element.SetValue(IsEnabledProperty, value);
  }

  private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
  {
    if (dependencyObject is not UIElement element)
    {
      throw new InvalidOperationException("WindowDragRegionBehavior can only be attached to a UIElement.");
    }

    element.MouseLeftButtonDown -= OnMouseLeftButtonDown;
    if (args.NewValue is true)
    {
      element.MouseLeftButtonDown += OnMouseLeftButtonDown;
    }
  }

  private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
  {
    if (sender is not DependencyObject element || args.ChangedButton != MouseButton.Left)
    {
      return;
    }

    Window? window = Window.GetWindow(element);
    if (window is null)
    {
      return;
    }

    if (args.ClickCount == 2)
    {
      WindowShellOperations.ToggleMaximize(window);
      args.Handled = true;
      return;
    }

    window.DragMove();
  }
}
