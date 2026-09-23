using System;

namespace DictateAnywhere.App.Workbench;

internal static class WorkbenchComposerText
{
  public static string Append(string? existingText, string? addedText)
  {
    string existing = existingText?.TrimEnd() ?? string.Empty;
    string added = addedText?.Trim() ?? string.Empty;
    if (added.Length == 0)
    {
      return existingText ?? string.Empty;
    }

    return existing.Length == 0
      ? added
      : string.Concat(existing, Environment.NewLine, Environment.NewLine, added);
  }
}
