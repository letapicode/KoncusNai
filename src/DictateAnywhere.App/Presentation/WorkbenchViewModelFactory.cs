using System;
using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Presentation;

public static class WorkbenchViewModelFactory
{
  public static WorkbenchViewModel Create(
    WorkbenchSessionState sessionState,
    string sessionStatusText,
    string hotkeyStatusText)
  {
    ArgumentNullException.ThrowIfNull(sessionStatusText);
    ArgumentNullException.ThrowIfNull(hotkeyStatusText);

    return new WorkbenchViewModel(
      SessionStatusText: sessionStatusText,
      SessionStatusKind: ResolveSessionStatusKind(sessionState, sessionStatusText),
      HotkeyStatusText: hotkeyStatusText,
      RecordEnabled: sessionState == WorkbenchSessionState.Idle,
      StopEnabled: sessionState == WorkbenchSessionState.Recording);
  }

  private static UiStatusKind ResolveSessionStatusKind(WorkbenchSessionState sessionState, string sessionStatusText)
  {
    if (sessionStatusText.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
    {
      return UiStatusKind.Error;
    }

    if (sessionStatusText.StartsWith("Done:", StringComparison.OrdinalIgnoreCase))
    {
      return UiStatusKind.Success;
    }

    return sessionState switch
    {
      WorkbenchSessionState.Recording => UiStatusKind.Pending,
      WorkbenchSessionState.Transcribing => UiStatusKind.Pending,
      _ => UiStatusKind.Neutral,
    };
  }
}
