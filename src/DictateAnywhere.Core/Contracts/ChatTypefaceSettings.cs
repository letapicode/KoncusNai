using System;

namespace DictateAnywhere.Core.Contracts;

/// <summary>Stable persisted identifiers for chat transcript typefaces.</summary>
public static class ChatTypefaceIds
{
  public const string System = "system";
  public const string BookSerif = "book-serif";
  public const string LiterarySerif = "literary-serif";
  public const string ModernSerif = "modern-serif";
  public const string Excalifont = "excalifont";
  public const string Kalam = "kalam";
}

public static class ChatTypefaceSettings
{
  public static string Normalize(string? typefaceId)
  {
    string normalized = (typefaceId ?? string.Empty).Trim().ToLowerInvariant();
    return normalized switch
    {
      ChatTypefaceIds.BookSerif => ChatTypefaceIds.BookSerif,
      ChatTypefaceIds.LiterarySerif => ChatTypefaceIds.LiterarySerif,
      ChatTypefaceIds.ModernSerif => ChatTypefaceIds.ModernSerif,
      ChatTypefaceIds.Excalifont => ChatTypefaceIds.Excalifont,
      ChatTypefaceIds.Kalam => ChatTypefaceIds.Kalam,
      _ => ChatTypefaceIds.System,
    };
  }
}
