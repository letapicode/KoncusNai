using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Insertion;

public sealed record class WindowFocusContext
{
  public nint ForegroundWindowHandle { get; init; }

  public uint ForegroundProcessId { get; init; }

  public long? ForegroundProcessStartTicks { get; init; }

  public string? FocusedAutomationRuntimeId { get; init; }

  public string? ForegroundProcessName { get; init; }

  public string? ForegroundWindowClassName { get; init; }

  public string? FocusedControlClassName { get; init; }

  public bool FocusedControlPasswordProtected { get; init; }

  public string? ForegroundWindowTitle { get; init; }

  public nint FocusedControlHandle { get; init; }

  public nint CaretWindowHandle { get; init; }

  public ScreenBounds? CaretBounds { get; init; }

  public ScreenBounds? FocusedElementBounds { get; init; }

  public ScreenBounds? RawFocusedElementBounds { get; init; }

  public ScreenBounds? ForegroundWindowBounds { get; init; }

  public WindowEditability Editability { get; init; } = WindowEditability.Unknown;

  public string? EditabilityReason { get; init; }

  public string? VerificationMode { get; init; }

  public BrowserSurfaceKind BrowserSurfaceKind { get; init; } = BrowserSurfaceKind.None;

  public string? FocusedAutomationId { get; init; }

  public string? FocusedAutomationName { get; init; }

  public string? FocusedAutomationClassName { get; init; }

  public string? FocusedAutomationControlType { get; init; }

  public string? FocusedAutomationFrameworkId { get; init; }

  public string? RawFocusedAutomationId { get; init; }

  public string? RawFocusedAutomationName { get; init; }

  public string? RawFocusedAutomationClassName { get; init; }

  public string? RawFocusedAutomationControlType { get; init; }

  public string? FocusResolutionSource { get; init; }

  public bool FocusedControlReadOnly { get; init; }

  public string? ObservedText { get; init; }

  public bool IsEditable => Editability == WindowEditability.Editable;

  public bool IsNonEditable => Editability == WindowEditability.NonEditable;

  public bool CanVerifyText => !string.IsNullOrWhiteSpace(VerificationMode);

  public bool CaretOwnedByFocusedControl =>
    FocusedControlHandle != 0 && CaretWindowHandle != 0 && FocusedControlHandle == CaretWindowHandle;

  public bool HasCaretBounds => CaretBounds.HasValue && !CaretBounds.Value.IsEmpty;

  public bool HasFocusedElementBounds => FocusedElementBounds.HasValue && !FocusedElementBounds.Value.IsEmpty;

  public bool HasForegroundWindowBounds => ForegroundWindowBounds.HasValue && !ForegroundWindowBounds.Value.IsEmpty;

  public static WindowFocusContext Empty { get; } = new();
}
