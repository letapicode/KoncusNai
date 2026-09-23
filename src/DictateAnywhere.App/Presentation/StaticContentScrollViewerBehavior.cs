using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DictateAnywhere.App.Presentation;

/// <summary>Provides vertical Home/End navigation for a directly focused static-content viewport.</summary>
internal static class StaticContentScrollViewerBehavior
{
  public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
    "IsEnabled",
    typeof(bool),
    typeof(StaticContentScrollViewerBehavior),
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

  internal static bool HandleKey(ScrollViewer scrollViewer, Key key, ModifierKeys modifiers)
  {
    ArgumentNullException.ThrowIfNull(scrollViewer);
    if (modifiers != ModifierKeys.None)
    {
      return false;
    }

    switch (key)
    {
      case Key.Home:
        scrollViewer.ScrollToHome();
        return true;
      case Key.End:
        scrollViewer.ScrollToEnd();
        return true;
      default:
        return false;
    }
  }

  private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
  {
    if (dependencyObject is not ScrollViewer scrollViewer)
    {
      throw new InvalidOperationException("StaticContentScrollViewerBehavior can only be attached to a ScrollViewer.");
    }

    scrollViewer.PreviewKeyDown -= OnPreviewKeyDown;
    if (args.NewValue is true)
    {
      scrollViewer.PreviewKeyDown += OnPreviewKeyDown;
    }
  }

  private static void OnPreviewKeyDown(object sender, KeyEventArgs args)
  {
    if (sender is not ScrollViewer scrollViewer
      || args.Handled
      || !ReferenceEquals(args.OriginalSource, scrollViewer))
    {
      return;
    }

    args.Handled = HandleKey(scrollViewer, args.Key, args.KeyboardDevice.Modifiers);
  }
}
