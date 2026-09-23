using System;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;
using DictateAnywhere.Insertion;

namespace DictateAnywhere.App.Runtime;

internal sealed record GlobalHotkeyTriggerDiagnostic(
  string FiredMessage,
  GlobalHotkeyTriggerDiagnosticKind SecondaryKind,
  string? SecondaryMessage);

internal static class GlobalHotkeyTriggerDiagnostics
{
  private static readonly string[] BrowserChromeTokens =
  [
    "menu",
    "appmenu",
    "browserappmenubutton",
    "customize and control",
    "settings and more",
    "more options",
    "toolbar",
    "tabstrip",
    "system menu",
  ];

  private static readonly string[] EditableSurfaceTokens =
  [
    "search",
    "query",
    "prompt",
    "message",
    "compose",
    "textarea",
    "editor",
  ];

  internal static void LogRegistrationOutcome(
    GlobalHotkeyRegistrationOutcome outcome,
    IDiagnostics diagnostics)
  {
    ArgumentNullException.ThrowIfNull(outcome);
    ArgumentNullException.ThrowIfNull(diagnostics);

    if (outcome.Success && !outcome.UsedFallbackBinding)
    {
      diagnostics.Info(outcome.DiagnosticsMessage);
      return;
    }

    if (outcome.Success)
    {
      diagnostics.Warning(outcome.DiagnosticsMessage);
      return;
    }

    diagnostics.Warning(outcome.DiagnosticsMessage);
  }

  internal static GlobalHotkeyTriggerDiagnostic AnalyzeTrigger(
    HotkeyBinding binding,
    WindowFocusContext focusContext)
  {
    ArgumentNullException.ThrowIfNull(focusContext);

    string bindingDisplay = HotkeyFormatter.ToDisplayString(binding);
    string focusDescription = WindowFocusContextLogFormatter.Describe(focusContext);
    string firedMessage = $"Global hotkey fired: binding='{bindingDisplay}', target={focusDescription}.";

    if (IsLikelyTriggerLeak(focusContext, out string leakReason))
    {
      return new GlobalHotkeyTriggerDiagnostic(
        firedMessage,
        GlobalHotkeyTriggerDiagnosticKind.TriggerKeysLeakedToApp,
        $"Trigger keys leaked to app before recording start: binding='{bindingDisplay}', reason='{leakReason}', target={focusDescription}.");
    }

    if (IsLikelyFocusDrift(focusContext, out string driftReason))
    {
      return new GlobalHotkeyTriggerDiagnostic(
        firedMessage,
        GlobalHotkeyTriggerDiagnosticKind.FocusChangedBeforeRecordingStart,
        $"Focus changed before recording start: binding='{bindingDisplay}', reason='{driftReason}', target={focusDescription}.");
    }

    return new GlobalHotkeyTriggerDiagnostic(
      firedMessage,
      GlobalHotkeyTriggerDiagnosticKind.None,
      SecondaryMessage: null);
  }

  private static bool IsLikelyTriggerLeak(WindowFocusContext focusContext, out string reason)
  {
    if (ContainsAny(
          focusContext.FocusedAutomationId,
          focusContext.FocusedAutomationName,
          focusContext.FocusedAutomationClassName,
          focusContext.FocusedAutomationControlType,
          focusContext.RawFocusedAutomationId,
          focusContext.RawFocusedAutomationName,
          focusContext.RawFocusedAutomationClassName,
          focusContext.RawFocusedAutomationControlType,
          BrowserChromeTokens))
    {
      reason = "browser-chrome-menu-surface";
      return true;
    }

    if (focusContext.BrowserSurfaceKind == BrowserSurfaceKind.NonEditableSurface
        && string.Equals(focusContext.FocusedAutomationControlType, "ControlType.Button", StringComparison.Ordinal))
    {
      reason = "browser-noneditable-button-surface";
      return true;
    }

    reason = string.Empty;
    return false;
  }

  private static bool IsLikelyFocusDrift(WindowFocusContext focusContext, out string reason)
  {
    if (focusContext.BrowserSurfaceKind is BrowserSurfaceKind.NonEditableSurface or BrowserSurfaceKind.UnknownBrowserSurface)
    {
      bool rawLooksEditable = LooksEditable(
        focusContext.RawFocusedAutomationId,
        focusContext.RawFocusedAutomationName,
        focusContext.RawFocusedAutomationClassName,
        focusContext.RawFocusedAutomationControlType);
      bool resolvedLooksEditable = LooksEditable(
        focusContext.FocusedAutomationId,
        focusContext.FocusedAutomationName,
        focusContext.FocusedAutomationClassName,
        focusContext.FocusedAutomationControlType);

      if (rawLooksEditable && !resolvedLooksEditable)
      {
        reason = "editable-raw-focus-promoted-to-noneditable-surface";
        return true;
      }

      if (!focusContext.IsEditable && !resolvedLooksEditable)
      {
        reason = "browser-surface-not-editable-at-hotkey-fire";
        return true;
      }
    }

    reason = string.Empty;
    return false;
  }

  private static bool LooksEditable(
    string? automationId,
    string? automationName,
    string? automationClassName,
    string? controlTypeName)
  {
    if (string.Equals(controlTypeName, "ControlType.Edit", StringComparison.Ordinal)
        || string.Equals(controlTypeName, "ControlType.Document", StringComparison.Ordinal))
    {
      return true;
    }

    return ContainsAny(automationId, automationName, automationClassName, EditableSurfaceTokens);
  }

  private static bool ContainsAny(string? value1, string? value2, string? value3, params string[] tokens)
  {
    return ContainsAny(value1, tokens)
           || ContainsAny(value2, tokens)
           || ContainsAny(value3, tokens);
  }

  private static bool ContainsAny(
    string? value1,
    string? value2,
    string? value3,
    string? value4,
    string? value5,
    string? value6,
    string? value7,
    string? value8,
    params string[] tokens)
  {
    return ContainsAny(value1, tokens)
           || ContainsAny(value2, tokens)
           || ContainsAny(value3, tokens)
           || ContainsAny(value4, tokens)
           || ContainsAny(value5, tokens)
           || ContainsAny(value6, tokens)
           || ContainsAny(value7, tokens)
           || ContainsAny(value8, tokens);
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
}
