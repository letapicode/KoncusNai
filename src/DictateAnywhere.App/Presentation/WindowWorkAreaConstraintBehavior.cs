using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DictateAnywhere.App.Presentation;

/// <summary>Keeps custom-chrome windows inside the monitor work area at launch and when maximized.</summary>
internal static class WindowWorkAreaConstraintBehavior
{
  private const int WmGetMinMaxInfo = 0x0024;
  private const int MonitorDefaultToNearest = 0x00000002;
  private static readonly ConditionalWeakTable<Window, HookState> States = new();

  public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
    "IsEnabled",
    typeof(bool),
    typeof(WindowWorkAreaConstraintBehavior),
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
    if (dependencyObject is not Window window)
    {
      throw new InvalidOperationException("WindowWorkAreaConstraintBehavior can only be attached to a Window.");
    }

    Detach(window);
    if (args.NewValue is true)
    {
      HookState state = new(window);
      States.Add(window, state);
      state.Attach();
    }
  }

  private static void Detach(Window window)
  {
    if (States.TryGetValue(window, out HookState? state))
    {
      state.Detach();
      States.Remove(window);
    }
  }

  private sealed class HookState
  {
    private readonly Window window;
    private HwndSource? source;

    public HookState(Window window) => this.window = window;

    public void Attach()
    {
      window.SourceInitialized += OnSourceInitialized;
      window.Loaded += OnLoaded;
      window.Closed += OnClosed;
    }

    public void Detach()
    {
      window.SourceInitialized -= OnSourceInitialized;
      window.Loaded -= OnLoaded;
      window.Closed -= OnClosed;
      if (source is not null)
      {
        source.RemoveHook(WndProc);
        source = null;
      }
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
      source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
      source?.AddHook(WndProc);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
      window.Loaded -= OnLoaded;
      if (source is null || window.WindowState != WindowState.Normal)
      {
        return;
      }

      IntPtr hwnd = new WindowInteropHelper(window).Handle;
      if (!TryGetWorkArea(hwnd, out NativeRect nativeWorkArea))
      {
        return;
      }

      Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
      Point workAreaTopLeft = fromDevice.Transform(new Point(nativeWorkArea.Left, nativeWorkArea.Top));
      Point workAreaBottomRight = fromDevice.Transform(new Point(nativeWorkArea.Right, nativeWorkArea.Bottom));
      Rect workArea = new(workAreaTopLeft, workAreaBottomRight);
      Rect requested = new(
        window.Left,
        window.Top,
        window.ActualWidth > 0 ? window.ActualWidth : window.Width,
        window.ActualHeight > 0 ? window.ActualHeight : window.Height);
      Rect constrained = ConstrainBoundsToWorkArea(requested, workArea, new Size(window.MinWidth, window.MinHeight));
      window.Width = constrained.Width;
      window.Height = constrained.Height;
      window.Left = constrained.Left;
      window.Top = constrained.Top;
    }

    private void OnClosed(object? sender, EventArgs args) =>
      WindowWorkAreaConstraintBehavior.Detach(window);

    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
      if (message == WmGetMinMaxInfo)
      {
        handled = ApplyConstraints(window, hwnd, lParam);
      }

      return IntPtr.Zero;
    }
  }

  private static bool ApplyConstraints(Window window, IntPtr hwnd, IntPtr lParam)
  {
    if (lParam == IntPtr.Zero)
    {
      return false;
    }

    if (!TryGetMonitorInfo(hwnd, out MonitorInfo monitorInfo))
    {
      return false;
    }

    MinMaxInfo minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
    NativeRect workArea = monitorInfo.rcWork;
    NativeRect monitorArea = monitorInfo.rcMonitor;
    minMaxInfo.ptMaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
    minMaxInfo.ptMaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
    minMaxInfo.ptMaxSize.X = Math.Abs(workArea.Right - workArea.Left);
    minMaxInfo.ptMaxSize.Y = Math.Abs(workArea.Bottom - workArea.Top);
    DpiScale dpi = VisualTreeHelper.GetDpi(window);
    minMaxInfo.ptMinTrackSize.X = Math.Max(1, (int)Math.Ceiling(window.MinWidth * dpi.DpiScaleX));
    minMaxInfo.ptMinTrackSize.Y = Math.Max(1, (int)Math.Ceiling(window.MinHeight * dpi.DpiScaleY));
    Marshal.StructureToPtr(minMaxInfo, lParam, fDeleteOld: false);
    return true;
  }

  internal static Rect ConstrainBoundsToWorkArea(Rect requested, Rect workArea, Size minimum)
  {
    if (workArea.IsEmpty || !double.IsFinite(workArea.Width) || !double.IsFinite(workArea.Height)
      || workArea.Width <= 0 || workArea.Height <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(workArea), "The monitor work area must have finite positive dimensions.");
    }

    double minimumWidth = double.IsFinite(minimum.Width) && minimum.Width > 0 ? minimum.Width : 1;
    double minimumHeight = double.IsFinite(minimum.Height) && minimum.Height > 0 ? minimum.Height : 1;
    double requestedWidth = double.IsFinite(requested.Width) && requested.Width > 0 ? requested.Width : minimumWidth;
    double requestedHeight = double.IsFinite(requested.Height) && requested.Height > 0 ? requested.Height : minimumHeight;
    double width = Math.Min(Math.Max(requestedWidth, Math.Min(minimumWidth, workArea.Width)), workArea.Width);
    double height = Math.Min(Math.Max(requestedHeight, Math.Min(minimumHeight, workArea.Height)), workArea.Height);
    double requestedLeft = double.IsFinite(requested.Left) ? requested.Left : workArea.Left;
    double requestedTop = double.IsFinite(requested.Top) ? requested.Top : workArea.Top;
    double left = Math.Clamp(requestedLeft, workArea.Left, workArea.Right - width);
    double top = Math.Clamp(requestedTop, workArea.Top, workArea.Bottom - height);
    return new Rect(left, top, width, height);
  }

  private static bool TryGetWorkArea(IntPtr hwnd, out NativeRect workArea)
  {
    if (TryGetMonitorInfo(hwnd, out MonitorInfo monitorInfo))
    {
      workArea = monitorInfo.rcWork;
      return true;
    }

    workArea = default;
    return false;
  }

  private static bool TryGetMonitorInfo(IntPtr hwnd, out MonitorInfo monitorInfo)
  {
    IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
    monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
    return monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo);
  }

  [DllImport("user32.dll")]
  private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

  [DllImport("user32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

  [StructLayout(LayoutKind.Sequential)]
  private struct NativePoint
  {
    public int X;
    public int Y;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct MinMaxInfo
  {
    public NativePoint ptReserved;
    public NativePoint ptMaxSize;
    public NativePoint ptMaxPosition;
    public NativePoint ptMinTrackSize;
    public NativePoint ptMaxTrackSize;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct NativeRect
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct MonitorInfo
  {
    public int cbSize;
    public NativeRect rcMonitor;
    public NativeRect rcWork;
    public int dwFlags;
  }
}
