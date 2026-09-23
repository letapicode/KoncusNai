using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DictateAnywhere.App.Presentation;

/// <summary>
/// Owns the relationship between Windows accessibility text metrics and Koncus Nai's
/// semantic application-chrome typography. User-selected chat and Reader document
/// typography deliberately remain outside this owner.
/// </summary>
internal static class AppTextScaleManager
{
  internal const double NormalMessageFontSize = 12d;
  internal const double MinimumScale = 1d;
  internal const double MaximumScale = 2.25d;

  private static readonly IReadOnlyDictionary<string, double> BaseFontSizes =
    new Dictionary<string, double>(StringComparer.Ordinal)
    {
      ["Font.Size.Caption"] = 12d,
      ["Font.Size.FieldLabel"] = 13d,
      ["Font.Size.Body"] = 14d,
      ["Font.Size.BodyComfortable"] = 14d,
      ["Font.Size.Control"] = 14d,
      ["Font.Size.Lead"] = 15d,
      ["Font.Size.Section"] = 16d,
      ["Font.Size.Subheading"] = 20d,
      ["Font.Size.Heading"] = 24d,
      ["Font.Size.Display"] = 28d,
      ["Font.Size.Hero"] = 34d,
      ["Line.Height.Caption"] = 17d,
      ["Line.Height.Body"] = 20d,
      ["Line.Height.Lead"] = 23d,
    };

  internal static double CalculateScale(double messageFontSize)
  {
    if (!double.IsFinite(messageFontSize) || messageFontSize <= 0d)
    {
      return MinimumScale;
    }

    return Math.Clamp(messageFontSize / NormalMessageFontSize, MinimumScale, MaximumScale);
  }

  internal static void Apply(ResourceDictionary resources, double messageFontSize)
  {
    ArgumentNullException.ThrowIfNull(resources);

    double scale = CalculateScale(messageFontSize);
    SetResource(resources, "Font.Scale.WindowsText", scale);
    foreach ((string key, double baseSize) in BaseFontSizes)
    {
      SetResource(resources, key, baseSize * scale);
    }

    // Width growth is deliberately capped. Text wraps or scrolls after the cap,
    // keeping the document/composer usable at each window's established minimum.
    SetResource(resources, "Layout.Workbench.SidebarWidth", new GridLength(292d + (68d * Math.Min(scale - 1d, 1d))));
    SetResource(resources, "Layout.QuickSettings.Width", 272d + (88d * Math.Min(scale - 1d, 1d)));
    SetResource(resources, "Layout.Reader.SidebarWidth", new GridLength(320d + (80d * Math.Min(scale - 1d, 1d))));
    double settingsLabelWidth = 150d + (70d * Math.Min(scale - 1d, 1d));
    SetResource(resources, "Layout.Settings.LabelWidth", new GridLength(settingsLabelWidth));
    SetResource(resources, "Layout.Settings.ContentIndent", new Thickness(settingsLabelWidth, 0d, 0d, 0d));
    SetResource(resources, "Layout.Settings.ContentIndent4", new Thickness(settingsLabelWidth, 4d, 0d, 0d));
    SetResource(resources, "Layout.Settings.ContentIndent7", new Thickness(settingsLabelWidth, 7d, 0d, 0d));
    SetResource(resources, "Layout.Settings.ContentIndent12", new Thickness(settingsLabelWidth, 12d, 0d, 0d));
    SetResource(resources, "Layout.Settings.ContentIndent14", new Thickness(settingsLabelWidth, 14d, 0d, 0d));
    SetResource(resources, "Layout.Settings.ValueWidth", new GridLength(360d + (60d * Math.Min(scale - 1d, 1d))));
    SetResource(resources, "Layout.Responsive.Columns", scale >= 1.5d ? 1 : 2);
  }

  private static void SetResource(ResourceDictionary resources, string key, object value)
  {
    ResourceDictionary owner = FindOwner(resources, key) ?? resources;
    owner[key] = value;
  }

  private static ResourceDictionary? FindOwner(ResourceDictionary resources, string key)
  {
    for (int index = resources.MergedDictionaries.Count - 1; index >= 0; index--)
    {
      ResourceDictionary? owner = FindOwner(resources.MergedDictionaries[index], key);
      if (owner is not null)
      {
        return owner;
      }
    }

    if (resources.Keys.Cast<object>().Any(candidate => Equals(candidate, key)))
    {
      return resources;
    }

    return null;
  }

  internal static void ApplyCurrent(ResourceDictionary resources) =>
    Apply(resources, ReadMessageFontSize());

  internal static double ReadMessageFontSize()
  {
    try
    {
      return SystemFonts.MessageFontSize;
    }
    catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
    {
      return NormalMessageFontSize;
    }
  }

  public static IDisposable Subscribe(Dispatcher dispatcher, ResourceDictionary resources)
  {
    ArgumentNullException.ThrowIfNull(dispatcher);
    ArgumentNullException.ThrowIfNull(resources);

    object gate = new();
    DispatcherOperation? pending = null;
    bool disposed = false;
    UserPreferenceChangedEventHandler handler = (_, e) =>
    {
      if (e.Category is not UserPreferenceCategory.Accessibility
          and not UserPreferenceCategory.General
          and not UserPreferenceCategory.VisualStyle)
      {
        return;
      }

      lock (gate)
      {
        if (disposed || pending is { Status: DispatcherOperationStatus.Pending })
        {
          return;
        }

        pending = dispatcher.BeginInvoke(
          DispatcherPriority.DataBind,
          new Action(() =>
          {
            lock (gate)
            {
              if (disposed)
              {
                pending = null;
                return;
              }

              pending = null;
            }

            ApplyCurrent(resources);
          }));
      }
    };

    SystemEvents.UserPreferenceChanged += handler;
    return new Subscription(() =>
    {
      SystemEvents.UserPreferenceChanged -= handler;
      lock (gate)
      {
        disposed = true;
        if (pending is { Status: DispatcherOperationStatus.Pending })
        {
          pending.Abort();
        }

        pending = null;
      }
    });
  }

  private sealed class Subscription(Action dispose) : IDisposable
  {
    private Action? disposeAction = dispose;

    public void Dispose() => System.Threading.Interlocked.Exchange(ref disposeAction, null)?.Invoke();
  }
}
