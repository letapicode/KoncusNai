using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Experience;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Runtime;

public sealed record GlobalHotkeyRegistrationOutcome(
  bool Success,
  HotkeyBinding ActiveBinding,
  bool UsedFallbackBinding,
  string StatusMessage,
  string DiagnosticsMessage);

public sealed class GlobalHotkeyRegistrationCoordinator
{
  private readonly Func<HotkeyBinding, CancellationToken, Task<HotkeyRegistrationResult>> registerAsync;

  public GlobalHotkeyRegistrationCoordinator(
    Func<HotkeyBinding, CancellationToken, Task<HotkeyRegistrationResult>> registerAsync)
  {
    this.registerAsync = registerAsync ?? throw new ArgumentNullException(nameof(registerAsync));
  }

  public async Task<GlobalHotkeyRegistrationOutcome> RegisterWithFallbackAsync(
    HotkeyBinding requestedBinding,
    CancellationToken cancellationToken = default)
  {
    HotkeyRegistrationResult primaryResult = await registerAsync(requestedBinding, cancellationToken).ConfigureAwait(false);
    if (primaryResult.Success)
    {
      string registered = HotkeyFormatter.ToDisplayString(requestedBinding);
      return new GlobalHotkeyRegistrationOutcome(
        Success: true,
        ActiveBinding: requestedBinding,
        UsedFallbackBinding: false,
        StatusMessage: $"Global hotkey active: {registered} (toggle start/stop).",
        DiagnosticsMessage: $"Global hotkey registered: {registered}");
    }

    string primaryReason = BuildReason(primaryResult);
    if (!GlobalToggleSettingsPolicy.ShouldAutoFallback(requestedBinding, primaryResult))
    {
      return new GlobalHotkeyRegistrationOutcome(
        Success: false,
        ActiveBinding: requestedBinding,
        UsedFallbackBinding: false,
        StatusMessage: $"Global hotkey unavailable: {primaryReason}",
        DiagnosticsMessage: $"Global hotkey registration failed for '{HotkeyFormatter.ToDisplayString(requestedBinding)}': {primaryReason}");
    }

    HotkeyBinding fallbackBinding = GlobalToggleSettingsPolicy.FallbackHotkey;
    HotkeyRegistrationResult fallbackResult = await registerAsync(fallbackBinding, cancellationToken).ConfigureAwait(false);
    if (fallbackResult.Success)
    {
      string fallback = HotkeyFormatter.ToDisplayString(fallbackBinding);
      return new GlobalHotkeyRegistrationOutcome(
        Success: true,
        ActiveBinding: fallbackBinding,
        UsedFallbackBinding: true,
        StatusMessage: $"Alt + Space unavailable ({primaryReason}). Fallback active: {fallback} (toggle start/stop).",
        DiagnosticsMessage: $"Global hotkey fallback activated after preferred hotkey registration failed: {primaryReason}. Fallback='{fallback}'");
    }

    string fallbackReason = BuildReason(fallbackResult);
    string fallbackDisplay = HotkeyFormatter.ToDisplayString(fallbackBinding);
    return new GlobalHotkeyRegistrationOutcome(
      Success: false,
      ActiveBinding: fallbackBinding,
      UsedFallbackBinding: true,
      StatusMessage: $"Alt + Space unavailable ({primaryReason}). Fallback '{fallbackDisplay}' also failed ({fallbackReason}).",
      DiagnosticsMessage: $"Global hotkey registration failed for Alt + Space and fallback '{fallbackDisplay}'. primary='{primaryReason}' fallback='{fallbackReason}'");
  }

  private static string BuildReason(HotkeyRegistrationResult result)
  {
    return string.IsNullOrWhiteSpace(result.ErrorMessage)
      ? "Windows rejected the hotkey registration."
      : result.ErrorMessage;
  }
}
