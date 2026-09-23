using System;

namespace DictateAnywhere.Core.Contracts;

public static class DictationStatusMessages
{
  public const string NoAudibleSpeechDetected = "No audible speech detected";
  public const string HotkeyUnavailable = "Hotkey unavailable · change it in Settings";
  public const string InsertFailedCopiedAndSaved = "Insert failed · copied + saved";
  public const string InsertFailedCopied = "Insert failed · copied";
  public const string InsertFailedSaved = "Insert failed · saved";
  public const string InsertFailedNotPreserved = "Insert failed · not preserved";

  public static TimeSpan TransientOutcomeDuration { get; } = TimeSpan.FromSeconds(4);
}
