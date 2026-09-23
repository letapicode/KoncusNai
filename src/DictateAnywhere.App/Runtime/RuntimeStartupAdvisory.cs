using System;
using System.Collections.Generic;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.App.Runtime;

internal static class RuntimeStartupAdvisory
{
  internal static string DescribeSettings(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    TranscriptionModelSelection transcriptionSelection = settings.GetConfiguredTranscriptionSelection();
    return $"Runtime startup settings: hotkey='{HotkeyFormatter.ToDisplayString(settings.Hotkey)}', recordingMode={settings.RecordingMode}, transcriptionProvider={transcriptionSelection.ProviderId}, transcriptionModel='{transcriptionSelection.ModelId}', automaticPunctuation={settings.EnableAutomaticPunctuation}, preferredInsertionMethod={settings.PreferredInsertionMethod}, overlayEnabled={settings.OverlayEnabled}, caretIndicatorEnabled={settings.CaretIndicatorEnabled}, fallbackToCornerOverlay={settings.FallbackToCornerOverlay}, recordingFeedbackVisible={IsRecordingFeedbackVisible(settings)}.";
  }

  internal static string? DescribeOverlayVisibilityRisk(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    if (!settings.OverlayEnabled)
    {
      return "Overlay is disabled, so recording and transcribing indicators will not be shown.";
    }

    if (!settings.CaretIndicatorEnabled && !settings.FallbackToCornerOverlay)
    {
      return "Caret indicator and corner fallback are both disabled, so recording and transcribing indicators will not be shown.";
    }

    return null;
  }

  internal static RuntimeStartupNotice? BuildNotice(
    AppSettings settings,
    GlobalHotkeyRegistrationOutcome? hotkeyOutcome)
  {
    ArgumentNullException.ThrowIfNull(settings);

    string? overlayMessage = DescribeOverlayVisibilityRisk(settings);
    string? hotkeyMessage = hotkeyOutcome is not null && hotkeyOutcome.UsedFallbackBinding
      ? hotkeyOutcome.StatusMessage
      : null;

    List<string> messages = new();
    if (!string.IsNullOrWhiteSpace(hotkeyMessage))
    {
      messages.Add(hotkeyMessage);
    }

    if (!string.IsNullOrWhiteSpace(overlayMessage))
    {
      messages.Add(overlayMessage);
    }

    return messages.Count switch
    {
      0 => null,
      1 when hotkeyMessage is not null => new RuntimeStartupNotice("Global Hotkey Fallback Active", hotkeyMessage),
      1 => new RuntimeStartupNotice("Overlay Feedback Disabled", overlayMessage!),
      _ => new RuntimeStartupNotice("Runtime Attention Required", string.Join(" ", messages)),
    };
  }

  internal static bool IsRecordingFeedbackVisible(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    if (!settings.OverlayEnabled)
    {
      return false;
    }

    return settings.CaretIndicatorEnabled || settings.FallbackToCornerOverlay;
  }
}
