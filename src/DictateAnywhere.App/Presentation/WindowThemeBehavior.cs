using System;
using System.Windows;

namespace DictateAnywhere.App.Presentation;

/// <summary>Applies Koncus Nai's native Windows theme when a first-party window obtains its handle.</summary>
internal static class WindowThemeBehavior
{
  public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
    "IsEnabled",
    typeof(bool),
    typeof(WindowThemeBehavior),
    new PropertyMetadata(false, OnIsEnabledChanged));

  public static readonly DependencyProperty IsHighContrastActiveProperty = DependencyProperty.RegisterAttached(
    "IsHighContrastActive",
    typeof(bool),
    typeof(WindowThemeBehavior),
    new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

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

  public static bool GetIsHighContrastActive(DependencyObject element)
  {
    ArgumentNullException.ThrowIfNull(element);
    return (bool)element.GetValue(IsHighContrastActiveProperty);
  }

  internal static void SetIsHighContrastActive(DependencyObject element, bool value)
  {
    ArgumentNullException.ThrowIfNull(element);
    element.SetValue(IsHighContrastActiveProperty, value);
  }

  private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
  {
    if (dependencyObject is not Window window)
    {
      throw new InvalidOperationException("WindowThemeBehavior can only be attached to a Window.");
    }

    window.SourceInitialized -= OnWindowSourceInitialized;
    window.Closed -= OnWindowClosed;

    if (args.NewValue is true)
    {
      window.Icon ??= AppBrand.Icon;
      window.SourceInitialized += OnWindowSourceInitialized;
      window.Closed += OnWindowClosed;
    }
  }

  private static void OnWindowSourceInitialized(object? sender, EventArgs args)
  {
    if (sender is Window window)
    {
      AppThemeManager.ApplyNativeWindowTheme(window);
    }
  }

  private static void OnWindowClosed(object? sender, EventArgs args)
  {
    if (sender is not Window window)
    {
      return;
    }

    window.SourceInitialized -= OnWindowSourceInitialized;
    window.Closed -= OnWindowClosed;
  }
}
