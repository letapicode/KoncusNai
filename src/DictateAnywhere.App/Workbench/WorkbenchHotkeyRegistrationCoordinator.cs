using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Workbench;

public sealed record WorkbenchHotkeyRegistrationOutcome(
  bool Success,
  HotkeyBinding ActiveBinding,
  bool UsedFallbackBinding,
  string StatusMessage,
  string DiagnosticsMessage);

public sealed class WorkbenchHotkeyRegistrationCoordinator
{
  private readonly Func<HotkeyBinding, CancellationToken, Task<HotkeyRegistrationResult>> registerAsync;

  public WorkbenchHotkeyRegistrationCoordinator(
    Func<HotkeyBinding, CancellationToken, Task<HotkeyRegistrationResult>> registerAsync)
  {
    this.registerAsync = registerAsync ?? throw new ArgumentNullException(nameof(registerAsync));
  }

  public async Task<WorkbenchHotkeyRegistrationOutcome> RegisterWithFallbackAsync(
    HotkeyBinding requestedBinding,
    CancellationToken cancellationToken = default)
  {
    HotkeyRegistrationResult primaryResult = await registerAsync(requestedBinding, cancellationToken).ConfigureAwait(false);
    if (primaryResult.Success)
    {
      string registered = HotkeyFormatter.ToDisplayString(requestedBinding);
      return new WorkbenchHotkeyRegistrationOutcome(
        Success: true,
        ActiveBinding: requestedBinding,
        UsedFallbackBinding: false,
        StatusMessage: $"Hotkey active: {registered} (toggle start/stop).",
        DiagnosticsMessage: $"Workbench hotkey registered: {registered}");
    }

    string primaryReason = BuildReason(primaryResult);
    if (!WorkbenchSettingsPolicy.ShouldAutoFallback(requestedBinding, primaryResult))
    {
      string requested = HotkeyFormatter.ToDisplayString(requestedBinding);
      return new WorkbenchHotkeyRegistrationOutcome(
        Success: false,
        ActiveBinding: requestedBinding,
        UsedFallbackBinding: false,
        StatusMessage: $"Hotkey unavailable: {primaryReason}. Buttons are still available.",
        DiagnosticsMessage: $"Workbench hotkey registration failed for '{requested}': {primaryReason}");
    }

    HotkeyBinding fallbackBinding = WorkbenchSettingsPolicy.FallbackHotkey;
    HotkeyRegistrationResult fallbackResult = await registerAsync(fallbackBinding, cancellationToken).ConfigureAwait(false);
    if (fallbackResult.Success)
    {
      string fallback = HotkeyFormatter.ToDisplayString(fallbackBinding);
      return new WorkbenchHotkeyRegistrationOutcome(
        Success: true,
        ActiveBinding: fallbackBinding,
        UsedFallbackBinding: true,
        StatusMessage: $"Alt + Space unavailable ({primaryReason}). Fallback active: {fallback}.",
        DiagnosticsMessage: $"Workbench hotkey fallback activated after preferred hotkey registration failed: {primaryReason}. Fallback='{fallback}'");
    }

    string fallbackReason = BuildReason(fallbackResult);
    string fallbackDisplay = HotkeyFormatter.ToDisplayString(fallbackBinding);
    return new WorkbenchHotkeyRegistrationOutcome(
      Success: false,
      ActiveBinding: requestedBinding,
      UsedFallbackBinding: false,
      StatusMessage: $"Alt + Space unavailable ({primaryReason}). Fallback '{fallbackDisplay}' also failed ({fallbackReason}). Buttons are still available.",
      DiagnosticsMessage: $"Workbench hotkey registration failed for Alt + Space and fallback '{fallbackDisplay}'. primary='{primaryReason}' fallback='{fallbackReason}'");
  }

  private static string BuildReason(HotkeyRegistrationResult result)
  {
    return string.IsNullOrWhiteSpace(result.ErrorMessage)
      ? "Unknown hotkey registration error"
      : result.ErrorMessage.Trim();
  }
}
