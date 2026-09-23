using System;
using System.Collections.Generic;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

internal static class RuntimeSettingsRestartPolicy
{
  public static bool RequiresRestart(AppSettings? active, AppSettings updated)
  {
    ArgumentNullException.ThrowIfNull(updated);

    if (active is null)
    {
      return true;
    }

    return active.Hotkey != updated.Hotkey
           || active.RecordingMode != updated.RecordingMode
           || !EqualsOrdinal(active.TranscriptionProviderId, updated.TranscriptionProviderId)
           || !EqualsOrdinal(active.TranscriptionModelId, updated.TranscriptionModelId)
           || !EqualsOrdinal(active.TranscriptionLanguage, updated.TranscriptionLanguage)
           || active.EnableAutomaticPunctuation != updated.EnableAutomaticPunctuation
           || active.PreferredInsertionMethod != updated.PreferredInsertionMethod
           || active.RestoreClipboard != updated.RestoreClipboard
           || active.OverlayEnabled != updated.OverlayEnabled
           || active.CaretIndicatorEnabled != updated.CaretIndicatorEnabled
           || active.FallbackToCornerOverlay != updated.FallbackToCornerOverlay
           || !EqualsOrdinal(active.PreferredAudioInputDeviceId, updated.PreferredAudioInputDeviceId)
           || active.UndoHotkey != updated.UndoHotkey
           || active.EnableSecureFieldDetection != updated.EnableSecureFieldDetection
           || !SequenceEqual(active.InsertionBlockedProcessNames, updated.InsertionBlockedProcessNames, StringComparer.OrdinalIgnoreCase)
           || active.EnableElevatedInsertion != updated.EnableElevatedInsertion
           || active.EnableDictationCommands != updated.EnableDictationCommands;
  }

  private static bool SequenceEqual<T>(
    IReadOnlyList<T>? left,
    IReadOnlyList<T>? right,
    IEqualityComparer<T> comparer)
  {
    IReadOnlyList<T> leftItems = left ?? Array.Empty<T>();
    IReadOnlyList<T> rightItems = right ?? Array.Empty<T>();
    return leftItems.Count == rightItems.Count && leftItems.SequenceEqual(rightItems, comparer);
  }

  private static bool EqualsOrdinal(string? left, string? right) =>
    string.Equals(left, right, StringComparison.Ordinal);
}
