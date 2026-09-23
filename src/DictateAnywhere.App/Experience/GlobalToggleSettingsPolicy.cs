using System;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.Experience;

public static class GlobalToggleSettingsPolicy
{
  public static HotkeyBinding PreferredHotkey => AppSettings.GlobalToggleDefaultHotkey;

  public static HotkeyBinding FallbackHotkey => AppSettings.AutomaticFallbackHotkey;

  public static AppSettings ApplyPreset(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    AppSettings minimal = CurrentSettingsPolicy.Normalize(settings);
    return minimal with
    {
      RecordingMode = RecordingMode.ToggleToTalk,
      Hotkey = PreferredHotkey,
    };
  }

  public static bool ShouldAutoFallback(HotkeyBinding requestedBinding, HotkeyRegistrationResult registrationResult)
  {
    ArgumentNullException.ThrowIfNull(registrationResult);
    return !registrationResult.Success && requestedBinding == PreferredHotkey;
  }
}
