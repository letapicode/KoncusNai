using System;
using DictateAnywhere.App.Experience;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.Workbench;

public static class WorkbenchSettingsPolicy
{
  public static HotkeyBinding PreferredHotkey => AppSettings.WorkbenchDefaultHotkey;

  public static HotkeyBinding FallbackHotkey => AppSettings.AutomaticFallbackHotkey;

  public static AppSettings Normalize(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    AppSettings minimal = CurrentSettingsPolicy.Normalize(settings);
    HotkeyBinding normalizedHotkey = minimal.Hotkey == AppSettings.AutomaticFallbackHotkey
      ? PreferredHotkey
      : minimal.Hotkey;

    if (normalizedHotkey.Modifiers == HotkeyModifiers.None)
    {
      normalizedHotkey = PreferredHotkey;
    }

    return minimal with
    {
      RecordingMode = RecordingMode.ToggleToTalk,
      Hotkey = normalizedHotkey,
    };
  }

  public static bool ShouldAutoFallback(HotkeyBinding requestedBinding, HotkeyRegistrationResult registrationResult)
  {
    ArgumentNullException.ThrowIfNull(registrationResult);
    return !registrationResult.Success && requestedBinding == PreferredHotkey;
  }
}
