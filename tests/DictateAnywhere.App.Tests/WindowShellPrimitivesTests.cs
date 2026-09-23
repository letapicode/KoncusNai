using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DictateAnywhere.App.Presentation;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(WpfApplicationCollection.Name)]
[Xunit.Trait("Category", "WindowsWpf")]
public sealed class WindowShellPrimitivesTests
{
  [Xunit.Fact]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The STA test reports WPF failures to the asserting thread.")]
  public void CaptionButtons_OwnWindowCommandsStateAndAccessibleNames()
  {
    Exception? failure = null;
    bool themeBehaviorEnabled = false;
    bool dragBehaviorEnabled = false;
    bool workAreaBehaviorEnabled = false;
    WindowState stateAfterMinimize = WindowState.Normal;
    string? minimizeName = null;
    string? maximizeName = null;
    string? restoreName = null;
    string? maximizeGlyph = null;
    string? restoreGlyph = null;

    Window? window = null;
    Thread thread = new(() =>
    {
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }

        window = new()
        {
          Width = 1,
          Height = 1,
          Left = -10_000,
          Top = -10_000,
          ShowActivated = false,
          ShowInTaskbar = false,
          WindowStyle = WindowStyle.None,
        };
        WindowCaptionButtons controls = new() { ContextTitle = "Test window" };
        window.Content = controls;
        WindowThemeBehavior.SetIsEnabled(window, true);
        themeBehaviorEnabled = WindowThemeBehavior.GetIsEnabled(window);
        WindowWorkAreaConstraintBehavior.SetIsEnabled(window, true);
        workAreaBehaviorEnabled = WindowWorkAreaConstraintBehavior.GetIsEnabled(window);
        Grid dragRegion = new();
        WindowDragRegionBehavior.SetIsEnabled(dragRegion, true);
        dragBehaviorEnabled = WindowDragRegionBehavior.GetIsEnabled(dragRegion);

        window.Show();
        Button minimizeButton = (Button)controls.FindName("MinimizeButton");
        Button maximizeButton = (Button)controls.FindName("MaximizeRestoreButton");
        TextBlock glyph = (TextBlock)controls.FindName("MaximizeRestoreGlyphTextBlock");

        minimizeName = AutomationProperties.GetName(minimizeButton);
        maximizeName = AutomationProperties.GetName(maximizeButton);
        maximizeGlyph = glyph.Text;

        window.WindowState = WindowState.Maximized;
        restoreName = AutomationProperties.GetName(maximizeButton);
        restoreGlyph = glyph.Text;

        minimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        stateAfterMinimize = window.WindowState;
      }
      catch (Exception ex)
      {
        failure = ex;
      }
      finally
      {
        window?.Close();
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(10));
    Xunit.Assert.True(completed, "STA test thread timed out.");

    Xunit.Assert.Null(failure);
    Xunit.Assert.True(themeBehaviorEnabled);
    Xunit.Assert.True(dragBehaviorEnabled);
    Xunit.Assert.True(workAreaBehaviorEnabled);
    Xunit.Assert.Equal("Minimize Test window", minimizeName);
    Xunit.Assert.Equal("Maximize Test window", maximizeName);
    Xunit.Assert.Equal("Restore Test window", restoreName);
    Xunit.Assert.Equal("\uE922", maximizeGlyph);
    Xunit.Assert.Equal("\uE923", restoreGlyph);
    Xunit.Assert.Equal(WindowState.Minimized, stateAfterMinimize);
  }

  [Xunit.Fact]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The STA test reports WPF failures to the asserting thread.")]
  public void CaptionButtons_ApplyKeyboardFocusIndicatorAndRestoreNormalRendering()
  {
    Exception? failure = null;

    Thread thread = new(() =>
    {
      Window? window = null;
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }

        window = new()
        {
          Width = 300,
          Height = 150,
          Left = -10_000,
          Top = -10_000,
          ShowActivated = true,
          ShowInTaskbar = false,
          WindowStyle = WindowStyle.None,
        };
        StackPanel root = new();
        WindowCaptionButtons controls = new() { ContextTitle = "Test window" };
        Button blurTarget = new() { Content = "BlurTarget", Width = 50, Height = 20 };
        root.Children.Add(controls);
        root.Children.Add(blurTarget);
        window.Content = root;
        window.Show();
        window.Activate();
        window.UpdateLayout();

        Button minimizeButton = (Button)controls.FindName("MinimizeButton");
        Button maximizeRestoreButton = (Button)controls.FindName("MaximizeRestoreButton");
        Button closeButton = (Button)controls.FindName("CloseButton");

        Button[] buttons = [minimizeButton, maximizeRestoreButton, closeButton];
        Brush primaryBrush = (Brush)window.FindResource("Brush.Control.Primary");

        blurTarget.Focus();
        window.UpdateLayout();

        foreach (Button button in buttons)
        {
          button.ApplyTemplate();
          Border chrome = (Border)button.Template.FindName("Chrome", button);

          // Initial unfocused state
          Xunit.Assert.False(button.IsKeyboardFocused);
          Xunit.Assert.Equal(Brushes.Transparent, chrome.BorderBrush);
          Xunit.Assert.Equal(new Thickness(1), chrome.BorderThickness);
          Xunit.Assert.Equal(44, button.ActualWidth);
          Xunit.Assert.Equal(34, button.ActualHeight);

          // Focus state applies primary focus border
          button.Focus();
          window.UpdateLayout();
          Xunit.Assert.True(button.IsKeyboardFocused);
          Xunit.Assert.Equal(primaryBrush, chrome.BorderBrush);
          Xunit.Assert.Equal(new Thickness(1), chrome.BorderThickness);
          Xunit.Assert.Equal(44, button.ActualWidth);
          Xunit.Assert.Equal(34, button.ActualHeight);

          // Focus removal restores normal transparent rendering
          blurTarget.Focus();
          window.UpdateLayout();
          Xunit.Assert.False(button.IsKeyboardFocused);
          Xunit.Assert.Equal(Brushes.Transparent, chrome.BorderBrush);
          Xunit.Assert.Equal(new Thickness(1), chrome.BorderThickness);
        }

        // Verify sequential tab navigation across caption controls
        minimizeButton.Focus();
        window.UpdateLayout();
        Xunit.Assert.True(minimizeButton.IsKeyboardFocused);

        minimizeButton.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        window.UpdateLayout();
        Xunit.Assert.True(maximizeRestoreButton.IsKeyboardFocused);

        maximizeRestoreButton.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        window.UpdateLayout();
        Xunit.Assert.True(closeButton.IsKeyboardFocused);

        // Verify maximize/restore toggle and glyph change preserves focus indicator
        maximizeRestoreButton.Focus();
        window.UpdateLayout();
        Xunit.Assert.True(maximizeRestoreButton.IsKeyboardFocused);
        TextBlock glyph = (TextBlock)controls.FindName("MaximizeRestoreGlyphTextBlock");
        Border maxChrome = (Border)maximizeRestoreButton.Template.FindName("Chrome", maximizeRestoreButton);
        Xunit.Assert.Equal("\uE922", glyph.Text);
        Xunit.Assert.Equal("Maximize Test window", AutomationProperties.GetName(maximizeRestoreButton));
        Xunit.Assert.Equal(primaryBrush, maxChrome.BorderBrush);

        window.WindowState = WindowState.Maximized;
        window.UpdateLayout();
        Xunit.Assert.Equal("\uE923", glyph.Text);
        Xunit.Assert.Equal("Restore Test window", AutomationProperties.GetName(maximizeRestoreButton));
        Xunit.Assert.Equal(primaryBrush, maxChrome.BorderBrush);

        window.WindowState = WindowState.Normal;
        window.UpdateLayout();
        Xunit.Assert.Equal("\uE922", glyph.Text);
        Xunit.Assert.Equal("Maximize Test window", AutomationProperties.GetName(maximizeRestoreButton));
        Xunit.Assert.Equal(primaryBrush, maxChrome.BorderBrush);

        Xunit.Assert.Equal("Minimize Test window", AutomationProperties.GetName(minimizeButton));
        Xunit.Assert.Equal("Close Test window", AutomationProperties.GetName(closeButton));
      }
      catch (Exception ex)
      {
        failure = ex;
      }
      finally
      {
        window?.Close();
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(10));
    Xunit.Assert.True(completed, "STA test thread timed out.");

    Xunit.Assert.Null(failure);
  }

  [Xunit.Fact]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The STA test reports WPF failures to the asserting thread.")]
  public void WindowStyle_AppliesOuterBorderAndSuppressesWhenMaximized()
  {
    Exception? failure = null;
    Thickness? normalThickness = null;
    Thickness? maximizedThickness = null;
    Thickness? restoredThickness = null;
    Brush? borderBrush = null;

    Thread thread = new(() =>
    {
      Window? window = null;
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }

        window = new()
        {
          Width = 300,
          Height = 150,
          Left = -10_000,
          Top = -10_000,
          ShowActivated = false,
          ShowInTaskbar = false,
          WindowStyle = WindowStyle.None,
        };
        window.Show();
        window.UpdateLayout();

        normalThickness = window.BorderThickness;
        borderBrush = window.BorderBrush;

        window.WindowState = WindowState.Maximized;
        window.UpdateLayout();
        maximizedThickness = window.BorderThickness;

        window.WindowState = WindowState.Normal;
        window.UpdateLayout();
        restoredThickness = window.BorderThickness;
      }
      catch (Exception ex)
      {
        failure = ex;
      }
      finally
      {
        window?.Close();
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(10));
    Xunit.Assert.True(completed, "STA test thread timed out.");

    Xunit.Assert.Null(failure);
    Xunit.Assert.Equal(new Thickness(1), normalThickness);
    Xunit.Assert.NotNull(borderBrush);
    Xunit.Assert.Equal(new Thickness(0), maximizedThickness);
    Xunit.Assert.Equal(new Thickness(1), restoredThickness);
  }

  [Xunit.Fact]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The STA test reports WPF failures to the asserting thread.")]
  public void HighContrastClientBoundary_IsLayoutNeutralNonInteractiveAndStateAware()
  {
    Exception? failure = null;

    Thread thread = new(() =>
    {
      Window? window = null;
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }

        SolidColorBrush semanticBoundary = new(SystemColors.WindowTextColor);
        window = new()
        {
          Width = 300,
          Height = 150,
          Left = -10_000,
          Top = -10_000,
          ShowActivated = false,
          ShowInTaskbar = false,
          WindowStyle = WindowStyle.None,
        };
        window.Resources["Brush.Border.Subtle"] = semanticBoundary;

        Grid root = new();
        Border content = new() { Background = Brushes.Transparent };
        Border boundary = new() { CornerRadius = new CornerRadius(16) };
        boundary.SetResourceReference(FrameworkElement.StyleProperty, "AppHighContrastWindowBoundaryStyle");
        root.Children.Add(content);
        root.Children.Add(boundary);
        window.Content = root;

        WindowThemeBehavior.SetIsHighContrastActive(window, false);
        window.Show();
        window.UpdateLayout();

        double contentWidth = content.ActualWidth;
        double contentHeight = content.ActualHeight;
        Xunit.Assert.Equal(new Thickness(0), boundary.BorderThickness);

        WindowThemeBehavior.SetIsHighContrastActive(window, true);
        window.UpdateLayout();

        Xunit.Assert.True(WindowThemeBehavior.GetIsHighContrastActive(boundary));
        Xunit.Assert.Equal(new Thickness(1), boundary.BorderThickness);
        Xunit.Assert.Equal(semanticBoundary, boundary.BorderBrush);
        Xunit.Assert.False(boundary.IsHitTestVisible);
        Xunit.Assert.False(boundary.Focusable);
        Xunit.Assert.True(Panel.GetZIndex(boundary) > 0);
        Xunit.Assert.Equal(root.ActualWidth, boundary.ActualWidth);
        Xunit.Assert.Equal(root.ActualHeight, boundary.ActualHeight);
        Xunit.Assert.Equal(contentWidth, content.ActualWidth);
        Xunit.Assert.Equal(contentHeight, content.ActualHeight);

        window.WindowState = WindowState.Maximized;
        window.UpdateLayout();
        Xunit.Assert.Equal(new Thickness(0), boundary.BorderThickness);

        window.WindowState = WindowState.Normal;
        window.UpdateLayout();
        Xunit.Assert.Equal(new Thickness(1), boundary.BorderThickness);

        WindowThemeBehavior.SetIsHighContrastActive(window, false);
        window.UpdateLayout();
        Xunit.Assert.Equal(new Thickness(0), boundary.BorderThickness);
      }
      catch (Exception ex)
      {
        failure = ex;
      }
      finally
      {
        window?.Close();
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(10));
    Xunit.Assert.True(completed, "STA test thread timed out.");

    Xunit.Assert.Null(failure);
  }
}
