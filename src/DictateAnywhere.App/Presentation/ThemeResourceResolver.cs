using System;
using System.Windows;
using System.Windows.Media;

namespace DictateAnywhere.App.Presentation;

public static class ThemeResourceResolver
{
  public static Brush ResolveStatusBrush(FrameworkElement element, UiStatusKind statusKind)
  {
    ArgumentNullException.ThrowIfNull(element);

    string resourceKey = statusKind switch
    {
      UiStatusKind.Success => ThemeResourceKeys.SuccessStatusBrush,
      UiStatusKind.Pending => ThemeResourceKeys.PendingStatusBrush,
      UiStatusKind.Error => ThemeResourceKeys.ErrorStatusBrush,
      _ => ThemeResourceKeys.NeutralTextBrush,
    };

    object? resolved = element.TryFindResource(resourceKey);
    if (resolved is Brush brush)
    {
      return brush;
    }

    return statusKind switch
    {
      UiStatusKind.Success => new SolidColorBrush(Color.FromRgb(15, 93, 33)),
      UiStatusKind.Pending => new SolidColorBrush(Color.FromRgb(138, 94, 0)),
      UiStatusKind.Error => new SolidColorBrush(Color.FromRgb(138, 47, 0)),
      _ => new SolidColorBrush(Color.FromRgb(60, 60, 60)),
    };
  }
}
