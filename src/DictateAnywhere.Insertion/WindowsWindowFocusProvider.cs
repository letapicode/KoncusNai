using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Insertion;

public sealed class WindowsWindowFocusProvider : IWindowFocusProvider, IEditableFocusRestorer, IWindowFocusRestorer
{
  private const int ClassNameBufferLength = 256;
  private const int GwlStyle = -16;
  private const int EditStylePassword = 0x0020;
  private const int ShowWindowRestore = 9;
  private static readonly Condition EditableBrowserTargetCondition = new OrCondition(
    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
  private static readonly HashSet<string> BrowserProcessNames = new(StringComparer.OrdinalIgnoreCase)
  {
    "chrome",
    "msedge",
    "brave",
    "opera",
  };
  private static readonly HashSet<string> ChromiumHostWindowClasses = new(StringComparer.OrdinalIgnoreCase)
  {
    "Chrome_WidgetWin_0",
    "Chrome_WidgetWin_1",
  };

  public nint GetForegroundWindowHandle()
  {
    return GetForegroundWindowNative();
  }

  public WindowFocusContext GetWindowFocusContext()
  {
    nint foregroundWindowHandle = GetForegroundWindowHandle();
    if (foregroundWindowHandle == 0)
    {
      return WindowFocusContext.Empty;
    }

    uint threadId = GetWindowThreadProcessIdNative(foregroundWindowHandle, out uint processId);

    nint focusedControlHandle = IntPtr.Zero;
    nint caretWindowHandle = IntPtr.Zero;
    GUITHREADINFO threadInfo = new()
    {
      cbSize = Marshal.SizeOf<GUITHREADINFO>(),
    };

    if (threadId != 0 && GetGUIThreadInfoNative(threadId, ref threadInfo))
    {
      focusedControlHandle = threadInfo.hwndFocus;
      caretWindowHandle = threadInfo.hwndCaret;
    }

    ScreenBounds? caretBounds = threadId != 0
      ? TryGetCaretBounds(threadInfo)
      : null;
    ScreenBounds? foregroundWindowBounds = TryGetWindowBounds(foregroundWindowHandle);

    string? processName = TryGetProcessName(processId);
    string? windowClass = TryGetClassName(foregroundWindowHandle);
    string? nativeFocusedControlClass = focusedControlHandle != 0
      ? TryGetClassName(focusedControlHandle)
      : null;
    bool nativePasswordProtected = focusedControlHandle != 0
      && TryIsPasswordProtectedEditControl(focusedControlHandle);
    string? windowTitle = TryGetWindowText(foregroundWindowHandle);
    AutomationFocusSnapshot automationSnapshot = TryGetAutomationFocusSnapshot(
      processId,
      processName,
      windowClass,
      foregroundWindowHandle,
      focusedControlHandle,
      nativeFocusedControlClass,
      foregroundWindowBounds);

    return new WindowFocusContext
    {
      ForegroundWindowHandle = foregroundWindowHandle,
      ForegroundProcessId = processId,
      ForegroundProcessStartTicks = TryGetProcessStartTicks(processId),
      FocusedAutomationRuntimeId = automationSnapshot.RuntimeId,
      ForegroundProcessName = processName,
      ForegroundWindowClassName = windowClass,
      FocusedControlClassName = automationSnapshot.ClassName
        ?? nativeFocusedControlClass,
      FocusedControlPasswordProtected = nativePasswordProtected || automationSnapshot.IsPasswordProtected,
      ForegroundWindowTitle = windowTitle,
      FocusedControlHandle = focusedControlHandle,
      CaretWindowHandle = caretWindowHandle,
      CaretBounds = caretBounds,
      FocusedElementBounds = automationSnapshot.Bounds,
      RawFocusedElementBounds = automationSnapshot.RawBounds,
      ForegroundWindowBounds = foregroundWindowBounds,
      Editability = automationSnapshot.Editability,
      EditabilityReason = automationSnapshot.EditabilityReason,
      VerificationMode = automationSnapshot.VerificationMode,
      BrowserSurfaceKind = automationSnapshot.BrowserSurfaceKind,
      FocusedAutomationId = automationSnapshot.AutomationId,
      FocusedAutomationName = automationSnapshot.AutomationName,
      FocusedAutomationClassName = automationSnapshot.ClassName,
      FocusedAutomationControlType = automationSnapshot.ControlTypeName,
      FocusedAutomationFrameworkId = automationSnapshot.FrameworkId,
      RawFocusedAutomationId = automationSnapshot.RawAutomationId,
      RawFocusedAutomationName = automationSnapshot.RawAutomationName,
      RawFocusedAutomationClassName = automationSnapshot.RawClassName,
      RawFocusedAutomationControlType = automationSnapshot.RawControlTypeName,
      FocusResolutionSource = automationSnapshot.ResolutionSource,
      FocusedControlReadOnly = automationSnapshot.IsReadOnly,
      ObservedText = automationSnapshot.ObservedText,
    };
  }

  public bool TryRestoreEditableFocus(WindowFocusContext currentContext)
  {
    ArgumentNullException.ThrowIfNull(currentContext);

    if (currentContext.ForegroundWindowHandle == 0
        || !IsChromiumHost(
          currentContext.ForegroundProcessName,
          currentContext.ForegroundWindowClassName,
          frameworkId: currentContext.FocusedAutomationFrameworkId))
    {
      return false;
    }

    try
    {
      AutomationElement? rawElement = TryGetFocusedAutomationElement(
        currentContext.ForegroundProcessId,
        currentContext.ForegroundWindowHandle,
        currentContext.FocusedControlHandle);
      BrowserEditableTargetSelection selection = ResolveChromiumEditableTargetSelection(
        currentContext.ForegroundProcessName,
        currentContext.ForegroundWindowClassName,
        currentContext.ForegroundWindowHandle,
        currentContext.ForegroundWindowBounds,
        rawElement,
        currentContext.FocusedControlHandle);
      BrowserEditableTargetCandidate? candidate = selection.Candidate;
      if (candidate?.Element is null || !BrowserEditableTargetHeuristics.IsEditableCandidate(candidate))
      {
        return false;
      }

      candidate.Element.SetFocus();
      return true;
    }
    catch (COMException)
    {
      return false;
    }
    catch (ElementNotAvailableException)
    {
      return false;
    }
    catch (InvalidOperationException)
    {
      return false;
    }
  }

  public bool TryRestoreForegroundWindow(WindowFocusContext targetContext)
  {
    ArgumentNullException.ThrowIfNull(targetContext);

    nint targetWindowHandle = targetContext.ForegroundWindowHandle;
    if (targetWindowHandle == 0 || !IsWindowNative(targetWindowHandle))
    {
      return false;
    }

    if (IsIconicNative(targetWindowHandle))
    {
      _ = ShowWindowNative(targetWindowHandle, ShowWindowRestore);
    }

    if (SetForegroundWindowNative(targetWindowHandle))
    {
      return true;
    }

    // Windows normally prevents a background process from stealing focus. A
    // dictation session is different: the user explicitly chose this target
    // before recording began. Temporarily sharing input between the current
    // foreground thread and that captured target lets Windows honor the
    // restoration without redirecting text to whichever app the user browsed
    // to while transcription was running.
    return TryRestoreForegroundWindowWithInputAttachment(targetWindowHandle);
  }

  private static bool TryRestoreForegroundWindowWithInputAttachment(nint targetWindowHandle)
  {
    nint currentForegroundWindowHandle = GetForegroundWindowNative();
    uint foregroundThreadId = currentForegroundWindowHandle == 0
      ? 0
      : GetWindowThreadProcessIdNative(currentForegroundWindowHandle, out _);
    uint targetThreadId = GetWindowThreadProcessIdNative(targetWindowHandle, out _);
    if (foregroundThreadId == 0 || targetThreadId == 0 || foregroundThreadId == targetThreadId)
    {
      return false;
    }

    if (!AttachThreadInputNative(foregroundThreadId, targetThreadId, attach: true))
    {
      return false;
    }

    try
    {
      _ = BringWindowToTopNative(targetWindowHandle);
      return SetForegroundWindowNative(targetWindowHandle);
    }
    finally
    {
      _ = AttachThreadInputNative(foregroundThreadId, targetThreadId, attach: false);
    }
  }

  private static string? TryGetProcessName(uint processId)
  {
    if (processId == 0)
    {
      return null;
    }

    try
    {
      using Process process = Process.GetProcessById(checked((int)processId));
      return process.ProcessName;
    }
    catch (ArgumentException)
    {
      return null;
    }
    catch (InvalidOperationException)
    {
      return null;
    }
  }

  private static string? TryGetClassName(nint windowHandle)
  {
    if (windowHandle == 0)
    {
      return null;
    }

    StringBuilder builder = new(ClassNameBufferLength);
    int copied = GetClassNameNative(windowHandle, builder, builder.Capacity);
    if (copied <= 0)
    {
      return null;
    }

    return builder.ToString(0, copied);
  }

  private static bool TryIsPasswordProtectedEditControl(nint windowHandle)
  {
    if (windowHandle == 0)
    {
      return false;
    }

    nint style = GetWindowStyle(windowHandle);
    if (style == 0)
    {
      return false;
    }

    long styleValue = style.ToInt64();
    return (styleValue & EditStylePassword) == EditStylePassword;
  }

  private static nint GetWindowStyle(nint windowHandle)
  {
    return IntPtr.Size == 8
      ? GetWindowLongPtrNative(windowHandle, GwlStyle)
      : new IntPtr(GetWindowLongNative(windowHandle, GwlStyle));
  }

  private static string? TryGetWindowText(nint windowHandle)
  {
    if (windowHandle == 0)
    {
      return null;
    }

    int length = GetWindowTextLengthNative(windowHandle);
    if (length <= 0)
    {
      return null;
    }

    StringBuilder builder = new(length + 1);
    int copied = GetWindowTextNative(windowHandle, builder, builder.Capacity);
    if (copied <= 0)
    {
      return null;
    }

    return builder.ToString(0, copied);
  }

  private static AutomationFocusSnapshot TryGetAutomationFocusSnapshot(
    uint processId,
    string? processName,
    string? foregroundWindowClass,
    nint foregroundWindowHandle,
    nint focusedControlHandle,
    string? nativeFocusedControlClass,
    ScreenBounds? foregroundWindowBounds)
  {
    try
    {
      AutomationElement? rawElement = TryGetFocusedAutomationElement(processId, foregroundWindowHandle, focusedControlHandle);
      if (rawElement is null)
      {
        return CreateFallbackSnapshot(processName, nativeFocusedControlClass, focusedControlHandle);
      }

      BrowserEditableTargetSelection browserSelection = ResolveChromiumEditableTargetSelection(
        processName,
        foregroundWindowClass,
        foregroundWindowHandle,
        foregroundWindowBounds,
        rawElement,
        focusedControlHandle);
      BrowserEditableTargetCandidate rawCandidate = browserSelection.Candidate is not null
        && browserSelection.Candidate.Source == BrowserEditableTargetSource.RawFocusedElement
        ? browserSelection.Candidate
        : CreateEditableTargetCandidate(
          rawElement,
          BrowserEditableTargetSource.RawFocusedElement,
          searchOrder: 0,
          focusedControlHandle);
      BrowserEditableTargetCandidate resolvedCandidate = browserSelection.Candidate ?? rawCandidate;
      bool accessibilityPlaceholder = BrowserEditableTargetHeuristics.IsAccessibilityPlaceholder(resolvedCandidate);

      BrowserSurfaceKind browserSurfaceKind = ClassifyBrowserSurface(
        processName,
        resolvedCandidate.AutomationId,
        resolvedCandidate.AutomationName,
        resolvedCandidate.ClassName,
        resolvedCandidate.ControlTypeName,
        resolvedCandidate.SupportsWritableValuePattern,
        resolvedCandidate.SupportsTextPattern,
        resolvedCandidate.IsKeyboardFocusable,
        resolvedCandidate.Bounds.HasValue && !resolvedCandidate.Bounds.Value.IsEmpty,
        accessibilityPlaceholder);

      (WindowEditability editability, string? reason) = ClassifyEditability(
        processName,
        nativeFocusedControlClass,
        resolvedCandidate.ClassName,
        resolvedCandidate.ControlTypeName,
        browserSurfaceKind,
        accessibilityPlaceholder,
        resolvedCandidate.IsPasswordProtected,
        resolvedCandidate.IsReadOnly,
        resolvedCandidate.SupportsWritableValuePattern,
        resolvedCandidate.SupportsTextPattern,
        resolvedCandidate.IsKeyboardFocusable);

      return new AutomationFocusSnapshot(
        ClassName: resolvedCandidate.ClassName,
        ControlTypeName: resolvedCandidate.ControlTypeName,
        Bounds: resolvedCandidate.Bounds,
        AutomationId: resolvedCandidate.AutomationId,
        AutomationName: resolvedCandidate.AutomationName,
        FrameworkId: resolvedCandidate.FrameworkId,
        Editability: editability,
        EditabilityReason: reason,
        VerificationMode: editability == WindowEditability.Editable ? resolvedCandidate.VerificationMode : null,
        BrowserSurfaceKind: browserSurfaceKind,
        ObservedText: editability == WindowEditability.Editable ? resolvedCandidate.ObservedText : null,
        IsReadOnly: resolvedCandidate.IsReadOnly,
        IsPasswordProtected: resolvedCandidate.IsPasswordProtected,
        RawAutomationId: rawCandidate.AutomationId,
        RawAutomationName: rawCandidate.AutomationName,
        RawClassName: rawCandidate.ClassName,
        RawControlTypeName: rawCandidate.ControlTypeName,
        RawBounds: rawCandidate.Bounds,
        ResolutionSource: browserSelection.ResolutionSource.ToString(),
        RuntimeId: resolvedCandidate.Element is { } target
          ? SafeGet(() => string.Join(".", target.GetRuntimeId())) : null);
    }
    catch (COMException)
    {
      return CreateFallbackSnapshot(processName, nativeFocusedControlClass, focusedControlHandle);
    }
    catch (ElementNotAvailableException)
    {
      return CreateFallbackSnapshot(processName, nativeFocusedControlClass, focusedControlHandle);
    }
    catch (InvalidOperationException)
    {
      return CreateFallbackSnapshot(processName, nativeFocusedControlClass, focusedControlHandle);
    }
  }

  private static AutomationElement? TryGetFocusedAutomationElement(
    uint processId,
    nint foregroundWindowHandle,
    nint focusedControlHandle)
  {
    AutomationElement? focusedElement = null;
    try
    {
      focusedElement = AutomationElement.FocusedElement;
      if (focusedElement is not null)
      {
        int focusedProcessId = SafeGet(() => focusedElement.Current.ProcessId, fallbackValue: 0);
        if (processId == 0
            || focusedProcessId == 0
            || focusedProcessId == processId
            || IsElementWithinForegroundWindow(focusedElement, foregroundWindowHandle))
        {
          return focusedElement;
        }
      }
    }
    catch (ElementNotAvailableException)
    {
    }
    catch (COMException)
    {
    }
    catch (InvalidOperationException)
    {
    }

    if (focusedControlHandle == 0)
    {
      return null;
    }

    try
    {
      return AutomationElement.FromHandle(focusedControlHandle);
    }
    catch (ElementNotAvailableException)
    {
      return null;
    }
    catch (COMException)
    {
      return null;
    }
    catch (InvalidOperationException)
    {
      return null;
    }
  }

  private static BrowserEditableTargetSelection ResolveChromiumEditableTargetSelection(
    string? processName,
    string? foregroundWindowClass,
    nint foregroundWindowHandle,
    ScreenBounds? foregroundWindowBounds,
    AutomationElement? rawElement,
    nint focusedControlHandle)
  {
    BrowserEditableTargetCandidate? rawCandidate = rawElement is null
      ? null
      : CreateEditableTargetCandidate(
        rawElement,
        BrowserEditableTargetSource.RawFocusedElement,
        searchOrder: 0,
        focusedControlHandle);
    if (!IsChromiumHost(
          processName,
          foregroundWindowClass,
          frameworkId: rawCandidate?.FrameworkId))
    {
      return new BrowserEditableTargetSelection(
        rawCandidate,
        rawCandidate?.Source ?? BrowserEditableTargetSource.None,
        PromotedFromRawFocus: false);
    }

    List<BrowserEditableTargetCandidate> candidates = new();
    HashSet<string> seenKeys = new(StringComparer.Ordinal);
    if (rawCandidate is not null)
    {
      TryAddCandidate(candidates, seenKeys, rawCandidate);
    }

    if (rawElement is not null)
    {
      TryCollectAncestorCandidates(candidates, seenKeys, rawElement);
      TryCollectDescendantCandidates(candidates, seenKeys, rawElement, BrowserEditableTargetSource.FocusedDescendant, maxCandidates: 12);
    }

    AutomationElement? foregroundWindowElement = TryGetAutomationElementFromHandle(foregroundWindowHandle);
    if (foregroundWindowElement is not null)
    {
      TryCollectDescendantCandidates(
        candidates,
        seenKeys,
        foregroundWindowElement,
        BrowserEditableTargetSource.ForegroundWindowSubtree,
        maxCandidates: 24);
    }

    return BrowserEditableTargetHeuristics.Select(rawCandidate, candidates, foregroundWindowBounds);
  }

  private static void TryCollectAncestorCandidates(
    List<BrowserEditableTargetCandidate> candidates,
    HashSet<string> seenKeys,
    AutomationElement rawElement)
  {
    AutomationElement? current = rawElement;
    for (int depth = 0; depth < 6; depth++)
    {
      current = TryGetParentElement(current);
      if (current is null)
      {
        break;
      }

      TryAddCandidate(
        candidates,
        seenKeys,
        CreateEditableTargetCandidate(
          current,
          BrowserEditableTargetSource.FocusedAncestor,
          searchOrder: depth + 1,
          boundsFallbackHandle: 0));
    }
  }

  private static void TryCollectDescendantCandidates(
    List<BrowserEditableTargetCandidate> candidates,
    HashSet<string> seenKeys,
    AutomationElement rootElement,
    BrowserEditableTargetSource source,
    int maxCandidates)
  {
    AutomationElementCollection? descendants = SafeGet(
      () => rootElement.FindAll(TreeScope.Descendants, EditableBrowserTargetCondition),
      fallbackValue: null);
    if (descendants is null)
    {
      return;
    }

    int added = 0;
    for (int index = 0; index < descendants.Count && added < maxCandidates; index++)
    {
      BrowserEditableTargetCandidate candidate = CreateEditableTargetCandidate(
        descendants[index],
        source,
        searchOrder: index + 1,
        boundsFallbackHandle: 0);

      if (TryAddCandidate(candidates, seenKeys, candidate))
      {
        added++;
      }
    }
  }

  private static bool TryAddCandidate(
    List<BrowserEditableTargetCandidate> candidates,
    HashSet<string> seenKeys,
    BrowserEditableTargetCandidate candidate)
  {
    string key = string.Concat(
      candidate.AutomationId ?? string.Empty,
      "|",
      candidate.AutomationName ?? string.Empty,
      "|",
      candidate.ClassName ?? string.Empty,
      "|",
      candidate.ControlTypeName ?? string.Empty,
      "|",
      candidate.Bounds?.ToString() ?? string.Empty);
    if (!seenKeys.Add(key))
    {
      return false;
    }

    candidates.Add(candidate);
    return true;
  }

  private static BrowserEditableTargetCandidate CreateEditableTargetCandidate(
    AutomationElement element,
    BrowserEditableTargetSource source,
    int searchOrder,
    nint boundsFallbackHandle)
  {
    string? automationClass = SafeGet(() => element.Current.ClassName);
    string? controlTypeName = SafeGet(() => element.Current.ControlType.ProgrammaticName);
    string? frameworkId = SafeGet(() => element.Current.FrameworkId);
    string? automationId = SafeGet(() => element.Current.AutomationId);
    string? automationName = SafeGet(() => element.Current.Name);
    ScreenBounds? bounds = TryGetFocusedElementBounds(element, boundsFallbackHandle);
    bool isKeyboardFocusable = SafeGet(() => element.Current.IsKeyboardFocusable, fallbackValue: false);
    bool isPassword = SafeGet(() => element.Current.IsPassword, fallbackValue: false);
    bool isReadOnly = false;
    string? observedText = null;
    string? verificationMode = null;
    bool supportsWritableValuePattern = false;
    bool supportsTextPattern = false;

    if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valuePatternObject)
        && valuePatternObject is ValuePattern valuePattern)
    {
      isReadOnly = SafeGet(() => valuePattern.Current.IsReadOnly, fallbackValue: false);
      observedText = SafeGet(() => valuePattern.Current.Value);
      verificationMode = "ValuePattern";
      supportsWritableValuePattern = !isReadOnly;
    }

    if (element.TryGetCurrentPattern(TextPattern.Pattern, out object? textPatternObject)
        && textPatternObject is TextPattern textPattern)
    {
      if (string.IsNullOrWhiteSpace(observedText))
      {
        observedText = SafeGet(() => textPattern.DocumentRange.GetText(-1));
      }

      verificationMode ??= "TextPattern";
      supportsTextPattern = true;
    }

    return new BrowserEditableTargetCandidate(
      AutomationId: automationId,
      AutomationName: automationName,
      ClassName: automationClass,
      ControlTypeName: controlTypeName,
      FrameworkId: frameworkId,
      Bounds: bounds,
      IsKeyboardFocusable: isKeyboardFocusable,
      IsPasswordProtected: isPassword,
      IsReadOnly: isReadOnly,
      SupportsWritableValuePattern: supportsWritableValuePattern,
      SupportsTextPattern: supportsTextPattern,
      VerificationMode: verificationMode,
      ObservedText: observedText,
      Source: source,
      SearchOrder: searchOrder,
      Element: element);
  }

  private static AutomationElement? TryGetParentElement(AutomationElement? element)
  {
    if (element is null)
    {
      return null;
    }

    return SafeGet(
      () => TreeWalker.ControlViewWalker.GetParent(element),
      fallbackValue: null);
  }

  private static AutomationElement? TryGetAutomationElementFromHandle(nint windowHandle)
  {
    if (windowHandle == 0)
    {
      return null;
    }

    try
    {
      return AutomationElement.FromHandle(windowHandle);
    }
    catch (ElementNotAvailableException)
    {
      return null;
    }
    catch (COMException)
    {
      return null;
    }
    catch (InvalidOperationException)
    {
      return null;
    }
  }

  private static bool IsElementWithinForegroundWindow(AutomationElement element, nint foregroundWindowHandle)
  {
    if (foregroundWindowHandle == 0)
    {
      return false;
    }

    AutomationElement? current = element;
    for (int depth = 0; depth < 16 && current is not null; depth++)
    {
      int nativeHandle = SafeGet(() => current.Current.NativeWindowHandle, fallbackValue: 0);
      if (nativeHandle != 0 && (nint)nativeHandle == foregroundWindowHandle)
      {
        return true;
      }

      current = TryGetParentElement(current);
    }

    return false;
  }

  private static AutomationFocusSnapshot CreateFallbackSnapshot(
    string? processName,
    string? nativeFocusedControlClass,
    nint focusedControlHandle)
  {
    WindowEditability editability = IsLikelyNativeEditableControl(nativeFocusedControlClass)
      ? WindowEditability.Editable
      : WindowEditability.Unknown;
    string? reason = editability == WindowEditability.Editable
      ? "native-focus-class"
      : null;
    BrowserSurfaceKind browserSurfaceKind = BrowserProcessNames.Contains(processName ?? string.Empty)
      ? BrowserSurfaceKind.UnknownBrowserSurface
      : BrowserSurfaceKind.None;

    return new AutomationFocusSnapshot(
      ClassName: nativeFocusedControlClass,
      ControlTypeName: null,
      Bounds: TryGetWindowBounds(focusedControlHandle),
      AutomationId: null,
      AutomationName: null,
      FrameworkId: null,
      Editability: editability,
      EditabilityReason: reason,
      VerificationMode: null,
      BrowserSurfaceKind: browserSurfaceKind,
      ObservedText: null,
      IsReadOnly: false,
      IsPasswordProtected: false,
      RawAutomationId: null,
      RawAutomationName: null,
      RawClassName: nativeFocusedControlClass,
      RawControlTypeName: null,
      RawBounds: TryGetWindowBounds(focusedControlHandle),
      ResolutionSource: "NativeFocusClass");
  }

  private static ScreenBounds? TryGetCaretBounds(GUITHREADINFO threadInfo)
  {
    if (threadInfo.hwndCaret == 0)
    {
      return null;
    }

    POINT topLeft = new()
    {
      X = threadInfo.rcCaret.Left,
      Y = threadInfo.rcCaret.Top,
    };
    POINT bottomRight = new()
    {
      X = threadInfo.rcCaret.Right,
      Y = threadInfo.rcCaret.Bottom,
    };

    if (!ClientToScreenNative(threadInfo.hwndCaret, ref topLeft)
        || !ClientToScreenNative(threadInfo.hwndCaret, ref bottomRight))
    {
      return null;
    }

    return CreateScreenBounds(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y, ensureVisibleSize: true);
  }

  private static ScreenBounds? TryGetFocusedElementBounds(AutomationElement element, nint focusedControlHandle)
  {
    ScreenBounds? automationBounds = TryGetAutomationBounds(element);
    if (automationBounds.HasValue)
    {
      return automationBounds;
    }

    return TryGetWindowBounds(focusedControlHandle);
  }

  private static ScreenBounds? TryGetAutomationBounds(AutomationElement element)
  {
    System.Windows.Rect bounds = SafeGet(
      () => element.Current.BoundingRectangle,
      fallbackValue: System.Windows.Rect.Empty);
    if (bounds.IsEmpty
        || double.IsNaN(bounds.Left)
        || double.IsNaN(bounds.Top)
        || double.IsNaN(bounds.Right)
        || double.IsNaN(bounds.Bottom)
        || double.IsInfinity(bounds.Left)
        || double.IsInfinity(bounds.Top)
        || double.IsInfinity(bounds.Right)
        || double.IsInfinity(bounds.Bottom))
    {
      return null;
    }

    int left = (int)Math.Floor(bounds.Left);
    int top = (int)Math.Floor(bounds.Top);
    int right = (int)Math.Ceiling(bounds.Right);
    int bottom = (int)Math.Ceiling(bounds.Bottom);
    return CreateScreenBounds(left, top, right, bottom, ensureVisibleSize: false);
  }

  private static ScreenBounds? TryGetWindowBounds(nint windowHandle)
  {
    if (windowHandle == 0 || !GetWindowRectNative(windowHandle, out RECT rect))
    {
      return null;
    }

    return CreateScreenBounds(rect.Left, rect.Top, rect.Right, rect.Bottom, ensureVisibleSize: false);
  }

  private static ScreenBounds? CreateScreenBounds(
    int left,
    int top,
    int right,
    int bottom,
    bool ensureVisibleSize)
  {
    int normalizedLeft = Math.Min(left, right);
    int normalizedTop = Math.Min(top, bottom);
    int width = Math.Abs(right - left);
    int height = Math.Abs(bottom - top);

    if (ensureVisibleSize)
    {
      width = Math.Max(1, width);
      height = Math.Max(1, height);
    }

    if (width <= 0 || height <= 0)
    {
      return null;
    }

    return new ScreenBounds(normalizedLeft, normalizedTop, width, height);
  }

  private static BrowserSurfaceKind ClassifyBrowserSurface(
    string? processName,
    string? automationId,
    string? automationName,
    string? automationClass,
    string? controlTypeName,
    bool supportsWritableValuePattern,
    bool supportsTextPattern,
    bool isKeyboardFocusable,
    bool hasVisibleBounds,
    bool isAccessibilityPlaceholder)
  {
    if (!BrowserProcessNames.Contains(processName ?? string.Empty))
    {
      return BrowserSurfaceKind.None;
    }

    if (isAccessibilityPlaceholder)
    {
      return BrowserSurfaceKind.NonEditableSurface;
    }

    if (IsUrlBarElement(automationId, automationName, automationClass, controlTypeName))
    {
      return BrowserSurfaceKind.UrlBar;
    }

    bool editableCandidate = supportsWritableValuePattern
      || (supportsTextPattern
          && isKeyboardFocusable
          && (string.Equals(controlTypeName, "ControlType.Document", StringComparison.Ordinal)
              || string.Equals(controlTypeName, "ControlType.Edit", StringComparison.Ordinal)))
      || (isKeyboardFocusable
          && hasVisibleBounds
          && (string.Equals(controlTypeName, "ControlType.Document", StringComparison.Ordinal)
              || string.Equals(controlTypeName, "ControlType.Edit", StringComparison.Ordinal)));
    if (editableCandidate)
    {
      return BrowserSurfaceKind.EditablePage;
    }

    return isKeyboardFocusable
      ? BrowserSurfaceKind.UnknownBrowserSurface
      : BrowserSurfaceKind.NonEditableSurface;
  }

  private static bool IsChromiumHost(
    string? processName,
    string? foregroundWindowClass,
    string? frameworkId)
  {
    return BrowserProcessNames.Contains(processName ?? string.Empty)
           || ChromiumHostWindowClasses.Contains(foregroundWindowClass ?? string.Empty)
           || string.Equals(frameworkId, "Chrome", StringComparison.OrdinalIgnoreCase)
           || string.Equals(frameworkId, "Chromium", StringComparison.OrdinalIgnoreCase);
  }

  private static (WindowEditability Editability, string? Reason) ClassifyEditability(
    string? processName,
    string? nativeFocusedControlClass,
    string? automationClass,
    string? controlTypeName,
    BrowserSurfaceKind browserSurfaceKind,
    bool isAccessibilityPlaceholder,
    bool isPassword,
    bool isReadOnly,
    bool supportsWritableValuePattern,
    bool supportsTextPattern,
    bool isKeyboardFocusable)
  {
    if (isPassword)
    {
      return (WindowEditability.NonEditable, "uia-password");
    }

    if (isAccessibilityPlaceholder)
    {
      return (WindowEditability.NonEditable, "uia-accessibility-placeholder");
    }

    if (browserSurfaceKind == BrowserSurfaceKind.NonEditableSurface)
    {
      return (WindowEditability.NonEditable, "browser-non-editable-surface");
    }

    if (supportsWritableValuePattern)
    {
      return (WindowEditability.Editable, "uia-valuepattern");
    }

    if (!isReadOnly
        && supportsTextPattern
        && isKeyboardFocusable
        && (string.Equals(controlTypeName, "ControlType.Document", StringComparison.Ordinal)
            || string.Equals(controlTypeName, "ControlType.Edit", StringComparison.Ordinal)))
    {
      return (WindowEditability.Editable, "uia-textpattern");
    }

    if (browserSurfaceKind == BrowserSurfaceKind.UrlBar || browserSurfaceKind == BrowserSurfaceKind.EditablePage)
    {
      return (WindowEditability.Editable, "browser-editable-surface");
    }

    if (IsLikelyNativeEditableControl(nativeFocusedControlClass)
        || IsLikelyNativeEditableControl(automationClass))
    {
      return (WindowEditability.Editable, "native-edit-class");
    }

    if (BrowserProcessNames.Contains(processName ?? string.Empty))
    {
      return (WindowEditability.Unknown, "browser-focus-unclassified");
    }

    return (WindowEditability.Unknown, null);
  }

  private static bool IsUrlBarElement(
    string? automationId,
    string? automationName,
    string? automationClass,
    string? controlTypeName)
  {
    if (!string.Equals(controlTypeName, "ControlType.Edit", StringComparison.Ordinal))
    {
      return false;
    }

    return ContainsAny(automationId, "url", "address", "omnibox")
           || ContainsAny(automationName, "address and search bar", "address bar", "search or enter address")
           || ContainsAny(automationClass, "omnibox");
  }

  private static bool IsLikelyNativeEditableControl(string? className)
  {
    return ContainsAny(className, "edit", "richedit", "scintilla");
  }

  private static bool ContainsAny(string? value, params string[] tokens)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return false;
    }

    foreach (string token in tokens)
    {
      if (value.Contains(token, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }

  private static T? SafeGet<T>(Func<T> accessor, T? fallbackValue = default)
  {
    try
    {
      return accessor();
    }
    catch (ElementNotAvailableException)
    {
      return fallbackValue;
    }
    catch (COMException)
    {
      return fallbackValue;
    }
    catch (InvalidOperationException)
    {
      return fallbackValue;
    }
  }

  [DllImport("user32.dll", EntryPoint = "GetForegroundWindow", SetLastError = true)]
  private static extern IntPtr GetForegroundWindowNative();

  [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId", SetLastError = true)]
  private static extern uint GetWindowThreadProcessIdNative(IntPtr windowHandle, out uint processId);

  [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern int GetClassNameNative(IntPtr windowHandle, StringBuilder className, int maxCount);

  [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern int GetWindowTextLengthNative(IntPtr windowHandle);

  [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
  private static extern int GetWindowTextNative(IntPtr windowHandle, StringBuilder text, int maxCount);

  [DllImport("user32.dll", EntryPoint = "GetGUIThreadInfo", SetLastError = true)]
  private static extern bool GetGUIThreadInfoNative(uint threadId, ref GUITHREADINFO threadInfo);

  [DllImport("user32.dll", EntryPoint = "ClientToScreen", SetLastError = true)]
  private static extern bool ClientToScreenNative(IntPtr windowHandle, ref POINT point);

  [DllImport("user32.dll", EntryPoint = "GetWindowRect", SetLastError = true)]
  private static extern bool GetWindowRectNative(IntPtr windowHandle, out RECT rect);

  [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
  private static extern IntPtr GetWindowLongPtrNative(IntPtr windowHandle, int index);

  [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
  private static extern int GetWindowLongNative(IntPtr windowHandle, int index);

  [DllImport("user32.dll", EntryPoint = "IsWindow", SetLastError = true)]
  private static extern bool IsWindowNative(IntPtr windowHandle);

  [DllImport("user32.dll", EntryPoint = "IsIconic", SetLastError = true)]
  private static extern bool IsIconicNative(IntPtr windowHandle);

  [DllImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
  private static extern bool ShowWindowNative(IntPtr windowHandle, int command);

  [DllImport("user32.dll", EntryPoint = "SetForegroundWindow", SetLastError = true)]
  private static extern bool SetForegroundWindowNative(IntPtr windowHandle);

  [DllImport("user32.dll", EntryPoint = "BringWindowToTop", SetLastError = true)]
  private static extern bool BringWindowToTopNative(IntPtr windowHandle);

  [DllImport("user32.dll", EntryPoint = "AttachThreadInput", SetLastError = true)]
  private static extern bool AttachThreadInputNative(uint attachThreadId, uint attachToThreadId, [MarshalAs(UnmanagedType.Bool)] bool attach);

  [StructLayout(LayoutKind.Sequential)]
  private struct RECT
  {
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct POINT
  {
    public int X;
    public int Y;
  }

  [StructLayout(LayoutKind.Sequential)]
  private struct GUITHREADINFO
  {
    public int cbSize;
    public int flags;
    public IntPtr hwndActive;
    public IntPtr hwndFocus;
    public IntPtr hwndCapture;
    public IntPtr hwndMenuOwner;
    public IntPtr hwndMoveSize;
    public IntPtr hwndCaret;
    public RECT rcCaret;
  }

  private sealed record AutomationFocusSnapshot(
    string? ClassName,
    string? ControlTypeName,
    ScreenBounds? Bounds,
    string? AutomationId,
    string? AutomationName,
    string? FrameworkId,
    WindowEditability Editability,
    string? EditabilityReason,
    string? VerificationMode,
    BrowserSurfaceKind BrowserSurfaceKind,
    string? ObservedText,
    bool IsReadOnly,
    bool IsPasswordProtected,
    string? RawAutomationId,
    string? RawAutomationName,
    string? RawClassName,
    string? RawControlTypeName,
    ScreenBounds? RawBounds,
    string ResolutionSource,
    string? RuntimeId = null);

  private static long? TryGetProcessStartTicks(uint processId)
  {
    try
    {
      using Process process = Process.GetProcessById(checked((int)processId));
      return process.StartTime.ToUniversalTime().Ticks;
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
    {
      return null;
    }
  }
}
