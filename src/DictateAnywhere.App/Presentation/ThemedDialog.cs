using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DictateAnywhere.App.Presentation;

internal static class ThemedDialog
{
  public static bool Confirm(
    Window owner,
    string title,
    string message,
    string confirmText,
    string cancelText = "Cancel",
    bool destructive = false)
  {
    ArgumentNullException.ThrowIfNull(owner);

    Window dialog = new()
    {
      Title = title,
      Owner = owner,
      Width = 440,
      SizeToContent = SizeToContent.Height,
      MinWidth = 380,
      MaxWidth = 560,
      ResizeMode = ResizeMode.NoResize,
      WindowStyle = WindowStyle.None,
      AllowsTransparency = true,
      WindowStartupLocation = WindowStartupLocation.CenterOwner,
      Background = Brushes.Transparent,
      Foreground = ResolveBrush(owner, "Brush.Text.Primary"),
    };

    Grid root = new()
    {
      Margin = new Thickness(18),
    };
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

    TextBlock heading = new()
    {
      Text = title,
      FontSize = 17,
      FontWeight = FontWeights.SemiBold,
      Margin = new Thickness(0, 0, 0, 8),
      Foreground = ResolveBrush(owner, "Brush.Text.Primary"),
    };
    Grid.SetRow(heading, 0);
    root.Children.Add(heading);

    TextBlock body = new()
    {
      Text = message,
      TextWrapping = TextWrapping.Wrap,
      Foreground = ResolveBrush(owner, "Brush.Text.Secondary"),
      MaxWidth = 500,
    };
    Grid.SetRow(body, 1);
    root.Children.Add(body);

    StackPanel buttons = new()
    {
      Orientation = Orientation.Horizontal,
      HorizontalAlignment = HorizontalAlignment.Right,
      Margin = new Thickness(0, 18, 0, 0),
    };

    Button cancelButton = new()
    {
      Content = cancelText,
      IsCancel = true,
      MinWidth = 88,
      Margin = new Thickness(0, 0, 8, 0),
      Style = ResolveStyle(owner, "AppActionButtonStyle"),
    };

    Button confirmButton = new()
    {
      Content = confirmText,
      IsDefault = true,
      MinWidth = 88,
      Style = ResolveStyle(owner, destructive ? "AppDangerActionButtonStyle" : "AppPrimaryActionButtonStyle"),
    };
    confirmButton.Click += (_, _) =>
    {
      dialog.DialogResult = true;
      dialog.Close();
    };

    buttons.Children.Add(cancelButton);
    buttons.Children.Add(confirmButton);
    Grid.SetRow(buttons, 2);
    root.Children.Add(buttons);

    dialog.Content = new Border
    {
      Background = ResolveBrush(owner, "Brush.Surface.Subtle"),
      BorderBrush = ResolveBrush(owner, "Brush.Border.Subtle"),
      BorderThickness = new Thickness(1),
      CornerRadius = new CornerRadius(14),
      Child = root,
    };
    WindowThemeBehavior.SetIsEnabled(dialog, true);
    return dialog.ShowDialog() == true;
  }

  private static Brush ResolveBrush(FrameworkElement owner, string resourceKey)
  {
    return owner.TryFindResource(resourceKey) as Brush
      ?? Application.Current?.TryFindResource(resourceKey) as Brush
      ?? Brushes.Transparent;
  }

  private static Style? ResolveStyle(FrameworkElement owner, string resourceKey)
  {
    return owner.TryFindResource(resourceKey) as Style
      ?? Application.Current?.TryFindResource(resourceKey) as Style;
  }
}
