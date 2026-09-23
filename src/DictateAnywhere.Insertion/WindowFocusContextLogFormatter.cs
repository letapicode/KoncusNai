using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Insertion;

public static class WindowFocusContextLogFormatter
{
  public static string Describe(WindowFocusContext focusContext)
  {
    ArgumentNullException.ThrowIfNull(focusContext);

    string processName = string.IsNullOrWhiteSpace(focusContext.ForegroundProcessName)
      ? "<unknown>"
      : focusContext.ForegroundProcessName.Trim();
    string windowTitle = string.IsNullOrWhiteSpace(focusContext.ForegroundWindowTitle)
      ? "<unknown>"
      : Truncate(focusContext.ForegroundWindowTitle.Trim(), 120);
    string windowClass = string.IsNullOrWhiteSpace(focusContext.ForegroundWindowClassName)
      ? "<unknown>"
      : focusContext.ForegroundWindowClassName.Trim();
    string controlClass = string.IsNullOrWhiteSpace(focusContext.FocusedControlClassName)
      ? "<unknown>"
      : focusContext.FocusedControlClassName.Trim();
    string handle = FormatHandle(focusContext.ForegroundWindowHandle);
    string focusedHandle = FormatHandle(focusContext.FocusedControlHandle);
    string caretHandle = FormatHandle(focusContext.CaretWindowHandle);
    string rawAutomationClass = string.IsNullOrWhiteSpace(focusContext.RawFocusedAutomationClassName)
      ? "<unknown>"
      : focusContext.RawFocusedAutomationClassName.Trim();
    string rawAutomationType = string.IsNullOrWhiteSpace(focusContext.RawFocusedAutomationControlType)
      ? "<unknown>"
      : focusContext.RawFocusedAutomationControlType.Trim();
    string rawAutomationId = string.IsNullOrWhiteSpace(focusContext.RawFocusedAutomationId)
      ? "<none>"
      : Truncate(focusContext.RawFocusedAutomationId.Trim(), 60);
    string rawAutomationName = string.IsNullOrWhiteSpace(focusContext.RawFocusedAutomationName)
      ? "<none>"
      : Truncate(focusContext.RawFocusedAutomationName.Trim(), 80);
    string resolvedAutomationClass = string.IsNullOrWhiteSpace(focusContext.FocusedAutomationClassName)
      ? "<unknown>"
      : focusContext.FocusedAutomationClassName.Trim();
    string resolvedAutomationType = string.IsNullOrWhiteSpace(focusContext.FocusedAutomationControlType)
      ? "<unknown>"
      : focusContext.FocusedAutomationControlType.Trim();
    string resolvedAutomationId = string.IsNullOrWhiteSpace(focusContext.FocusedAutomationId)
      ? "<none>"
      : Truncate(focusContext.FocusedAutomationId.Trim(), 60);
    string resolvedAutomationName = string.IsNullOrWhiteSpace(focusContext.FocusedAutomationName)
      ? "<none>"
      : Truncate(focusContext.FocusedAutomationName.Trim(), 80);
    string caretBounds = FormatBounds(focusContext.CaretBounds);
    string rawBounds = FormatBounds(focusContext.RawFocusedElementBounds);
    string elementBounds = FormatBounds(focusContext.FocusedElementBounds);
    string windowBounds = FormatBounds(focusContext.ForegroundWindowBounds);
    string focusResolution = string.IsNullOrWhiteSpace(focusContext.FocusResolutionSource)
      ? "<none>"
      : focusContext.FocusResolutionSource.Trim();

    return $"handle={handle}, process='{processName}', title='{windowTitle}', windowClass='{windowClass}', controlClass='{controlClass}', focusedHandle={focusedHandle}, caretHandle={caretHandle}, caretOwned={focusContext.CaretOwnedByFocusedControl}, caretBounds={caretBounds}, rawBounds={rawBounds}, elementBounds={elementBounds}, windowBounds={windowBounds}, secure={focusContext.FocusedControlPasswordProtected}, editability={focusContext.Editability}, reason='{focusContext.EditabilityReason ?? "<none>"}', verification='{focusContext.VerificationMode ?? "<none>"}', browserSurface={focusContext.BrowserSurfaceKind}, focusResolution='{focusResolution}', rawAutomationClass='{rawAutomationClass}', rawAutomationType='{rawAutomationType}', rawAutomationId='{rawAutomationId}', rawAutomationName='{rawAutomationName}', resolvedAutomationClass='{resolvedAutomationClass}', resolvedAutomationType='{resolvedAutomationType}', resolvedAutomationId='{resolvedAutomationId}', resolvedAutomationName='{resolvedAutomationName}'";
  }

  private static string FormatHandle(nint handle)
  {
    return handle == 0
      ? "0x0"
      : $"0x{(long)handle:X}";
  }

  private static string FormatBounds(ScreenBounds? bounds)
  {
    return bounds.HasValue && !bounds.Value.IsEmpty
      ? bounds.Value.ToString()
      : "<none>";
  }

  private static string Truncate(string value, int maxLength)
  {
    if (value.Length <= maxLength)
    {
      return value;
    }

    return value[..(maxLength - 3)] + "...";
  }
}
