using System;
using System.Security.Cryptography;
using System.Text;

namespace DictateAnywhere.Insertion;

/// <summary>A text-free identity carried across focus checks and the helper boundary.</summary>
public sealed record InsertionTargetIdentity(
  long WindowHandle,
  uint ProcessId,
  long? ProcessStartTicks,
  string? AutomationRuntimeId,
  long ControlHandle,
  string? AutomationIdHash)
{
  public static InsertionTargetIdentity FromContext(WindowFocusContext context) => new(
    context.ForegroundWindowHandle.ToInt64(), context.ForegroundProcessId,
    context.ForegroundProcessStartTicks, context.FocusedAutomationRuntimeId,
    context.FocusedControlHandle.ToInt64(), HashAutomationId(context.FocusedAutomationId));

  public bool Matches(WindowFocusContext context)
  {
    if (WindowHandle == 0 || ProcessId == 0 || !ProcessStartTicks.HasValue
        || WindowHandle != context.ForegroundWindowHandle.ToInt64()
        || ProcessId != context.ForegroundProcessId
        || ProcessStartTicks != context.ForegroundProcessStartTicks)
    {
      return false;
    }

    if (!string.IsNullOrEmpty(AutomationRuntimeId) || !string.IsNullOrEmpty(context.FocusedAutomationRuntimeId))
    {
      return !string.IsNullOrEmpty(AutomationRuntimeId)
        && string.Equals(AutomationRuntimeId, context.FocusedAutomationRuntimeId, StringComparison.Ordinal);
    }

    // Browser controls share a native host HWND. A missing automation identity
    // cannot establish that the same page editor still owns keyboard focus.
    return context.BrowserSurfaceKind == BrowserSurfaceKind.None
      && ControlHandle != 0 && ControlHandle == context.FocusedControlHandle.ToInt64()
      && string.Equals(AutomationIdHash, HashAutomationId(context.FocusedAutomationId), StringComparison.Ordinal);
  }

  private static string? HashAutomationId(string? value) => string.IsNullOrEmpty(value)
    ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
