using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Presentation;

internal sealed record ChatTypefaceOption(
  string Id,
  string DisplayName,
  string FontFamilyName)
{
  public override string ToString() => DisplayName;
}

internal static class ChatTypefaceCatalog
{
  internal static IReadOnlyList<ChatTypefaceOption> Options { get; } =
  [
    new(ChatTypefaceIds.System, "System", "Segoe UI"),
    new(ChatTypefaceIds.BookSerif, "Book Serif", "Georgia"),
    new(ChatTypefaceIds.LiterarySerif, "Literary Serif", "Palatino Linotype"),
    new(ChatTypefaceIds.ModernSerif, "Modern Serif", "Cambria"),
    new(ChatTypefaceIds.Excalifont, "Excalifont", "./Assets/Fonts/Excalifont/#Excalifont"),
    new(ChatTypefaceIds.Kalam, "Kalam", "./Assets/Fonts/Kalam/#Kalam"),
  ];

  internal static ChatTypefaceOption Resolve(string? typefaceId)
  {
    string normalized = ChatTypefaceSettings.Normalize(typefaceId);
    return Options.First(option => string.Equals(option.Id, normalized, StringComparison.Ordinal));
  }

  internal static FontFamily CreateFontFamily(string? typefaceId)
  {
    ChatTypefaceOption option = Resolve(typefaceId);
    return option.FontFamilyName.StartsWith("./", StringComparison.Ordinal)
      ? new FontFamily(new Uri("pack://application:,,,/", UriKind.Absolute), option.FontFamilyName)
      : new FontFamily(option.FontFamilyName);
  }
}
