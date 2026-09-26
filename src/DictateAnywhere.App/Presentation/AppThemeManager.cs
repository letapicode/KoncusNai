using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DictateAnywhere.Core.Contracts;
using Microsoft.Win32;

namespace DictateAnywhere.App.Presentation;

internal static class AppThemeManager
{
  internal const int DwmAttributeUseImmersiveDarkMode = 20;
  internal const int DwmAttributeUseImmersiveDarkModeBefore20H1 = 19;
  internal const int DwmAttributeBorderColor = 34;
  internal const int DwmColorDefault = unchecked((int)0xFFFFFFFF);

  private const string PersonalizeRegistryPath =
    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

  private static readonly IReadOnlyDictionary<string, Color> LightPalette =
    new Dictionary<string, Color>(StringComparer.Ordinal)
    {
      ["Brush.Text.Primary"] = Color.FromRgb(31, 36, 48),
      ["Brush.Text.Secondary"] = Color.FromRgb(96, 99, 108),
      ["Brush.Text.Inverse"] = Color.FromRgb(255, 255, 255),
      ["Brush.Status.Success"] = Color.FromRgb(15, 93, 33),
      ["Brush.Status.Warning"] = Color.FromRgb(94, 62, 0),
      ["Brush.Status.Error"] = Color.FromRgb(138, 47, 0),
      ["Brush.Surface.Subtle"] = Color.FromRgb(235, 232, 226),
      ["Brush.Surface.Canvas"] = Color.FromRgb(242, 240, 235),
      ["Brush.Surface.Sidebar"] = Color.FromRgb(233, 230, 224),
      ["Brush.Surface.Composer"] = Color.FromRgb(248, 247, 243),
      ["Brush.Surface.BlueWash"] = Color.FromRgb(246, 225, 213),
      ["Brush.Surface.Base"] = Color.FromRgb(247, 245, 240),
      ["Brush.Surface.Elevated"] = Color.FromRgb(252, 251, 248),
      ["Brush.Accent.Chat"] = Color.FromRgb(13, 148, 136),
      ["Brush.Accent.Dictation"] = Color.FromRgb(147, 51, 234),
      ["Brush.Code.Background"] = Color.FromRgb(244, 247, 252),
      ["Brush.Code.Border"] = Color.FromRgb(213, 221, 234),
      ["Brush.Code.Text"] = Color.FromRgb(30, 41, 59),
      ["Brush.Code.Comment"] = Color.FromRgb(98, 114, 137),
      ["Brush.Code.String"] = Color.FromRgb(4, 120, 87),
      ["Brush.Code.Keyword"] = Color.FromRgb(161, 31, 117),
      ["Brush.Code.Identifier"] = Color.FromRgb(30, 41, 59),
      ["Brush.Code.Number"] = Color.FromRgb(154, 52, 18),
      ["Brush.Code.Header"] = Color.FromRgb(232, 237, 244),
      ["Brush.Code.Label"] = Color.FromRgb(56, 67, 84),
      ["Brush.Code.Icon"] = Color.FromRgb(84, 96, 114),
      ["Brush.Code.Primitive"] = Color.FromRgb(107, 55, 172),
      ["Brush.Code.Type"] = Color.FromRgb(99, 54, 159),
      ["Brush.Code.Method"] = Color.FromRgb(34, 87, 157),
      ["Brush.Code.Member"] = Color.FromRgb(30, 104, 98),
      ["Brush.Code.Literal"] = Color.FromRgb(154, 52, 18),
      ["Brush.Code.Operator"] = Color.FromRgb(71, 81, 96),
      ["Brush.Code.Bracket"] = Color.FromRgb(71, 81, 96),
      ["Brush.Code.Punctuation"] = Color.FromRgb(86, 98, 115),
      ["Brush.Chat.User"] = Color.FromRgb(218, 231, 247),
      ["Brush.Chat.UserText"] = Color.FromRgb(29, 35, 49),
      ["Brush.Paper.Ink"] = Color.FromRgb(29, 35, 49),
      ["Brush.Paper.User"] = Color.FromRgb(225, 230, 237),
      ["Brush.Paper.UserText"] = Color.FromRgb(29, 35, 49),
      ["Brush.Paper.Composer"] = Color.FromRgb(240, 241, 243),
      ["Brush.Paper.Keyword"] = Color.FromRgb(161, 31, 117),
      ["Brush.Paper.Identifier"] = Color.FromRgb(29, 35, 49),
      ["Brush.Paper.Number"] = Color.FromRgb(145, 66, 11),
      ["Brush.Paper.String"] = Color.FromRgb(0, 103, 78),
      ["Brush.Paper.Comment"] = Color.FromRgb(82, 94, 111),
      ["Brush.Paper.Page"] = Color.FromRgb(240, 241, 243),
      ["Brush.Paper.Border"] = Color.FromRgb(174, 180, 188),
      ["Brush.Paper.Control"] = Color.FromRgb(229, 232, 236),
      ["Brush.Paper.Hover"] = Color.FromRgb(216, 222, 229),
      ["Brush.Paper.Primitive"] = Color.FromRgb(100, 49, 165),
      ["Brush.Paper.Type"] = Color.FromRgb(89, 48, 151),
      ["Brush.Paper.Method"] = Color.FromRgb(34, 79, 154),
      ["Brush.Paper.Member"] = Color.FromRgb(24, 95, 92),
      ["Brush.Paper.Literal"] = Color.FromRgb(145, 66, 11),
      ["Brush.Paper.Operator"] = Color.FromRgb(58, 67, 79),
      ["Brush.Paper.Bracket"] = Color.FromRgb(58, 67, 79),
      ["Brush.Paper.Punctuation"] = Color.FromRgb(77, 86, 99),
      ["Brush.Border.Subtle"] = Color.FromRgb(210, 205, 196),
      ["Brush.Control.Input"] = Color.FromRgb(249, 248, 244),
      ["Brush.Control.InputDisabled"] = Color.FromRgb(229, 226, 220),
      ["Brush.Control.Hover"] = Color.FromRgb(228, 225, 219),
      ["Brush.Control.Muted"] = Color.FromRgb(224, 220, 213),
      ["Brush.Control.Pressed"] = Color.FromRgb(216, 212, 204),
      ["Brush.Control.Selection"] = Color.FromRgb(239, 200, 178),
      ["Brush.Control.Primary"] = Color.FromRgb(183, 71, 33),
      ["Brush.Control.PrimaryHover"] = Color.FromRgb(163, 57, 24),
      ["Brush.Control.PrimaryPressed"] = Color.FromRgb(140, 46, 19),
      ["Brush.Control.Danger"] = Color.FromRgb(198, 40, 40),
      ["Brush.Control.DangerHover"] = Color.FromRgb(183, 28, 28),
      ["Brush.Control.DangerPressed"] = Color.FromRgb(142, 21, 21),
      ["Brush.Progress.Track"] = Color.FromRgb(210, 205, 196),
      ["Brush.Progress.Value"] = Color.FromRgb(183, 71, 33),
      ["Brush.Scrollbar.Thumb"] = Color.FromRgb(156, 163, 175),
      ["Brush.Scrollbar.ThumbHover"] = Color.FromRgb(107, 114, 128),
    };

  private static readonly IReadOnlyDictionary<string, Color> DarkPalette =
    new Dictionary<string, Color>(StringComparer.Ordinal)
    {
      ["Brush.Text.Primary"] = Color.FromRgb(243, 244, 246),
      ["Brush.Text.Secondary"] = Color.FromRgb(156, 163, 175),
      ["Brush.Text.Inverse"] = Color.FromRgb(255, 255, 255),
      ["Brush.Status.Success"] = Color.FromRgb(74, 222, 128),
      ["Brush.Status.Warning"] = Color.FromRgb(251, 191, 36),
      ["Brush.Status.Error"] = Color.FromRgb(248, 113, 113),
      ["Brush.Surface.Subtle"] = Color.FromRgb(10, 15, 24),
      ["Brush.Surface.Canvas"] = Color.FromRgb(10, 15, 24),
      ["Brush.Surface.Sidebar"] = Color.FromRgb(15, 20, 30),
      ["Brush.Surface.Composer"] = Color.FromRgb(24, 33, 49),
      ["Brush.Surface.BlueWash"] = Color.FromRgb(66, 38, 29),
      ["Brush.Surface.Base"] = Color.FromRgb(17, 24, 39),
      ["Brush.Surface.Elevated"] = Color.FromRgb(24, 33, 49),
      ["Brush.Accent.Chat"] = Color.FromRgb(45, 212, 191),
      ["Brush.Accent.Dictation"] = Color.FromRgb(196, 181, 253),
      ["Brush.Code.Background"] = Color.FromRgb(23, 26, 33),
      ["Brush.Code.Border"] = Color.FromRgb(48, 55, 68),
      ["Brush.Code.Text"] = Color.FromRgb(216, 222, 233),
      ["Brush.Code.Comment"] = Color.FromRgb(129, 139, 155),
      ["Brush.Code.String"] = Color.FromRgb(168, 201, 127),
      ["Brush.Code.Keyword"] = Color.FromRgb(240, 113, 155),
      ["Brush.Code.Identifier"] = Color.FromRgb(216, 222, 233),
      ["Brush.Code.Number"] = Color.FromRgb(242, 166, 90),
      ["Brush.Code.Header"] = Color.FromRgb(36, 42, 52),
      ["Brush.Code.Label"] = Color.FromRgb(201, 209, 220),
      ["Brush.Code.Icon"] = Color.FromRgb(142, 152, 168),
      ["Brush.Code.Primitive"] = Color.FromRgb(182, 157, 248),
      ["Brush.Code.Type"] = Color.FromRgb(196, 167, 255),
      ["Brush.Code.Method"] = Color.FromRgb(130, 170, 255),
      ["Brush.Code.Member"] = Color.FromRgb(138, 190, 183),
      ["Brush.Code.Literal"] = Color.FromRgb(242, 166, 90),
      ["Brush.Code.Operator"] = Color.FromRgb(188, 195, 206),
      ["Brush.Code.Bracket"] = Color.FromRgb(174, 182, 194),
      ["Brush.Code.Punctuation"] = Color.FromRgb(137, 146, 159),
      ["Brush.Chat.User"] = Color.FromRgb(28, 65, 113),
      ["Brush.Chat.UserText"] = Color.FromRgb(244, 247, 251),
      ["Brush.Paper.Ink"] = Color.FromRgb(29, 35, 49),
      ["Brush.Paper.User"] = Color.FromRgb(225, 230, 237),
      ["Brush.Paper.UserText"] = Color.FromRgb(29, 35, 49),
      ["Brush.Paper.Composer"] = Color.FromRgb(240, 241, 243),
      ["Brush.Paper.Keyword"] = Color.FromRgb(161, 31, 117),
      ["Brush.Paper.Identifier"] = Color.FromRgb(29, 35, 49),
      ["Brush.Paper.Number"] = Color.FromRgb(145, 66, 11),
      ["Brush.Paper.String"] = Color.FromRgb(0, 103, 78),
      ["Brush.Paper.Comment"] = Color.FromRgb(82, 94, 111),
      ["Brush.Paper.Page"] = Color.FromRgb(240, 241, 243),
      ["Brush.Paper.Border"] = Color.FromRgb(174, 180, 188),
      ["Brush.Paper.Control"] = Color.FromRgb(229, 232, 236),
      ["Brush.Paper.Hover"] = Color.FromRgb(216, 222, 229),
      ["Brush.Paper.Primitive"] = Color.FromRgb(100, 49, 165),
      ["Brush.Paper.Type"] = Color.FromRgb(89, 48, 151),
      ["Brush.Paper.Method"] = Color.FromRgb(34, 79, 154),
      ["Brush.Paper.Member"] = Color.FromRgb(24, 95, 92),
      ["Brush.Paper.Literal"] = Color.FromRgb(145, 66, 11),
      ["Brush.Paper.Operator"] = Color.FromRgb(58, 67, 79),
      ["Brush.Paper.Bracket"] = Color.FromRgb(58, 67, 79),
      ["Brush.Paper.Punctuation"] = Color.FromRgb(77, 86, 99),
      ["Brush.Border.Subtle"] = Color.FromRgb(55, 65, 81),
      ["Brush.Control.Input"] = Color.FromRgb(15, 23, 42),
      ["Brush.Control.InputDisabled"] = Color.FromRgb(31, 41, 55),
      ["Brush.Control.Hover"] = Color.FromRgb(31, 41, 55),
      ["Brush.Control.Muted"] = Color.FromRgb(36, 44, 58),
      ["Brush.Control.Pressed"] = Color.FromRgb(55, 65, 81),
      ["Brush.Control.Selection"] = Color.FromRgb(118, 52, 30),
      ["Brush.Control.Primary"] = Color.FromRgb(183, 71, 33),
      ["Brush.Control.PrimaryHover"] = Color.FromRgb(194, 79, 37),
      ["Brush.Control.PrimaryPressed"] = Color.FromRgb(163, 57, 24),
      ["Brush.Control.Danger"] = Color.FromRgb(217, 74, 74),
      ["Brush.Control.DangerHover"] = Color.FromRgb(228, 93, 93),
      ["Brush.Control.DangerPressed"] = Color.FromRgb(185, 54, 54),
      ["Brush.Progress.Track"] = Color.FromRgb(55, 65, 81),
      ["Brush.Progress.Value"] = Color.FromRgb(239, 136, 91),
      ["Brush.Scrollbar.Thumb"] = Color.FromRgb(75, 85, 99),
      ["Brush.Scrollbar.ThumbHover"] = Color.FromRgb(107, 114, 128),
    };

  private static AppThemePreference currentPreference = AppThemePreference.Dark;
  private static bool wasHighContrastActive;

  public static AppThemePreference CurrentPreference => currentPreference;

  public static bool IsHighContrastActive()
  {
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return false;
    }

    try
    {
      return SystemParameters.HighContrast;
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
      return false;
    }
  }

  public static void ApplySystemThemeResources()
  {
    ApplyThemeResources(AppThemePreference.Dark);
  }

  public static void ApplyThemeResources(AppThemePreference preference)
  {
    ApplyThemeResources(preference, IsHighContrastActive());
  }

  public static void ApplyThemeResources(AppThemePreference preference, bool isHighContrast)
  {
    ResourceDictionary? resources = Application.Current?.Resources;
    if (resources is null)
    {
      return;
    }

    currentPreference = NormalizePreference(preference);
    wasHighContrastActive = isHighContrast;
    ApplyPalette(resources, ResolveUseDarkTheme(currentPreference), isHighContrast);
    ApplyNativeThemeToOpenWindows();
  }

  public static IDisposable SubscribeToSystemThemeChanges(Dispatcher dispatcher)
  {
    ArgumentNullException.ThrowIfNull(dispatcher);

    UserPreferenceChangedEventHandler handler = (_, e) =>
    {
      if (e.Category is not UserPreferenceCategory.General
          and not UserPreferenceCategory.VisualStyle
          and not UserPreferenceCategory.Accessibility
          and not UserPreferenceCategory.Color)
      {
        return;
      }

      bool isHighContrast = IsHighContrastActive();
      if (isHighContrast || wasHighContrastActive)
      {
        _ = dispatcher.BeginInvoke(new Action(() =>
        {
          ApplyThemeResources(currentPreference, isHighContrast);
        }));
      }
    };

    SystemEvents.UserPreferenceChanged += handler;
    return new ThemeSubscription(() => SystemEvents.UserPreferenceChanged -= handler);
  }

  internal static void ApplyPalette(ResourceDictionary resources, bool useDarkTheme, bool isHighContrast = false)
  {
    ArgumentNullException.ThrowIfNull(resources);

    IReadOnlyDictionary<string, Color> palette = isHighContrast
      ? GetHighContrastPalette()
      : (useDarkTheme ? DarkPalette : LightPalette);

    foreach ((string key, Color color) in palette)
    {
      resources[key] = new SolidColorBrush(color);
    }
  }

  internal static IReadOnlyDictionary<string, Color> GetHighContrastPalette()
  {
    return new Dictionary<string, Color>(StringComparer.Ordinal)
    {
      ["Brush.Text.Primary"] = SystemColors.WindowTextColor,
      ["Brush.Text.Secondary"] = SystemColors.WindowTextColor,
      ["Brush.Text.Inverse"] = SystemColors.HighlightTextColor,
      ["Brush.Status.Success"] = SystemColors.HighlightColor,
      ["Brush.Status.Warning"] = SystemColors.HighlightColor,
      ["Brush.Status.Error"] = SystemColors.HighlightColor,
      ["Brush.Surface.Subtle"] = SystemColors.WindowColor,
      ["Brush.Surface.Canvas"] = SystemColors.WindowColor,
      ["Brush.Surface.Sidebar"] = SystemColors.WindowColor,
      ["Brush.Surface.Composer"] = SystemColors.WindowColor,
      ["Brush.Surface.BlueWash"] = SystemColors.WindowColor,
      ["Brush.Surface.Base"] = SystemColors.WindowColor,
      ["Brush.Surface.Elevated"] = SystemColors.WindowColor,
      ["Brush.Accent.Chat"] = SystemColors.HighlightColor,
      ["Brush.Accent.Dictation"] = SystemColors.HighlightColor,
      ["Brush.Code.Background"] = SystemColors.WindowColor,
      ["Brush.Code.Border"] = SystemColors.WindowTextColor,
      ["Brush.Code.Text"] = SystemColors.WindowTextColor,
      ["Brush.Code.Comment"] = SystemColors.WindowTextColor,
      ["Brush.Code.String"] = SystemColors.HighlightColor,
      ["Brush.Code.Keyword"] = SystemColors.HighlightColor,
      ["Brush.Code.Identifier"] = SystemColors.WindowTextColor,
      ["Brush.Code.Number"] = SystemColors.WindowTextColor,
      ["Brush.Code.Header"] = SystemColors.WindowColor,
      ["Brush.Code.Label"] = SystemColors.WindowTextColor,
      ["Brush.Code.Icon"] = SystemColors.WindowTextColor,
      ["Brush.Code.Primitive"] = SystemColors.WindowTextColor,
      ["Brush.Code.Type"] = SystemColors.WindowTextColor,
      ["Brush.Code.Method"] = SystemColors.WindowTextColor,
      ["Brush.Code.Member"] = SystemColors.WindowTextColor,
      ["Brush.Code.Literal"] = SystemColors.WindowTextColor,
      ["Brush.Code.Operator"] = SystemColors.WindowTextColor,
      ["Brush.Code.Bracket"] = SystemColors.WindowTextColor,
      ["Brush.Code.Punctuation"] = SystemColors.WindowTextColor,
      ["Brush.Chat.User"] = SystemColors.HighlightColor,
      ["Brush.Chat.UserText"] = SystemColors.HighlightTextColor,
      ["Brush.Paper.Ink"] = SystemColors.WindowTextColor,
      ["Brush.Paper.User"] = SystemColors.HighlightColor,
      ["Brush.Paper.UserText"] = SystemColors.HighlightTextColor,
      ["Brush.Paper.Composer"] = SystemColors.WindowColor,
      ["Brush.Paper.Keyword"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Identifier"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Number"] = SystemColors.WindowTextColor,
      ["Brush.Paper.String"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Comment"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Page"] = SystemColors.WindowColor,
      ["Brush.Paper.Border"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Control"] = SystemColors.WindowColor,
      ["Brush.Paper.Hover"] = SystemColors.HighlightColor,
      ["Brush.Paper.Primitive"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Type"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Method"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Member"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Literal"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Operator"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Bracket"] = SystemColors.WindowTextColor,
      ["Brush.Paper.Punctuation"] = SystemColors.WindowTextColor,
      ["Brush.Border.Subtle"] = SystemColors.WindowTextColor,
      ["Brush.Control.Input"] = SystemColors.WindowColor,
      ["Brush.Control.InputDisabled"] = SystemColors.WindowColor,
      ["Brush.Control.Hover"] = SystemColors.HighlightColor,
      ["Brush.Control.Muted"] = SystemColors.HighlightColor,
      ["Brush.Control.Pressed"] = SystemColors.HighlightColor,
      ["Brush.Control.Selection"] = SystemColors.HighlightColor,
      ["Brush.Control.Primary"] = SystemColors.HighlightColor,
      ["Brush.Control.PrimaryHover"] = SystemColors.HighlightColor,
      ["Brush.Control.PrimaryPressed"] = SystemColors.HighlightColor,
      ["Brush.Control.Danger"] = SystemColors.HighlightColor,
      ["Brush.Control.DangerHover"] = SystemColors.HighlightColor,
      ["Brush.Control.DangerPressed"] = SystemColors.HighlightColor,
      ["Brush.Progress.Track"] = SystemColors.WindowColor,
      ["Brush.Progress.Value"] = SystemColors.HighlightColor,
      ["Brush.Scrollbar.Thumb"] = SystemColors.WindowTextColor,
      ["Brush.Scrollbar.ThumbHover"] = SystemColors.HighlightColor,
    };
  }

  public static void ApplyNativeWindowTheme(Window window)
  {
    ArgumentNullException.ThrowIfNull(window);

    WindowThemeBehavior.SetIsHighContrastActive(window, wasHighContrastActive);

    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
      return;
    }

    IntPtr handle = new WindowInteropHelper(window).Handle;
    if (handle == IntPtr.Zero)
    {
      return;
    }

    ApplyNativeWindowThemeAttributes(
      currentPreference,
      wasHighContrastActive,
      window.WindowStyle == WindowStyle.None,
      SystemColors.WindowTextColor,
      SystemColors.WindowColor,
      (attribute, value) =>
      {
        int attributeValue = value;
        _ = DwmSetWindowAttribute(handle, attribute, ref attributeValue, Marshal.SizeOf<int>());
      });
  }

  internal static void ApplyNativeWindowThemeAttributes(
    AppThemePreference preference,
    bool isHighContrast,
    bool usesCustomChrome,
    Color highContrastBoundaryColor,
    Color adjacentSurfaceColor,
    Action<int, int> applyAttribute)
  {
    ArgumentNullException.ThrowIfNull(applyAttribute);

    int darkModeValue = ResolveUseDarkTheme(preference) ? 1 : 0;
    applyAttribute(DwmAttributeUseImmersiveDarkMode, darkModeValue);
    applyAttribute(DwmAttributeUseImmersiveDarkModeBefore20H1, darkModeValue);
    if (usesCustomChrome)
    {
      applyAttribute(
        DwmAttributeBorderColor,
        isHighContrast
          ? ResolveHighContrastBorderColor(highContrastBoundaryColor, adjacentSurfaceColor)
          : DwmColorDefault);
    }
  }

  internal static int ResolveHighContrastBorderColor(Color boundaryColor, Color adjacentSurfaceColor)
  {
    if (boundaryColor.A == 0)
    {
      throw new InvalidOperationException("The High Contrast window boundary color cannot be transparent.");
    }
    if (boundaryColor.R == adjacentSurfaceColor.R
        && boundaryColor.G == adjacentSurfaceColor.G
        && boundaryColor.B == adjacentSurfaceColor.B)
    {
      throw new InvalidOperationException("The High Contrast window boundary must differ from the window surface.");
    }

    return ToColorRef(boundaryColor);
  }

  internal static int ToColorRef(Color color) =>
    color.R | (color.G << 8) | (color.B << 16);

  public static bool ResolveUseDarkTheme(AppThemePreference preference)
  {
    return NormalizePreference(preference) switch
    {
      AppThemePreference.Dark => true,
      AppThemePreference.Light => false,
      _ => true,
    };
  }

  private static void ApplyNativeThemeToOpenWindows()
  {
    Application? app = Application.Current;
    if (app is null || !app.Dispatcher.CheckAccess())
    {
      return;
    }

    foreach (Window window in app.Windows)
    {
      ApplyNativeWindowTheme(window);
    }
  }

  private static AppThemePreference NormalizePreference(AppThemePreference preference)
  {
    return Enum.IsDefined(typeof(AppThemePreference), preference)
      ? preference
      : AppThemePreference.Dark;
  }

  private static bool IsSystemDarkTheme()
  {
    try
    {
      object? value = Registry.GetValue(PersonalizeRegistryPath, "AppsUseLightTheme", 1);
      return value is int intValue && intValue == 0;
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
    {
      return false;
    }
  }

  [DllImport("dwmapi.dll")]
  private static extern int DwmSetWindowAttribute(
    IntPtr hwnd,
    int dwAttribute,
    ref int pvAttribute,
    int cbAttribute);

  private sealed class ThemeSubscription : IDisposable
  {
    private readonly Action dispose;
    private bool disposed;

    public ThemeSubscription(Action dispose)
    {
      this.dispose = dispose;
    }

    public void Dispose()
    {
      if (disposed)
      {
        return;
      }

      disposed = true;
      dispose();
    }
  }
}
