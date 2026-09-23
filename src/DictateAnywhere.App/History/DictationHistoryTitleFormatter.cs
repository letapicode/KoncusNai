using System;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal static class DictationHistoryTitleFormatter
{
  public static string FormatSidebarTitle(DictationHistoryRecord record)
  {
    ArgumentNullException.ThrowIfNull(record);

    DictationHistoryRecord normalized = record.Normalize();
    return string.IsNullOrWhiteSpace(normalized.Title)
      ? "Dictation"
      : normalized.Title;
  }
}
